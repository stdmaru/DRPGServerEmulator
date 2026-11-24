using System.Buffers.Binary;
using System.Collections.Frozen;
using DRPGServer.Configuration;
using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Stats;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DRPGServer.Tests;

public class TimedItemTests
{
    private static readonly ItemInfo Aura718 = new(718, "Aura T of Deva (for New Tamers)")
    {
        Type = 27, SubType = 2, Level = 1, MaxStack = 1, DurationSeconds = 2_593_000,
        EquipmentBonus = new EquipmentBonus(HpRecovery: 8, EvpRecovery: 13, MaintainEvolution: true),
    };

    private static readonly ItemInfo Crest1 = new(1, "Crest of Courage +1")
    {
        Type = 1, SubType = 1, Level = 1, MaxStack = 1, DurationSeconds = 0,
        EquipmentBonus = new EquipmentBonus(BonusAtk: 15, BonusDef: 10, BonusBr: 50),
    };

    private static readonly ItemInfo Bread25 = new(25, "Bread")
    {
        Type = 4, SubType = 1, Level = 1, MaxStack = 99, DurationSeconds = 0,
    };

    private static readonly Dictionary<uint, ItemInfo> TestItems = new()
    {
        [718] = Aura718,
        [1] = Crest1,
        [25] = Bread25,
    };

    private static (Player Player, Session Session) MakePlayer()
    {
        var inv = new Inventory(id => TestItems.GetValueOrDefault(id), 99);
        var species = TestSupport.Data.Species.Values.First(s => s.Stage == StatFormulas.InTraining);
        var leader = new Digimon(TestSupport.Data, species, 10);
        var character = new Character(leader, inv)
        {
            Uid = 10, AccountUid = 1, Slot = 0, Name = "Tamer", TamerId = 1, Level = 10, MapId = 79, X = 30, Y = 30,
        };
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "user", Authority.None), character);
        session.Player = player;
        return (player, session);
    }

    [Fact]
    public void GiveItems_supports_arbitrary_duration_permanent_and_default_duration()
    {
        var (player, _) = MakePlayer();
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 1. Período arbitrário: 1 dia (86400 s) de Aura #718
        Assert.True(player.GiveItems([(718, 86400)]));
        var oneDay = player.Character.Inventory.Main[0]!;
        Assert.True(oneDay.IsTimed);
        Assert.Equal(86400u, oneDay.DurationSeconds);
        Assert.InRange(oneDay.ExpiresAt, (uint)(before + 86395), (uint)(before + 86410));

        // 2. Permanente (-1): mesmo item #718 (que tem col 26 = 2593000) vira permanente
        Assert.True(player.GiveItems([(718, -1)]));
        var permanent = player.Character.Inventory.Main[1]!;
        Assert.False(permanent.IsTimed);
        Assert.Equal(0u, permanent.ExpiresAt);
        Assert.Equal(0u, permanent.DurationSeconds);

        // 3. Padrão (1): usa a duração padrão do ItemInfo (2593000 s)
        Assert.True(player.GiveItems([(718, 1)]));
        var defaultTimed = player.Character.Inventory.Main[2]!;
        Assert.True(defaultTimed.IsTimed);
        Assert.Equal(2_593_000u, defaultTimed.DurationSeconds);

        // 4. Item não empilhável originalmente permanente (#1) pode receber período arbitrário (3600 s)
        Assert.True(player.GiveItems([(1, 3600)]));
        var timedCrest = player.Character.Inventory.Main[3]!;
        Assert.True(timedCrest.IsTimed);
        Assert.Equal(3600u, timedCrest.DurationSeconds);

        // 5. Item empilhável (#25) trata o valor como quantidade normal (e rejeita -1)
        Assert.False(player.GiveItems([(25, -1)]));
        Assert.True(player.GiveItems([(25, 10)]));
        var bread = player.Character.Inventory.Main[4]!;
        Assert.False(bread.IsTimed);
        Assert.Equal(10u, bread.Amount);
    }

    [Fact]
    public void ItemRecord_and_CharacterData_0x02_encode_timestamps_at_official_offsets()
    {
        var (player, _) = MakePlayer();
        Assert.True(player.GiveItems([(718, 86400), (718, -1)]));
        var timed = player.Character.Inventory.Main[0]!;
        var perm = player.Character.Inventory.Main[1]!;

        // ItemRecord de item de período: +12 = ExpiresAt, +16 = DurationSeconds (> 999)
        var w1 = new PacketWriter();
        ItemRecord.Write(w1, timed);
        var recTimed = w1.ToFrame(0).AsSpan(PacketHeader.Length).ToArray();
        Assert.Equal(ItemRecord.Length, recTimed.Length);
        Assert.Equal(timed.ExpiresAt, BinaryPrimitives.ReadUInt32LittleEndian(recTimed.AsSpan(12)));
        Assert.Equal(86400u, BinaryPrimitives.ReadUInt32LittleEndian(recTimed.AsSpan(16)));

        // ItemRecord de item permanente (-1): +12 = 1, +16 = 1 (MaxStack)
        var w2 = new PacketWriter();
        ItemRecord.Write(w2, perm);
        var recPerm = w2.ToFrame(0).AsSpan(PacketHeader.Length).ToArray();
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(recPerm.AsSpan(12)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(recPerm.AsSpan(16)));

        // S2C 0x02 (7600 B): offset 0x19B0 (6576) contém o Unix timestamp UTC atual do servidor
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var p02 = new CharacterDataPacket(player.Character).EncodePayload();
        Assert.Equal(7600, p02.Length);
        uint serverTime = BinaryPrimitives.ReadUInt32LittleEndian(p02.AsSpan(0x19B0));
        Assert.InRange(serverTime, (uint)(now - 5), (uint)(now + 5));
    }

    [Fact]
    public void Expired_items_cannot_be_equipped_and_are_removed_on_zone_tick_and_login()
    {
        var (player, session) = MakePlayer();
        var inv = player.Character.Inventory;
        uint pastTs = (uint)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60);
        uint futureTs = (uint)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600);

        // 1. Guarda de segurança: item já expirado não pode ser equipado
        var expiredBagItem = inv.Create(718, 0, 1, expiresAt: pastTs, durationSeconds: 86400);
        inv.Main.Place(expiredBagItem);
        Assert.False(EquipmentSlots.CanEquip(ItemPlace.Aura, expiredBagItem, player.Character.Level));

        // 2. Expiração em tempo real no tick da zona: remove item vencido equipado e recalcula status
        var expiredEquip = inv.Create(1, (int)ItemPlace.Crest1, 1, expiresAt: pastTs, durationSeconds: 86400);
        inv.Equipment.Place(expiredEquip);
        player.Character.SyncEquipmentBonuses();
        int boostedAtk = player.Leader.Stats.Atk;

        var world = TestSupport.NewWorld();
        var zone = new ZoneSystem(TestSupport.Data, world, NullLogger<ZoneSystem>.Instance).Find(79)!;
        zone.Enter(player);
        session.SentOpcodes(); // limpa entrada

        zone.Update(Zone.RecoveryInterval + TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50));
        Assert.Null(inv.Main[0]);
        Assert.Null(inv.EquippedAt(ItemPlace.Crest1));
        Assert.Equal(boostedAtk - 15, player.Leader.Stats.Atk);
        var ops = session.SentOpcodes();
        Assert.Contains((ushort)MapOpcode.InventoryRefresh, ops);
        Assert.Contains((ushort)MapOpcode.TamerExp, ops);
        Assert.Contains((ushort)MapOpcode.BattleDigimonStatus, ops);

        // 3. Persistência e recarga: item válido permanece com ExpiresAt; item vencido é descartado no login
        var validTimed = inv.Create(718, (int)ItemPlace.Aura, 1, expiresAt: futureTs, durationSeconds: 3600);
        inv.Equipment.Place(validTimed);
        var snap = CharacterSnapshot.Of(player.Character);
        var itemsWithExpired = snap.Items.Concat([new ItemRow((byte)BagKind.Inventory, 2, 999, 718, 1, pastTs, 86400)]).ToList();

        var data = new GameData
        {
            Species = TestSupport.Data.Species,
            Items = TestItems.ToFrozenDictionary(),
            Cards = TestSupport.Data.Cards,
            DigimonTitles = TestSupport.Data.DigimonTitles,
            Skills = TestSupport.Data.Skills,
            Evolutions = TestSupport.Data.Evolutions,
            Maps = TestSupport.Data.Maps,
            Grids = TestSupport.Data.Grids,
            DropGroups = TestSupport.Data.DropGroups,
            TamingNets = TestSupport.Data.TamingNets,
            Portals = TestSupport.Data.Portals,
            Spawns = TestSupport.Data.Spawns,
            ZoneIds = TestSupport.Data.ZoneIds,
        };
        var species = TestSupport.Data.Species.Values.First(s => s.Stage == StatFormulas.InTraining);
        var factory = new CharacterFactory(data, Options.Create(new GameOptions()));
        var record = new CharacterRecord(
            new CharacterRow { Uid = 10, AccountUid = 1, Slot = 0, Name = "Tamer", TamerId = 1, Level = 10, MapId = 79, PosX = 30, PosY = 30 },
            [new DigimonRow { Uid = 100, Slot = 0, DigimonId = species.Id, Name = species.Name, Level = 10 }],
            [new FormRow(100, species.Id, 0, 0, 0, 0, 0, 0)],
            itemsWithExpired,
            []);
        var reloaded = factory.Build(record)!;

        Assert.Null(reloaded.Inventory.Main[2]); // expirado descartado na carga
        var reloadedAura = reloaded.Inventory.EquippedAt(ItemPlace.Aura)!;
        Assert.Equal(futureTs, reloadedAura.ExpiresAt);
        Assert.Equal(3600u, reloadedAura.DurationSeconds);
    }
}
