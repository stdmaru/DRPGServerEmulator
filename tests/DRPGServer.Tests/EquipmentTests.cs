using System.Buffers.Binary;
using System.Collections.Frozen;
using DRPGServer.Configuration;
using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Battle.Cards;
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

public class EquipmentTests
{
    private static readonly ItemInfo Crest107 = new(107, "Crest of Courage +5 (for Beginners)")
    {
        Type = 1, SubType = 1, Level = 1, MaxStack = 1,
        EquipmentBonus = new EquipmentBonus(BonusAtk: 75, BonusDef: 40, BonusBr: 350),
    };

    private static readonly ItemInfo Digimental215 = new(215, "Digimental of Miracles +5 (for Beginners)")
    {
        Type = 2, SubType = 1, Level = 1, MaxStack = 1,
        EquipmentBonus = new EquipmentBonus(Attributes: new Attributes(6, 6, 6, 0)),
    };

    private static readonly ItemInfo Dark442 = new(442, "Rookie's D-ark")
    {
        Type = 9, SubType = 1, Level = 1, MaxStack = 1,
        EquipmentBonus = new EquipmentBonus(CardSlashLimit: 23),
    };

    private static readonly ItemInfo HighLevelRing = new(668, "Ring of Hope +1")
    {
        Type = 25, SubType = 1, Level = 30, MaxStack = 1,
        EquipmentBonus = new EquipmentBonus(Attributes: new Attributes(5, 0, 0, 0)),
    };

    private static readonly ItemInfo Bread25 = new(25, "Bread")
    {
        Type = 4, SubType = 1, Level = 1, MaxStack = 99,
    };

    private static readonly ItemInfo Watch23552 = new(23552, "Smart Watch [30 Days]")
    {
        Type = 45, SubType = 6, Level = 1, MaxStack = 1,
        EquipmentBonus = new EquipmentBonus(BonusAtk: 25, BonusVp: 200, BonusBr: 100, BonusDefPercent: 3),
    };

    private static readonly Dictionary<uint, ItemInfo> TestItems = new()
    {
        [107] = Crest107,
        [215] = Digimental215,
        [442] = Dark442,
        [668] = HighLevelRing,
        [25] = Bread25,
        [23552] = Watch23552,
    };

    private static DigimonSpecies InTrainingSpecies =>
        TestSupport.Data.Species.Values.First(s => s.Stage == StatFormulas.InTraining);

    private static (Player Player, Session Session) MakePlayer(ushort tamerLevel = 1)
    {
        var inv = new Inventory(id => TestItems.GetValueOrDefault(id), 99, _ => null, TestSupport.Clock);
        var species = InTrainingSpecies;
        var leader = new Digimon(TestSupport.Data, species, 1); // L1 In-Training (10/10/10/10)
        var second = new Digimon(TestSupport.Data, species, 1);
        var character = new Character(leader, inv)
        {
            Uid = 10, AccountUid = 1, Slot = 0, Name = "Tamer", TamerId = 1, Level = tamerLevel, MapId = 79, X = 30, Y = 30,
        };
        character.Place(1, second);
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "user", Authority.None), character);
        session.Player = player;
        return (player, session);
    }

    private static PacketReader MovePacket(ItemPlace from, ItemPlace to, int fa, int fb, int ta, int tb, uint uid, uint itemId)
    {
        var w = new PacketWriter();
        new ItemMoveHeader(from, to, fa, fb, ta, tb).Write(w);
        w.WriteU32(uid);
        w.WriteU32(itemId);
        w.WriteZeros(250 - 28);
        return new PacketReader(w.ToFrame((ushort)MapOpcode.ItemDropGet).AsSpan(PacketHeader.Length).ToArray());
    }

    [Fact]
    public void EquipmentEffects_xml_loads_all_structured_bonuses_and_auras()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "Item", "EquipmentEffects.xml");
        var effects = GameDataLoader.LoadEquipmentEffects(path);
        Assert.True(effects.Count > 9000);

        // Crest #107
        Assert.Equal((75, 40, 350), (effects[107].BonusAtk, effects[107].BonusDef, effects[107].BonusBr));
        // Digimental #215
        Assert.Equal(new Attributes(6, 6, 6, 0), effects[215].Attributes);
        // Rookie's D-ark #442
        Assert.Equal(23, effects[442].CardSlashLimit);
        // Aura of High-Velocity #529
        Assert.Equal((1, 8, 15), (effects[529].AutoSummonPartners, effects[529].VpRecovery, effects[529].EvpRecovery));
        // Aura of Evolution #555
        Assert.True(effects[555].MaintainEvolution);
        // Aura T of Deva #718
        Assert.True(effects[718].MaintainEvolution);
        Assert.Equal((8, 13), (effects[718].HpRecovery, effects[718].EvpRecovery));
        // Smart Watch #23552
        Assert.Equal((25, 200, 100, 3), (effects[23552].BonusAtk, effects[23552].BonusVp, effects[23552].BonusBr, effects[23552].BonusDefPercent));
    }

    /// <summary>
    /// ✅ Confirmação exata contra captures.db (conn=234, InputName L1 10/10/10/10):
    /// - Sem equipamento: ATK 145, DEF 87, BR 131, HP 115, VP 115
    /// - +1× Crest 107: ATK 220, DEF 127, BR 481, HP 115, VP 115
    /// - +1× Digimental 215: ATK 258, DEF 145, BR 489, HP 146, VP 125
    /// - +2ª Crest 107: ATK 333, DEF 185, BR 839, HP 146, VP 125
    /// </summary>
    [Fact]
    public void Equipping_crests_and_digimental_matches_official_conn234_stats_and_packet_sequence()
    {
        var (player, session) = MakePlayer();
        var inv = player.Character.Inventory;
        var crest1 = inv.Add(107, 1)!;
        var egg1 = inv.Add(215, 1)!;
        var crest2 = inv.Add(107, 1)!;

        // Baseline sem equipamento
        var s0 = player.Leader.Stats;
        Assert.Equal((145, 87, 131, 115, 115), (s0.Atk, s0.Def, s0.Br, s0.MaxHp, s0.MaxVp));

        var handler = new ItemMoveHandler(TestSupport.Guard());

        // 1. Equipar Crest 107 no slot Crest1 (4)
        handler.Dispatch(session, MovePacket(ItemPlace.Inventory, ItemPlace.Crest1, crest1.Row, crest1.Column, 0, 0, crest1.Uid, 107));
        var s1 = player.Leader.Stats;
        Assert.Equal((220, 127, 481, 115, 115), (s1.Atk, s1.Def, s1.Br, s1.MaxHp, s1.MaxVp));
        // Também aplica ao 2º Digimon da party
        Assert.Equal(s1, player.Character.Party[1]!.Stats);
        // Sequência oficial de pacotes: 0xB1 → 0xB4 × 2 → 0x10 → 0x91
        Assert.Equal(
            [(ushort)MapOpcode.TamerExp, (ushort)MapOpcode.BattleDigimonStatus, (ushort)MapOpcode.BattleDigimonStatus,
             (ushort)MapOpcode.RefreshCharacterInfo, (ushort)MapOpcode.ItemDropGet],
            session.SentOpcodes());

        // 2. Equipar Digimental 215 no slot Digimental1 (7)
        handler.Dispatch(session, MovePacket(ItemPlace.Inventory, ItemPlace.Digimental1, egg1.Row, egg1.Column, 0, 0, egg1.Uid, 215));
        var s2 = player.Leader.Stats;
        Assert.Equal((258, 145, 489, 146, 125), (s2.Atk, s2.Def, s2.Br, s2.MaxHp, s2.MaxVp));

        // 3. Equipar 2ª Crest 107 no slot Crest2 (5)
        handler.Dispatch(session, MovePacket(ItemPlace.Inventory, ItemPlace.Crest2, crest2.Row, crest2.Column, 0, 0, crest2.Uid, 107));
        var s3 = player.Leader.Stats;
        Assert.Equal((333, 185, 839, 146, 125), (s3.Atk, s3.Def, s3.Br, s3.MaxHp, s3.MaxVp));

        // 4. Desequipar Crest1 volta para s2
        handler.Dispatch(session, MovePacket(ItemPlace.Crest1, ItemPlace.Inventory, 0, 0, 0, 0, crest1.Uid, 107));
        Assert.Null(inv.EquippedAt(ItemPlace.Crest1));
        Assert.Equal(crest1, inv.Main[0]);
        Assert.Equal(s2, player.Leader.Stats);
    }

    [Fact]
    public void CharacterData_0x02_and_TamerExp_0xB1_write_equipment_at_official_offsets()
    {
        var (player, _) = MakePlayer();
        var inv = player.Character.Inventory;
        var c1 = inv.Create(107, (int)ItemPlace.Crest1, 1);
        var e1 = inv.Create(215, (int)ItemPlace.Digimental1, 1);
        var dark = inv.Create(442, (int)ItemPlace.Digivice, 1);
        var watch = inv.Create(23552, (int)ItemPlace.Watch, 1);
        inv.Equipment.Place(c1);
        inv.Equipment.Place(e1);
        inv.Equipment.Place(dark);
        inv.Equipment.Place(watch);

        // S2C 0x02 (7600 B)
        var p02 = new CharacterDataPacket(player.Character, TestSupport.Clock.UnixNow).EncodePayload();
        Assert.Equal(7600, p02.Length);
        Assert.Equal(c1.Uid, BinaryPrimitives.ReadUInt32LittleEndian(p02.AsSpan(100)));
        Assert.Equal(107u, BinaryPrimitives.ReadUInt32LittleEndian(p02.AsSpan(104)));
        Assert.Equal(e1.Uid, BinaryPrimitives.ReadUInt32LittleEndian(p02.AsSpan(424)));
        Assert.Equal(215u, BinaryPrimitives.ReadUInt32LittleEndian(p02.AsSpan(428)));
        Assert.Equal(dark.Uid, BinaryPrimitives.ReadUInt32LittleEndian(p02.AsSpan(0x12E0)));
        Assert.Equal(442u, BinaryPrimitives.ReadUInt32LittleEndian(p02.AsSpan(0x12E4)));
        Assert.Equal(watch.Uid, BinaryPrimitives.ReadUInt32LittleEndian(p02.AsSpan(0x1CF0)));
        Assert.Equal(23552u, BinaryPrimitives.ReadUInt32LittleEndian(p02.AsSpan(0x1CF4)));

        // S2C 0xB1 (1252 B)
        var pb1 = new TamerExpPacket(player.Character).EncodePayload();
        Assert.Equal(1252, pb1.Length);
        Assert.Equal(c1.Uid, BinaryPrimitives.ReadUInt32LittleEndian(pb1.AsSpan(0x018)));
        Assert.Equal(e1.Uid, BinaryPrimitives.ReadUInt32LittleEndian(pb1.AsSpan(0x15C)));
        Assert.Equal(dark.Uid, BinaryPrimitives.ReadUInt32LittleEndian(pb1.AsSpan(0x2A0)));
    }

    [Fact]
    public void Security_guards_reject_invalid_equip_unequip_and_swaps()
    {
        var (player, session) = MakePlayer(tamerLevel: 5);
        var inv = player.Character.Inventory;
        var crest = inv.Add(107, 1)!;          // slot 0 (Type 1)
        var egg = inv.Add(215, 1)!;            // slot 1 (Type 2)
        var bread = inv.Add(25, 1)!;           // slot 2 (Type 4, consumível)
        var ring = inv.Add(668, 1)!;           // slot 3 (Type 25, nível 30)
        var handler = new ItemMoveHandler(TestSupport.Guard());

        // Tipo incompatível: Digimental (Type 2) em Crest1 (aceita só Type 1)
        handler.Dispatch(session, MovePacket(ItemPlace.Inventory, ItemPlace.Crest1, egg.Row, egg.Column, 0, 0, egg.Uid, 215));
        Assert.Null(inv.EquippedAt(ItemPlace.Crest1));
        Assert.Empty(session.SentOpcodes());

        // Consumível em slot de equipamento
        handler.Dispatch(session, MovePacket(ItemPlace.Inventory, ItemPlace.Ring, bread.Row, bread.Column, 0, 0, bread.Uid, 25));
        Assert.Null(inv.EquippedAt(ItemPlace.Ring));

        // Nível insuficiente (Tamer L5 vs Ring L30)
        handler.Dispatch(session, MovePacket(ItemPlace.Inventory, ItemPlace.Ring, ring.Row, ring.Column, 0, 0, ring.Uid, 668));
        Assert.Null(inv.EquippedAt(ItemPlace.Ring));

        // UID forjado / ItemId trocado
        handler.Dispatch(session, MovePacket(ItemPlace.Inventory, ItemPlace.Crest1, crest.Row, crest.Column, 0, 0, crest.Uid + 999, 107));
        handler.Dispatch(session, MovePacket(ItemPlace.Inventory, ItemPlace.Crest1, crest.Row, crest.Column, 0, 0, crest.Uid, 215));
        Assert.Null(inv.EquippedAt(ItemPlace.Crest1));

        // Coordenadas não-zero no slot de equipamento
        handler.Dispatch(session, MovePacket(ItemPlace.Inventory, ItemPlace.Crest1, crest.Row, crest.Column, 1, 0, crest.Uid, 107));
        Assert.Null(inv.EquippedAt(ItemPlace.Crest1));

        // Equipar válido e tentar desequipar em cima do pão (troca inválida: pão não pode ir para Crest1)
        handler.Dispatch(session, MovePacket(ItemPlace.Inventory, ItemPlace.Crest1, crest.Row, crest.Column, 0, 0, crest.Uid, 107));
        Assert.Equal(crest, inv.EquippedAt(ItemPlace.Crest1));
        session.SentOpcodes();

        handler.Dispatch(session, MovePacket(ItemPlace.Crest1, ItemPlace.Inventory, 0, 0, bread.Row, bread.Column, crest.Uid, 107));
        Assert.Equal(crest, inv.EquippedAt(ItemPlace.Crest1));
        Assert.Equal(bread, inv.Main[bread.Slot]);
        Assert.Empty(session.SentOpcodes());
    }

    [Fact]
    public void Equipment_persists_through_snapshot_and_factory_reload()
    {
        var (player, _) = MakePlayer();
        var inv = player.Character.Inventory;
        var c1 = inv.Create(107, (int)ItemPlace.Crest1, 1);
        var dark = inv.Create(442, (int)ItemPlace.Digivice, 1);
        inv.Equipment.Place(c1);
        inv.Equipment.Place(dark);
        player.Character.SyncEquipmentBonuses();

        var snap = CharacterSnapshot.Of(player.Character);
        Assert.Contains(snap.Items, i => i.Bag == (byte)BagKind.Equipment && i.Slot == (byte)ItemPlace.Crest1 && i.ItemId == 107);
        Assert.Contains(snap.Items, i => i.Bag == (byte)BagKind.Equipment && i.Slot == (byte)ItemPlace.Digivice && i.ItemId == 442);

        // Recarrega via CharacterFactory com GameData contendo os itens de teste
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
        var factory = new CharacterFactory(data, Options.Create(new GameOptions()), TestSupport.Clock);
        var species = InTrainingSpecies;
        var record = new CharacterRecord(
            new CharacterRow
            {
                Uid = 10, AccountUid = 1, Slot = 0, Name = "Tamer", TamerId = 1, Level = 1,
                MapId = 79, PosX = 30, PosY = 30, DepositorySlots = 10,
            },
            [new DigimonRow { Uid = 100, Slot = 0, DigimonId = species.Id, Name = species.Name, Level = 1 }],
            [new FormRow(100, species.Id, 0, 0, 0, 0, 0, 0)],
            snap.Items,
            [],
            []);
        var reloaded = factory.Build(record)!;

        Assert.Equal(107u, reloaded.Inventory.EquippedAt(ItemPlace.Crest1)?.ItemId);
        Assert.Equal(442u, reloaded.Inventory.EquippedAt(ItemPlace.Digivice)?.ItemId);
        Assert.Equal(220, reloaded.Leader.Stats.Atk); // 145 base + 75 Crest
        Assert.Equal(23, reloaded.TotalEquipmentBonus.CardSlashLimit);
        Assert.Equal(23, new CardBattleState(reloaded.TotalEquipmentBonus.CardSlashLimit).SlashesRemaining);
    }

    [Fact]
    public void Aura_recovery_bonus_applies_to_zone_auto_recovery_tick()
    {
        var auraDeva = new ItemInfo(718, "Aura T of Deva")
        {
            Type = 27, SubType = 2, Level = 1, MaxStack = 1,
            EquipmentBonus = new EquipmentBonus(HpRecovery: 8, VpRecovery: 4, EvpRecovery: 13),
        };
        var items = new Dictionary<uint, ItemInfo>(TestItems) { [718] = auraDeva };
        var inv = new Inventory(id => items.GetValueOrDefault(id), 99, _ => null, TestSupport.Clock);
        inv.Equipment.Place(inv.Create(718, (int)ItemPlace.Aura, 1));

        var leader = new Digimon(TestSupport.Data, InTrainingSpecies, 10);
        leader.Load(0, 0, 0, 0, hp: leader.Stats.MaxHp - 50, vp: leader.Stats.MaxVp - 30, evp: leader.Stats.MaxEvp - 40);
        var (hp0, vp0, evp0) = (leader.Hp, leader.Vp, leader.Evp);

        var character = new Character(leader, inv)
        {
            Uid = 10, AccountUid = 1, Slot = 0, Name = "Tamer", TamerId = 1, Level = 10, MapId = 79, X = 30, Y = 30,
        };
        var world = TestSupport.NewWorld();
        var zone = new ZoneSystem(TestSupport.Data, world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance).Find(79)!;
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);
        session.Player = player;
        zone.Enter(player);

        // Avança além do RecoveryInterval (5.2s) -> deve curar base (5/3/10) + aura (8/4/13) = 13/7/23
        zone.Update(Zone.RecoveryInterval + TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50));
        Assert.Equal(hp0 + 13, leader.Hp);
        Assert.Equal(vp0 + 7, leader.Vp);
        Assert.Equal(evp0 + 23, leader.Evp);
    }

    [Fact]
    public void Aura_maintains_evolution_across_battle_end_and_devolves_on_unequip()
    {
        var auraDeva = new ItemInfo(718, "Aura T of Deva")
        {
            Type = 27, SubType = 2, Level = 1, MaxStack = 1,
            EquipmentBonus = new EquipmentBonus(MaintainEvolution: true),
        };
        var items = new Dictionary<uint, ItemInfo>(TestItems) { [718] = auraDeva };
        var inv = new Inventory(id => items.GetValueOrDefault(id), 99, _ => null, TestSupport.Clock);
        var auraItem = inv.Create(718, (int)ItemPlace.Aura, 1);
        inv.Equipment.Place(auraItem);

        // Encontra espécie que possua evolução na tabela de testes
        var baseSpecies = TestSupport.Data.Species.Values.First(s => TestSupport.Data.NextForm(s) is not null);
        var evolvedSpecies = TestSupport.Data.NextForm(baseSpecies)!;
        var leader = new Digimon(TestSupport.Data, baseSpecies, 21);

        var character = new Character(leader, inv)
        {
            Uid = 10, AccountUid = 1, Slot = 0, Name = "Tamer", TamerId = 1, Level = 21, MapId = 79, X = 30, Y = 30,
        };
        var world = TestSupport.NewWorld();
        var zone = new ZoneSystem(TestSupport.Data, world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance).Find(79)!;
        var battles = TestSupport.NewBattles(world, TestSupport.Data, TestSupport.GameOptions());
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);
        session.Player = player;
        zone.Enter(player);

        var battle = battles.StartPve(player, player.StandBy(zone.WildDigimons.First()))!;
        Assert.True(leader.EvolveTo(evolvedSpecies));
        Assert.True(leader.IsEvolved);

        // Vitória com Aura equipada -> permanece evoluído
        battle.KnockOut(BattleSide.B);
        Assert.True(leader.IsEvolved);
        Assert.Equal(evolvedSpecies.Id, leader.SpeciesId);

        // Ao desequipar a Aura no mapa -> reverte para a forma-base
        new ItemMoveHandler(TestSupport.Guard()).Dispatch(session, MovePacket(ItemPlace.Aura, ItemPlace.Inventory, 0, 0, 0, 0, auraItem.Uid, 718));
        Assert.False(leader.IsEvolved);
        Assert.Equal(baseSpecies.Id, leader.SpeciesId);
    }

    [Fact]
    public void High_velocity_aura_auto_summons_partner_on_battle_start()
    {
        var highVel = new ItemInfo(529, "Aura of High-Velocity")
        {
            Type = 8, SubType = 3, Level = 1, MaxStack = 1,
            EquipmentBonus = new EquipmentBonus(VpRecovery: 8, EvpRecovery: 15, AutoSummonPartners: 1),
        };
        var items = new Dictionary<uint, ItemInfo>(TestItems) { [529] = highVel };
        var inv = new Inventory(id => items.GetValueOrDefault(id), 99, _ => null, TestSupport.Clock);
        inv.Equipment.Place(inv.Create(529, (int)ItemPlace.Aura, 1));

        var leader = new Digimon(TestSupport.Data, InTrainingSpecies, 5);
        var partner = new Digimon(TestSupport.Data, InTrainingSpecies, 5);
        var character = new Character(leader, inv)
        {
            Uid = 10, AccountUid = 1, Slot = 0, Name = "Tamer", TamerId = 1, Level = 5, MapId = 79, X = 30, Y = 30,
        };
        character.Place(1, partner);

        var world = TestSupport.NewWorld();
        var zone = new ZoneSystem(TestSupport.Data, world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance).Find(79)!;
        var battles = TestSupport.NewBattles(world, TestSupport.Data, TestSupport.GameOptions());
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);
        session.Player = player;
        zone.Enter(player);

        var battle = battles.StartPve(player, player.StandBy(zone.WildDigimons.First()))!;
        Assert.Equal(2, battle.TeamA.Count);
        Assert.Contains(battle.TeamA, c => c.Digimon == leader);
        Assert.Contains(battle.TeamA, c => c.Digimon == partner);
    }
}
