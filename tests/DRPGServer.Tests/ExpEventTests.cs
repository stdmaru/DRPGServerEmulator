using System.Buffers.Binary;
using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Commands;
using DRPGServer.Game.Entities;
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
    }

    [Fact]
    public void ExpEventService_notifies_only_when_event_changes_unless_forced()
    {
        var service = new ExpEventService(null, TestSupport.GameOptions(), NullLogger<ExpEventService>.Instance);
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
        var expEvents = new ExpEventService(null, options, NullLogger<ExpEventService>.Instance);
        expEvents.Apply(new ActiveExpEvent(1, "Evento +500%", 500, false, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1)));

        var battles = new BattleSystem(world, TestSupport.Data, options, NullLogger<BattleSystem>.Instance, expEvents: expEvents);
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
    public void ReloadEvents_command_requires_administrator()
    {
        var world = TestSupport.NewWorld();
        var zones = new ZoneSystem(TestSupport.Data, world, NullLogger<ZoneSystem>.Instance);
        var expEvents = new ExpEventService(null, TestSupport.GameOptions(), NullLogger<ExpEventService>.Instance);
        var commands = new ChatCommands(zones, TestSupport.GameOptions(), NullLogger<ChatCommands>.Instance, expEvents);

        var gm = new Player(TestSupport.NewSession(ServerRole.Map), new AuthenticatedAccount(1, "gm", Authority.GameMaster), Entities.Character());
        var admin = new Player(TestSupport.NewSession(ServerRole.Map), new AuthenticatedAccount(2, "adm", Authority.Administrator), Entities.Character());

        Assert.False(commands.TryExecute(gm, "reloadevents"));
        Assert.True(commands.TryExecute(admin, "reloadevents"));
    }
}
