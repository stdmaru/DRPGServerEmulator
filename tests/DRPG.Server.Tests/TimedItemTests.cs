using System.Buffers.Binary;
using System.Collections.Frozen;
using DRPG.Server.Configuration;
using DRPG.Server.Data.GameData;
using DRPG.Server.Data.Repositories;
using DRPG.Server.Game.Entities;
using DRPG.Server.Game.Items;
using DRPG.Server.Game.Stats;
using DRPG.Server.Game.World;
using DRPG.Server.Network;
using DRPG.Server.Network.Handlers;
using DRPG.Server.Network.Packets.Map;
using DRPG.Shared.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DRPG.Server.Tests;

public class TimedItemTests
{
    private static readonly ItemInfo Aura718 = new(718, "Aura T of Deva (for New Tamers)")
    {
        Type = 27, SubType = 2, Level = 1, MaxStack = 1, DurationSeconds = 2_593_000,
        Modifiers = StatModifiers.Of([new(StatModifierKind.HpRecovery, 8), new(StatModifierKind.EvpRecovery, 13), new(StatModifierKind.MaintainEvolution, 1)]),
    };

    private static readonly ItemInfo Crest1 = new(1, "Crest of Courage +1")
    {
        Type = 1, SubType = 1, Level = 1, MaxStack = 1, DurationSeconds = 0,
        Modifiers = StatModifiers.Of([new(StatModifierKind.Atk, 15), new(StatModifierKind.Def, 10), new(StatModifierKind.Br, 50)]),
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

    private static readonly ItemService Items = TestSupport.Items();

    private static (Player Player, Session Session) MakePlayer()
    {
        var inv = new Inventory(id => TestItems.GetValueOrDefault(id), 99, _ => null, TestSupport.Clock);
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
    public void Timed_item_expiry_follows_the_game_clock()
    {
        var time = new ManualTime(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        IGameClock clock = new GameClock(time);
        var inv = new Inventory(id => TestItems.GetValueOrDefault(id), 99, _ => null, clock);

        var aura = inv.Main.Add(718, 1).Single();
        Assert.Equal(1_800_000_000u + Aura718.DurationSeconds, aura.ExpiresAt);
        Assert.False(inv.IsExpired(aura));

        time.Now = time.Now.AddSeconds(Aura718.DurationSeconds - 1);
        Assert.False(inv.IsExpired(aura));
        time.Now = time.Now.AddSeconds(1);
        Assert.True(inv.IsExpired(aura));
        Assert.False(EquipmentSlots.CanEquip(ItemPlace.Aura, aura, 10, clock.UnixNow));
    }

    [Fact]
    public void Character_data_writes_the_given_server_time()
    {
        var (player, _) = MakePlayer();
        var payload = TwoWayPacket.EncodePayload(CharacterData.Of(player.Character, 1_800_000_123));
        Assert.Equal(1_800_000_123u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0x19B0)));
    }

    [Fact]
    public void GiveItems_supports_arbitrary_duration_permanent_and_default_duration()
    {
        var (player, _) = MakePlayer();
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 1. Período arbitrário: 1 dia (86400 s) de Aura #718
        Assert.True(Items.GiveItems(player, [new(718, Duration: ItemDuration.FromDays(1))], TestSupport.Reason));
        var oneDay = player.Character.Inventory.Main[0]!;
        Assert.True(oneDay.IsTimed);
        Assert.InRange(oneDay.ExpiresAt, (uint)(before + 86395), (uint)(before + 86410));

        // 2. Permanente: mesmo item #718 (que tem col 26 = 2593000) vira permanente
        Assert.True(Items.GiveItems(player, [new(718, Duration: ItemDuration.Permanent)], TestSupport.Reason));
        var permanent = player.Character.Inventory.Main[1]!;
        Assert.False(permanent.IsTimed);
        Assert.Equal(0u, permanent.ExpiresAt);

        // 3. Padrão: usa a duração padrão do ItemInfo (2593000 s)
        Assert.True(Items.GiveItems(player, [new(718)], TestSupport.Reason));
        var defaultTimed = player.Character.Inventory.Main[2]!;
        Assert.True(defaultTimed.IsTimed);
        Assert.InRange(defaultTimed.ExpiresAt, (uint)(before + 2_592_995), (uint)(before + 2_593_010));

        // 4. Item não empilhável originalmente permanente (#1) pode receber período arbitrário (3600 s)
        Assert.True(Items.GiveItems(player, [new(1, Duration: ItemDuration.FromSeconds(3600))], TestSupport.Reason));
        var timedCrest = player.Character.Inventory.Main[3]!;
        Assert.True(timedCrest.IsTimed);
        Assert.InRange(timedCrest.ExpiresAt, (uint)(before + 3595), (uint)(before + 3610));

        // 5. Item empilhável (#25) recebe quantidade e não aceita prazo
        Assert.False(Items.GiveItems(player, [new(25, Duration: ItemDuration.Permanent)], TestSupport.Reason));
        Assert.True(Items.GiveItems(player, [new(25, 10)], TestSupport.Reason));
        var bread = player.Character.Inventory.Main[4]!;
        Assert.False(bread.IsTimed);
        Assert.Equal(10u, bread.Amount);
    }

    [Fact]
    public void ItemRecord_and_CharacterData_0x02_encode_timestamps_at_official_offsets()
    {
        var (player, _) = MakePlayer();
        Assert.True(Items.GiveItems(player, [new(718, Duration: ItemDuration.FromDays(1)), new(718, Duration: ItemDuration.Permanent)], TestSupport.Reason));
        var timed = player.Character.Inventory.Main[0]!;
        var perm = player.Character.Inventory.Main[1]!;

        // ItemRecord de item de período: +12 = ExpiresAt, +16 >= 1000
        var w1 = new PacketWriter();
        ItemRecord.Write(w1, timed);
        var recTimed = w1.ToFrame(0).AsSpan(PacketHeader.Length).ToArray();
        Assert.Equal(ItemRecord.Length, recTimed.Length);
        Assert.Equal(timed.ExpiresAt, BinaryPrimitives.ReadUInt32LittleEndian(recTimed.AsSpan(12)));
        Assert.Equal(2_593_000u, BinaryPrimitives.ReadUInt32LittleEndian(recTimed.AsSpan(16)));

        // ItemRecord de item permanente (-1): +12 = 1, +16 = 1 (MaxStack)
        var w2 = new PacketWriter();
        ItemRecord.Write(w2, perm);
        var recPerm = w2.ToFrame(0).AsSpan(PacketHeader.Length).ToArray();
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(recPerm.AsSpan(12)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(recPerm.AsSpan(16)));

        // S2C 0x02 (7600 B): offset 0x19B0 (6576) contém o Unix timestamp UTC atual do servidor
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var p02 = TwoWayPacket.EncodePayload(CharacterData.Of(player.Character, TestSupport.Clock.UnixNow));
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
        var expiredBagItem = inv.Create(718, 0, 1, expiresAt: pastTs);
        inv.Main.Place(expiredBagItem);
        Assert.False(EquipmentSlots.CanEquip(ItemPlace.Aura, expiredBagItem, player.Character.Level, TestSupport.Clock.UnixNow));

        // 2. Expiração em tempo real no tick da zona: remove item vencido equipado e recalcula status
        var expiredEquip = inv.Create(1, (int)ItemPlace.Crest1, 1, expiresAt: pastTs);
        inv.Equipment.Place(expiredEquip);
        player.Character.SyncEquipmentModifiers();
        int boostedAtk = player.Leader.Stats.Atk;

        var world = TestSupport.NewWorld();
        var zone = new ZoneSystem(TestSupport.Data, world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance).Find(79)!;
        zone.Enter(player);
        session.SentOpcodes(); // limpa entrada

        zone.Update(Zone.RecoveryInterval + TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50));
        Assert.Null(inv.Main[0]);
        Assert.Null(inv.EquippedAt(ItemPlace.Crest1));
        Assert.Equal(boostedAtk - 15, player.Leader.Stats.Atk);
        var ops = session.SentOpcodes();
        // EquipmentService: 0xFC da mochila, depois a sequência de troca de equipamento (0xB1 → 0xB4 por Digimon → 0x10)
        ushort[] sequence = [(ushort)MapOpcode.InventoryRefresh, (ushort)MapOpcode.TamerExp,
            (ushort)MapOpcode.BattleDigimonStatus, (ushort)MapOpcode.RefreshCharacterInfo];
        Assert.Equal(sequence, ops.Where(sequence.Contains));

        // 3. Persistência e recarga: item válido permanece com ExpiresAt; item vencido é descartado no login
        var validTimed = inv.Create(718, (int)ItemPlace.Aura, 1, expiresAt: futureTs);
        inv.Equipment.Place(validTimed);
        var snap = CharacterSnapshot.Of(player.Character);
        var itemsWithExpired = snap.Items.Concat([new ItemRow((byte)BagKind.Inventory, 2, 999, 718, 1, pastTs)]).ToList();

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
            Portals = TestSupport.Data.Portals,
            Spawns = TestSupport.Data.Spawns,
            ZoneIds = TestSupport.Data.ZoneIds,
        };
        var species = TestSupport.Data.Species.Values.First(s => s.Stage == StatFormulas.InTraining);
        var factory = new CharacterFactory(data, Options.Create(new GameOptions()), TestSupport.Clock);
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
    }

    [Fact]
    public void ExchangeItems_supports_timed_and_permanent_and_GiveCards_accepts_batch()
    {
        var (player, session) = MakePlayer();
        Assert.True(Items.GiveItems(player, [new(25, 5)], TestSupport.Reason, refresh: false));
        session.SentOpcodes();

        // a troca aceita permanente e prazo arbitrário no mesmo lote
        Assert.True(Items.Exchange(player, 25, 5, [new(718, Duration: ItemDuration.Permanent), new(1, Duration: ItemDuration.FromDays(1))], TestSupport.Reason));
        Assert.Equal(0u, player.Character.Inventory.Main.Count(25));
        var permAura = player.Character.Inventory.Main.Find(718)!;
        var timedCrest = player.Character.Inventory.Main.Find(1)!;
        Assert.False(permAura.IsTimed);
        Assert.True(timedCrest.IsTimed);

        // GiveCards entrega múltiplos cards em um único pacote 0xC8
        session.SentOpcodes();
        Assert.True(Items.GiveCards(player, [new(74, 50), new(19, 10)], TestSupport.Reason, refresh: false));
        Assert.Equal(50u, player.Character.Inventory.Battle.Count(74));
        Assert.Equal(10u, player.Character.Inventory.Battle.Count(19));
        Assert.Equal([(ushort)MapOpcode.BattleItemReceive], session.SentOpcodes());

        // GiveCards rejeita quantidade 0 sem alterar nada
        Assert.False(Items.GiveCards(player, [new(74), new(19, 0)], TestSupport.Reason, refresh: false));
        Assert.Equal(50u, player.Character.Inventory.Battle.Count(74));
    }

    [Fact]
    public void Non_stackable_grant_amount_is_one_slot_each_with_the_same_duration()
    {
        var (player, _) = MakePlayer();
        Assert.True(Items.GiveItems(player, [new(718, 3, ItemDuration.FromDays(1))], TestSupport.Reason, refresh: false));

        var auras = player.Character.Inventory.Main.Items.Where(i => i.ItemId == 718).ToList();
        Assert.Equal(3, auras.Count);
        Assert.All(auras, a => Assert.Equal((1u, auras[0].ExpiresAt), (a.Amount, a.ExpiresAt)));
    }

    [Fact]
    public void Invalid_or_oversized_grant_changes_nothing()
    {
        var (player, session) = MakePlayer();
        var bag = player.Character.Inventory.Main;
        Assert.True(Items.GiveItems(player, [new(1, (uint)bag.Capacity - 1)], TestSupport.Reason, refresh: false));
        session.SentOpcodes();

        Assert.False(Items.GiveItems(player, [new(718, 2)], TestSupport.Reason));          // uma posição livre
        Assert.False(Items.GiveItems(player, [new(25, 0)], TestSupport.Reason));
        Assert.False(Items.GiveCards(player, [new(74, 1, ItemDuration.Permanent)], TestSupport.Reason));
        Assert.Throws<ArgumentOutOfRangeException>(() => ItemDuration.FromSeconds(0));

        Assert.Equal(bag.Capacity - 1, bag.Items.Count());
        Assert.Empty(session.SentOpcodes());
        Assert.Equal(bag.Capacity - 1, player.Character.Economy.Pending.Single().Amount);
    }

    [Fact]
    public void Exchange_can_use_the_slot_it_frees_and_rolls_back_when_it_does_not_fit()
    {
        var (player, _) = MakePlayer();
        var bag = player.Character.Inventory.Main;
        Assert.True(Items.GiveItems(player, [new(1, (uint)bag.Capacity - 1), new(25, 5)], TestSupport.Reason, refresh: false));

        Assert.False(Items.Exchange(player, 25, 5, [new(718, 2)], TestSupport.Reason));   // só a posição do pão
        Assert.Equal(5u, bag.Count(25));

        Assert.True(Items.Exchange(player, 25, 5, [new(718)], TestSupport.Reason));
        Assert.Equal((0u, 1u), (bag.Count(25), bag.Count(718)));
    }
}
