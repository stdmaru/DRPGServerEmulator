using System.Buffers.Binary;
using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Commands;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Shop;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

public class ExpEventTests
{
    [Fact]
    public void ExpEventPacket_encrypts_with_map_session_key_matching_official_v322_layout()
    {
        // Semente 0x0E real da conexão 92 (sessão 53) e ciphertext real do 0xE6 (+1200% até 2026-11-23)
        byte[] helloSeed = Convert.FromHexString("C56EB87907D6313C73B3781043A5BB36");
        byte[] expectedCipher = Convert.FromHexString("345FAFF6443AD0F12037CCCB3C95DA071973DAE58B8C0467C23DF04B1B6F85C5");
        var key = GlobalSessionCipher.DeriveKey(helloSeed);

        var ev = new ActiveExpEvent(1, "Evento +1200%", BonusPercent: 1200, IsHotTime: false,
            new DateTime(2026, 11, 20), new DateTime(2026, 11, 23));

        var frame = new ExpEventPacket(key, ev).Encode();
        var header = PacketHeader.Read(frame);
        Assert.Equal((ushort)MapOpcode.ExpEvent, header.Opcode);
        Assert.Equal(32, frame.Length - PacketHeader.Length);

        var cipher = frame[PacketHeader.Length..];
        var plain = GlobalSessionCipher.DecryptBlocks(key, cipher);
        Assert.Equal((byte)48, plain[0]); // 1200 / 25 = 48
        Assert.Equal(2026u, BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(4, 4)));
        Assert.Equal(11u, BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(8, 4)));
        Assert.Equal(23u, BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(12, 4)));
        Assert.Equal((byte)0, plain[16]); // isHotTime = 0
        Assert.Equal((byte)0, plain[17]); // mode = 0
        Assert.Equal(expectedCipher[..24], cipher[..24]); // 24 bytes iniciais idênticos à captura real!
    }

    [Fact]
    public void ExpEventPacket_turns_off_badge_when_null_and_avoids_ignored_year_2038()
    {
        var key = GlobalSessionCipher.DeriveKey(new byte[16]);

        var offFrame = new ExpEventPacket(key, null).Encode();
        var offPlain = GlobalSessionCipher.DecryptBlocks(key, offFrame[PacketHeader.Length..]);
        Assert.All(offPlain, b => Assert.Equal((byte)0, b));

        var y2038 = new ActiveExpEvent(2, "Longo", BonusPercent: 500, IsHotTime: true,
            new DateTime(2026, 1, 1), new DateTime(2038, 12, 31));
        var onFrame = new ExpEventPacket(key, y2038).Encode();
        var onPlain = GlobalSessionCipher.DecryptBlocks(key, onFrame[PacketHeader.Length..]);
        Assert.Equal((byte)20, onPlain[0]);
        Assert.Equal(2037u, BinaryPrimitives.ReadUInt32LittleEndian(onPlain.AsSpan(4, 4)));
        Assert.Equal((byte)1, onPlain[16]);

        // Clamp do sprite acima do limite do cliente (80 = +2000% normal, 28 = +700% hot time), mantendo ExpMultiplier integral
        var hugeNormal = new ActiveExpEvent(3, "3000%", BonusPercent: 3000, IsHotTime: false, DateTime.UtcNow, DateTime.UtcNow.AddDays(1));
        Assert.Equal((byte)80, hugeNormal.SpriteIndex);
        Assert.Equal(31.0, hugeNormal.ExpMultiplier);

        var hugeHot = new ActiveExpEvent(4, "Hot 1000%", BonusPercent: 1000, IsHotTime: true, DateTime.UtcNow, DateTime.UtcNow.AddDays(1));
        Assert.Equal((byte)28, hugeHot.SpriteIndex);
        Assert.Equal(11.0, hugeHot.ExpMultiplier);
    }

    [Fact]
    public void ExpEventService_notifies_only_when_event_changes_unless_forced()
    {
        var service = TestSupport.NewExpEvents(TestSupport.GameOptions());
        var events = new List<ActiveExpEvent?>();
        service.Changed += e => events.Add(e);

        var ev = new ActiveExpEvent(1, "FDS", 500, false, new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));
        Assert.True(service.Apply(ev));
        Assert.Equal(6.0, service.ExpMultiplier);
        Assert.Single(events);

        // Mesmo evento -> não reenvia
        Assert.False(service.Apply(ev));
        Assert.Single(events);

        // Reload forçado (/reloadevents) -> notifica mesmo igual
        Assert.True(service.Apply(ev, forceNotify: true));
        Assert.Equal(2, events.Count);

        // Encerramento -> volta a 1.0x e notifica null
        Assert.True(service.Apply(null));
        Assert.Equal(1.0, service.ExpMultiplier);
        Assert.Null(events[^1]);
    }

    [Fact]
    public void Battle_rewards_apply_active_exp_event_multiplier()
    {
        var world = TestSupport.NewWorld();
        var options = TestSupport.GameOptions(o => { o.ExpRate = 2; o.AllowMultiLevelUp = true; });
        var expEvents = TestSupport.NewExpEvents(options);
        expEvents.Apply(new ActiveExpEvent(1, "Evento +500%", 500, false, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1)));

        var battles = TestSupport.NewBattles(world, TestSupport.Data, options, expEvents: expEvents);
        var session = TestSupport.NewSession(ServerRole.Map);
        var character = Entities.Character();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);
        session.Player = player;

        var digimon = character.Leader;
        var combatant = new Combatant(digimon, BattleSide.A, player, null);
        var wildOption = new SpawnOption(14, 1, ExpReward: 10, BitReward: 5, AppearanceRate: 100, DropGroup: 0);
        var defeated = new WildMember(new Digimon(TestSupport.Data, TestSupport.Data.Species[14], 1), wildOption);

        // ExpReward(10) * ExpRate(2) * EventMultiplier(6.0) = 120 EXP
        battles.GrantRewards(player, combatant, [combatant], defeated);

        var rewardFrame = session.DrainOutgoing().Single(f => PacketHeader.Read(f).Opcode == 0x6E);
        long sentExp = BitConverter.ToInt64(rewardFrame, PacketHeader.Length + 16);
        Assert.Equal(120, sentExp);
    }

    [Fact]
    public async Task ReloadEvents_command_requires_administrator_and_replies_from_the_world()
    {
        await using var world = new WorldHarness();
        await world.StartAsync();
        var zones = new ZoneSystem(TestSupport.Data, world.World, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
        var store = new FakeExpEventStore();
        var expEvents = TestSupport.NewExpEvents(store: store);
        var cashShop = TestSupport.NewCashShop(world, new InMemoryCashStore());
        var commands = new ChatCommands(zones, world.Io, TestSupport.GameOptions(), NullLogger<ChatCommands>.Instance, expEvents, cashShop);

        var gmSession = TestSupport.NewSession(ServerRole.Map);
        var adminSession = TestSupport.NewSession(ServerRole.Map);
        var gm = new Player(gmSession, new AuthenticatedAccount(1, "gm", Authority.GameMaster), Entities.Character());
        var admin = new Player(adminSession, new AuthenticatedAccount(2, "adm", Authority.Administrator), Entities.Character());

        bool gmRan = true, adminRan = false;
        await world.RunAsync(() =>
        {
            gmRan = commands.TryExecute(gm, "reloadevents");
            adminRan = commands.TryExecute(admin, "reloadevents");
        });

        Assert.False(gmRan);
        Assert.True(adminRan);
        Assert.Equal(1, store.Queries);
        Assert.Empty(gmSession.SentOpcodes());
        Assert.Equal([(ushort)MapOpcode.ChatNormal], adminSession.SentOpcodes());
    }
}
