using System.Buffers.Binary;
using DRPG.Server.Data.Repositories;
using DRPG.Server.Game;
using DRPG.Server.Game.Battle;
using DRPG.Server.Game.Digimons;
using DRPG.Server.Game.Entities;
using DRPG.Server.Game.Items;
using DRPG.Server.Game.Stats;
using DRPG.Server.Game.World;
using DRPG.Server.Network;
using DRPG.Server.Network.Handlers;
using DRPG.Server.Network.Packets.Map;
using DRPG.Shared.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPG.Server.Tests;

/// <summary>
/// Área inicial (sessões 53 e 56) sobre a fixture sintética: Seedmon (14, in-training) → Rookiemon (50) → Champmon (80).
/// </summary>
public class TutorialTests
{
    private readonly GameWorld _world = TestSupport.NewWorld();
    private readonly ZoneSystem _zones;
    private readonly BattleSystem _battles;
    private readonly Session _session = TestSupport.NewSession();
    private readonly Player _player;

    public TutorialTests()
    {
        _zones = new ZoneSystem(TestSupport.Data, _world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
        _battles = TestSupport.NewBattles(_world, TestSupport.Data, TestSupport.GameOptions());
        _player = new Player(_session, new AuthenticatedAccount(1, "u", Authority.GameMaster), Entities.Character());
        _session.Player = _player;
        _zones.Find(79)!.Enter(_player);
        _session.DrainOutgoing();
    }

    private static Digimon NewDigimon(ushort species, ushort level, uint uid) =>
        new(TestSupport.Data, TestSupport.Data.Species[species], level) { Uid = uid };

    private static Character NewCharacter(params Digimon[] party)
    {
        var c = new Character(party[0], new Inventory(TestSupport.Data.FindItem, 999, _ => null, TestSupport.Clock))
        {
            Uid = 1, AccountUid = 1, Slot = 0, Name = "Tester", TamerId = 1, MapId = 79, X = 56, Y = 56,
        };
        for (int i = 1; i < party.Length; i++) Assert.True(c.Place(i, party[i]));
        return c;
    }

    private Player Join(Character character)
    {
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(2, "v", Authority.GameMaster), character);
        _zones.Find(79)!.Enter(player);
        session.DrainOutgoing();
        return player;
    }

    // ---------- evolução ----------

    [Fact]
    public void In_training_becomes_rookie_at_level_11_and_does_not_go_back()
    {
        var d = NewDigimon(14, 10, 7);
        var player = Join(NewCharacter(d));
        Progression.GainExp(player, d, d.ExpToNextLevel, 0, 160, inBattle: false);
        // ✅ 0x6E, 0x70 com a forma antiga, 0x80 com a nova
        Assert.Equal([(ushort)0x6E, (ushort)0x70, (ushort)0x80], player.Session.SentOpcodes());
        Assert.Equal(11, d.Level);
        Assert.Equal(50, d.BaseForm.Species.Id);
        Assert.False(d.IsEvolved);
        Assert.Null(d.FindForm(14));
        Assert.False(d.Devolve());
    }

    [Fact]
    public void Rookie_evolves_on_its_own_at_21_only_in_battle()
    {
        var inBattle = NewDigimon(50, 20, 7);
        var player = Join(NewCharacter(inBattle));
        Progression.GainExp(player, inBattle, inBattle.ExpToNextLevel, 0, 160, inBattle: true);
        Assert.Equal([(ushort)0x6E, (ushort)0x70, (ushort)0x80], player.Session.SentOpcodes());
        Assert.Equal(80, inBattle.SpeciesId);
        Assert.True(inBattle.IsEvolved);
        Assert.True(inBattle.Devolve());                       // ✅ volta à forma-base no fim da batalha
        Assert.Equal(50, inBattle.SpeciesId);

        var onMap = NewDigimon(50, 20, 8);
        var other = Join(NewCharacter(onMap));
        Progression.GainExp(other, onMap, onMap.ExpToNextLevel, 0, 160, inBattle: false);
        Assert.Equal([(ushort)0x6E, (ushort)0x70], other.Session.SentOpcodes());
        Assert.Equal(StatFormulas.Champion, onMap.UnlockedStage);
        Assert.Equal(50, onMap.SpeciesId);
        Assert.Equal(new ushort[] { 80 }, onMap.EvolutionOptions().Select(s => s.Id));
    }

    [Fact]
    public void Each_form_has_its_own_points_and_skill_points_are_shared()
    {
        var d = NewDigimon(50, 22, 7);
        var champion = d.FormAtStage(StatFormulas.Champion)!;
        Assert.Equal(80, champion.Species.Id);
        Assert.Same(d.BaseForm, d.FormAtStage(0));
        Assert.Null(d.FormAtStage(StatFormulas.Ultimate));      // não liberado

        // ✅ rookie: 2 por nível até o 21 (40); champion: 2 por nível a partir do 21
        Assert.Equal(40, d.PointsOf(d.BaseForm));
        Assert.Equal(2, d.PointsOf(champion));
        Assert.True(d.AllocatePoint(champion, StatKind.Str));
        Assert.Equal(1, d.PointsOf(champion));
        Assert.Equal(40, d.PointsOf(d.BaseForm));

        // ✅ skills aprendidas no nível exigido; pontos de skill = (L − 1) − Σ(nível − 1), de todas as formas
        Assert.Equal(new[] { 1, 1 }, d.BaseForm.SkillLevels);
        Assert.Equal(new[] { 1, 0 }, champion.SkillLevels);             // a 41 pede nível 26
        Assert.Equal(21, d.SkillPoints);
        Assert.True(d.UpgradeSkill(champion, 0));
        Assert.True(d.UpgradeSkill(d.BaseForm, 1));
        Assert.Equal(19, d.SkillPoints);
        Assert.False(d.UpgradeSkill(champion, 1));             // ainda não aprendida
    }

    [Fact]
    public void Form_info_and_point_packets_carry_the_requested_form()
    {
        var d = NewDigimon(50, 22, 7);
        var champion = d.FormAtStage(StatFormulas.Champion)!;
        var payload = new AddAbilityPointPacket(d, champion, StatFormulas.Champion, 1).EncodePayload();
        Assert.Equal(80, BitConverter.ToUInt16(payload, 20));
        Assert.Equal(new byte[] { StatFormulas.Champion, 1 }, payload[696..698]);
        Assert.Equal(50, d.SpeciesId);                         // a forma atual não muda
        var info = new DigimonFormInfoPacket(3, d, champion).EncodePayload();
        Assert.Equal(80, BitConverter.ToUInt16(info, 8 + 20));
    }

    // ---------- time ----------

    [Fact]
    public void Leader_swaps_with_slot_zero_and_buttons_swap_the_rest()
    {
        var a = NewDigimon(50, 5, 1);
        var b = NewDigimon(50, 5, 2);
        var c = NewDigimon(50, 5, 3);
        var character = NewCharacter(a, b, c);
        Assert.True(character.SetLeader(c));
        Assert.Equal(new Digimon?[] { c, b, a, null, null }, character.Party);
        Assert.False(character.SetLeader(c));
        // ✅ botão 1 troca as posições 2 e 3, mesmo com a 3 vazia
        Assert.True(character.SwapSlots(2, 3));
        Assert.Equal(new Digimon?[] { c, b, null, a, null }, character.Party);
        Assert.False(character.SwapSlots(0, 1));
    }

    // ---------- inventário ----------

    private static byte[] MovePayload(ItemPlace from, ItemPlace to, int fa, int fb, int ta, int tb, uint uid, uint itemId)
    {
        var p = new byte[ItemMoveHeader.Length + 108 * 2 + 14];
        p[0] = (byte)from;
        p[1] = (byte)to;
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(4), fa);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(8), fb);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(12), ta);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(16), tb);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(20), uid);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(24), itemId);
        return p;
    }

    [Fact]
    public void Picking_up_stacks_moves_and_dropping_destroys()
    {
        var zone = _zones.Find(79)!;
        var drop = zone.Drop(41, 64, 27);
        _player.Character.X = 64;
        _player.Character.Y = 27;
        _session.DrainOutgoing();
        new ItemMoveHandler(TestSupport.Guard()).Dispatch(_session, new PacketReader(MovePayload(ItemPlace.Ground, ItemPlace.Inventory, drop.Y, drop.X, 4, 6, drop.Uid, 41)));
        // ✅ o oficial tira do chão (0x13 com 108 zeros + x, y) antes do 0x91
        var pickFrames = _session.DrainOutgoing();
        Assert.Equal([(ushort)0x13, (ushort)0x91], pickFrames.Select(f => PacketHeader.Read(f).Opcode).ToArray());
        AssertRemovalPacket(pickFrames[0], drop.X, drop.Y);
        Assert.Null(zone.FindDrop(drop.Uid));
        var bag = _player.Character.Inventory.Main;
        var seed = bag[0]!;
        Assert.Equal(1u, seed.Amount);

        new ItemMoveHandler(TestSupport.Guard()).Dispatch(_session, new PacketReader(MovePayload(ItemPlace.Inventory, ItemPlace.Inventory, 0, 0, 3, 5, seed.Uid, 41)));
        Assert.Equal([(ushort)0x91], _session.SentOpcodes());
        Assert.Same(seed, bag[23]);
        Assert.Null(bag[0]);

        new ItemDestroyHandler().Dispatch(_session, new PacketReader(MovePayload(ItemPlace.Inventory, ItemPlace.Ground, 3, 5, 0, 0, seed.Uid, 41)));
        Assert.Equal([(ushort)0xBD], _session.SentOpcodes());
        Assert.Empty(bag.Items);
    }

    [Fact]
    public void Expired_ground_drops_are_removed_with_coordinates_after_lifetime()
    {
        var zone = _zones.Find(79)!;
        var drop = zone.Drop(41, 64, 27);
        _player.Character.X = 64;
        _player.Character.Y = 27;
        _session.DrainOutgoing();

        // antes do DropLifetime o item continua no chão e sem spam de 0x13
        zone.Update(drop.DroppedAt + Zone.DropLifetime - TimeSpan.FromSeconds(1), Zone.DropCleanInterval);
        Assert.NotNull(zone.FindDrop(drop.Uid));
        Assert.DoesNotContain((ushort)0x13, _session.SentOpcodes());

        // ao atingir DropLifetime, o item é removido e 0x13 leva 108 zeros + (X, Y)
        zone.Update(drop.DroppedAt + Zone.DropLifetime, Zone.DropCleanInterval);
        var frames = _session.DrainOutgoing().Where(f => PacketHeader.Read(f).Opcode == 0x13).ToList();
        Assert.Single(frames);
        AssertRemovalPacket(frames[0], drop.X, drop.Y);
        Assert.Null(zone.FindDrop(drop.Uid));
    }

    [Fact]
    public void Simultaneous_drops_do_not_collide_and_entering_player_sees_them()
    {
        var zone = _zones.Find(79)!;
        var drops = Enumerable.Range(0, 5).Select(_ => zone.Drop(41, 64, 27)).ToList();
        Assert.Equal(drops.Count, drops.Select(d => (d.X, d.Y)).Distinct().Count());

        var otherSession = TestSupport.NewSession();
        var other = new Player(otherSession, new AuthenticatedAccount(2, "u2", Authority.GameMaster), Entities.Character());
        zone.Enter(other);
        Assert.Equal(drops.Count, otherSession.SentOpcodes().Count(o => o == 0x13));
    }

    private static void AssertRemovalPacket(byte[] frame, short expectedX, short expectedY)
    {
        var payload = frame.AsSpan(PacketHeader.Length);
        Assert.Equal(ItemRecord.Length + 4, payload.Length);
        Assert.True(payload[..ItemRecord.Length].IndexOfAnyExcept((byte)0) < 0);
        Assert.Equal(expectedX, BitConverter.ToInt16(payload[ItemRecord.Length..]));
        Assert.Equal(expectedY, BitConverter.ToInt16(payload[(ItemRecord.Length + 2)..]));
    }

    [Fact]
    public void Seeds_stack_up_to_the_table_limit()
    {
        var bag = _player.Character.Inventory.Main;
        var changed = bag.Add(41, 10);                         // pilha 8 na fixture (col 26)
        Assert.Equal(new uint[] { 8, 2 }, changed.Select(i => i.Amount));
        Assert.Equal(10u, bag.Count(41));
        Assert.Equal(1u, TestSupport.Data.Items[107].MaxStack); // duração em segundos (> 999) não empilha
        Assert.Equal(1000u, TestSupport.Data.Items[41].BuyPrice);
        Assert.Equal(100u, TestSupport.Data.Items[41].SellPrice);
    }

    [Fact]
    public void Picking_up_stackable_drops_merges_into_same_slot_until_max()
    {
        var zone = _zones.Find(79)!;
        var d1 = zone.Drop(41, 64, 27);
        var d2 = zone.Drop(41, 64, 27);
        _player.Character.X = 64;
        _player.Character.Y = 27;
        _session.DrainOutgoing();

        new ItemMoveHandler(TestSupport.Guard()).Dispatch(_session, new PacketReader(MovePayload(ItemPlace.Ground, ItemPlace.Inventory, d1.Y, d1.X, 4, 6, d1.Uid, 41)));
        new ItemMoveHandler(TestSupport.Guard()).Dispatch(_session, new PacketReader(MovePayload(ItemPlace.Ground, ItemPlace.Inventory, d2.Y, d2.X, 4, 6, d2.Uid, 41)));

        var bag = _player.Character.Inventory.Main;
        Assert.Single(bag.Items);
        Assert.Equal(2u, bag[0]!.Amount);
    }

    [Fact]
    public void Inventory_sort_0x148_merges_stacks_orders_and_sends_0xFC()
    {
        var inv = _player.Character.Inventory;
        var bag = inv.Main;
        // coloca itens fora de ordem e duas pilhas parciais da mesma semente (MaxStack = 8 na fixture)
        bag.Place(inv.Create(22004, slot: 10, amount: 1));
        bag.Place(inv.Create(41, slot: 5, amount: 5));
        bag.Place(inv.Create(41, slot: 12, amount: 5));
        bag.Place(inv.Create(74, slot: 20, amount: 2));
        _session.DrainOutgoing();

        var payload = new byte[38];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8), 1); // bagType 1 = Main Inventory
        new InventorySortHandler().Dispatch(_session, new PacketReader(payload));

        Assert.Equal([(ushort)0xFC], _session.SentOpcodes());
        // ordenado por Type (74: Type 4, 41: Type 5 [8 + 2], 22004: Type 26) nos slots 0..3
        Assert.Equal(74u, bag[0]!.ItemId);
        Assert.Equal(2u, bag[0]!.Amount);
        Assert.Equal(41u, bag[1]!.ItemId);
        Assert.Equal(8u, bag[1]!.Amount);
        Assert.Equal(41u, bag[2]!.ItemId);
        Assert.Equal(2u, bag[2]!.Amount);
        Assert.Equal(22004u, bag[3]!.ItemId);
        Assert.Null(bag[4]);
        Assert.Null(bag[10]);
        Assert.Null(bag[20]);

        // bagType 2 = Battle Bag (não mexe no inventário principal)
        inv.Battle.Place(inv.Create(74, slot: 8, amount: 3));
        inv.Battle.Place(inv.Create(74, slot: 15, amount: 4));
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8), 2);
        new InventorySortHandler().Dispatch(_session, new PacketReader(payload));

        Assert.Equal([(ushort)0xFC], _session.SentOpcodes());
        Assert.Equal(74u, inv.Battle[0]!.ItemId);
        Assert.Equal(7u, inv.Battle[0]!.Amount);
        Assert.Null(inv.Battle[8]);
        Assert.Null(inv.Battle[15]);
    }

    // ---------- NPCs ----------

    private DRPG.Server.Game.Npcs.NpcScriptRegistry Npcs => TestSupport.NewNpcRegistry(new DRPG.Server.Game.Npcs.VillageOfBeginningNpcs(_zones, TestSupport.Items()));

    /// <summary>Escolha <paramref name="choice"/> falando com o NPC <paramref name="npcId"/> do mapa 79, de perto.</summary>
    private bool Talk(uint choice, ushort npcId)
    {
        _player.Character.AtNpc(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.TutorialMapId, npcId);
        return Npcs.Choose(_player, choice);
    }

    [Fact]
    public void Manuals_are_given_once()
    {
        Assert.True(Talk(1, DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.PatamonNpc));
        Assert.Equal([(ushort)0x56, (ushort)0xC9, (ushort)0xFC], _session.SentOpcodes());
        Assert.False(Talk(1, DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.PatamonNpc));
        Assert.Empty(_session.SentOpcodes());
        Assert.Equal(1u, _player.Character.Inventory.Main.Count(22004));
        Assert.True(_player.Character.HasFlag(1));
    }

    [Fact]
    public void Tunomon_takes_five_seeds_and_the_end_of_the_tutorial_needs_it()
    {
        var c = _player.Character;
        Assert.False(Talk(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.FinalChoice, DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.ElecmonNpc));
        Assert.False(Talk(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.TunomonChoice, DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.TunomonNpc));   // sem sementes
        c.Inventory.Main.Add(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.YuramonSeed, 8);
        Assert.True(Talk(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.TunomonChoice, DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.TunomonNpc));
        Assert.Equal([(ushort)0x56, (ushort)0xC9, (ushort)0xFC], _session.SentOpcodes());
        Assert.Equal(3u, c.Inventory.Main.Count(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.YuramonSeed));
        Assert.Equal(1u, c.Inventory.Main.Count(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.TunomonCard));

        Assert.True(Talk(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.FinalChoice, DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.ElecmonNpc));
        var sent = _session.SentOpcodes();
        Assert.Equal([(ushort)0xB0, (ushort)0x56, (ushort)0xC8, (ushort)0xC9, (ushort)0xFC], sent.Take(5));
        Assert.Equal(7, sent.Count(o => o == 0x7E));
        Assert.Equal(7, sent.Count(o => o == 0x7F));
        Assert.Equal(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.FinalMap, c.MapId);
        Assert.Equal(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.GoldenDrillAtEndNormal, c.Inventory.Battle.Count(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.GoldenDrillX));
    }

    [Fact]
    public void Tutorial_quiz_perfect_choice_7_gives_100_golden_drills_and_blocks_choice_8()
    {
        var c = _player.Character;
        c.Inventory.Main.Add(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.YuramonSeed, 5);
        Assert.True(Talk(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.TunomonChoice, DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.TunomonNpc));
        _session.DrainOutgoing();

        Assert.True(Talk(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.FinalChoicePerfect, DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.ElecmonNpc));
        Assert.Equal(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.FinalMap, c.MapId);
        Assert.Equal(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.GoldenDrillAtEndPerfect,
            c.Inventory.Battle.Count(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.GoldenDrillX));

        // Não pode reivindicar a escolha 8 após já ter concluído pela escolha 7
        Assert.False(Talk(DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.FinalChoice, DRPG.Server.Game.Npcs.VillageOfBeginningNpcs.ElecmonNpc));
    }

    // ---------- recompensas ----------

    [Fact]
    public void Every_digimon_in_the_battle_gets_exp_and_bits_come_once()
    {
        var first = NewDigimon(50, 5, 1);
        var second = NewDigimon(50, 5, 2);
        var player = Join(NewCharacter(first, second));
        var wild = _zones.Find(79)!.WildDigimons.First();
        var battle = _battles.StartPve(player, player.StandBy(wild))!;
        battle.SendTeams(player);
        battle.ClientReady(player);
        battle.RequestAction(player, BattleIcon.Summon, first.Serial, second.Serial);
        foreach (var e in battle.TeamB) e.Gauge = int.MinValue / 2;
        for (int i = 0; i < 20 && battle.TeamA.Count < 2; i++)
            _battles.Update(TimeSpan.Zero, Battle.TickInterval);
        Assert.Equal(2, battle.TeamA.Count);
        battle.ClientReady(player);
        player.Session.DrainOutgoing();

        double before = player.Character.Wallet.Bits;
        battle.KnockOut(BattleSide.B);
        var frames = player.Session.DrainOutgoing();
        var rewards = frames.Where(f => PacketHeader.Read(f).Opcode == 0x6E).ToList();
        Assert.Equal(2 * battle.TeamB.Count, rewards.Count);
        double bits = rewards.Sum(f => BitConverter.ToDouble(f, PacketHeader.Length + 16 + 8));
        Assert.Equal(player.Character.Wallet.Bits - before, bits);
        // ✅ só o primeiro 0x6E de cada inimigo leva bits; Yuramon do tutorial: 5 a 7
        for (int k = 0; k < rewards.Count; k += 2)
        {
            Assert.InRange(BitConverter.ToDouble(rewards[k], PacketHeader.Length + 24), 5, 7);
            Assert.Equal(0, BitConverter.ToDouble(rewards[k + 1], PacketHeader.Length + 24));
        }
        // ✅ 0xB1, 0x84 e 0x6D um por digimon
        Assert.Equal(2, frames.Count(f => PacketHeader.Read(f).Opcode == 0x6D));
        Assert.Equal(2, frames.Count(f => PacketHeader.Read(f).Opcode == 0x84));
        // ✅ duas sementes por Yuramon
        Assert.Equal(2 * battle.TeamB.Count, _zones.Find(79)!.Drops.Count(d => d.ItemId == 41));
    }

    // ---------- persistência ----------

    [Fact]
    public void Snapshot_round_trips_through_the_factory()
    {
        var a = NewDigimon(50, 22, 11);
        var b = NewDigimon(14, 3, 12);
        var champion = a.FormAtStage(StatFormulas.Champion)!;
        a.AllocatePoint(champion, StatKind.Con);
        a.AllocatePoint(StatKind.Str);
        a.UpgradeSkill(a.BaseForm, 0);
        var character = NewCharacter(a, b);
        character.SwapSlots(1, 3);
        character.Inventory.Main.Add(41, 3);
        character.Inventory.Battle.Add(74, 50);
        character.SetFlag(5);
        character.SetBits(123);

        var s = CharacterSnapshot.Of(character);
        var row = new CharacterRow
        {
            Uid = s.Uid, AccountUid = 1, Slot = 0, Name = "Tester", TamerId = 1, Level = s.Level, MapId = s.MapId,
            PosX = s.X, PosY = s.Y, Bits = s.Bits,
        };
        var digimons = s.Digimons.Select(d => new DigimonRow
        {
            Uid = d.Uid, Slot = d.Slot, DigimonId = d.DigimonId, Name = d.Name, Level = d.Level, Exp = d.Exp,
            UnlockedStage = d.UnlockedStage,
        }).ToList();
        var factory = new CharacterFactory(TestSupport.Data, TestSupport.GameOptions(), TestSupport.Clock);
        var loaded = factory.Build(new CharacterRecord(row, digimons, s.Forms, s.Items, s.Flags))!;

        Assert.Equal(11u, loaded.Leader.Uid);
        Assert.Equal(12u, loaded.Party[3]!.Uid);
        Assert.Null(loaded.Party[1]);
        var la = loaded.Leader;
        Assert.Equal(StatFormulas.Champion, la.UnlockedStage);
        Assert.Equal(1, la.FindForm(80)!.Allocated.Con);
        Assert.Equal(1, la.BaseForm.Allocated.Str);
        Assert.Equal(2, la.BaseForm.SkillLevels[0]);
        Assert.Equal(a.SkillPoints, la.SkillPoints);
        Assert.Equal(3u, loaded.Inventory.Main.Count(41));
        Assert.Equal(50u, loaded.Inventory.Battle.Count(74));
        Assert.True(loaded.HasFlag(5));
        Assert.Equal(123.0, loaded.Wallet.Bits);
        // o uid novo não repete os carregados
        Assert.DoesNotContain(loaded.Inventory.Main.Add(41, 8)[^1].Uid, s.Items.Select(i => i.Uid));
    }
}
