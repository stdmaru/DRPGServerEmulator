using System.Collections.Frozen;
using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Battle.Cards;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

public class CardSlashTests
{
    private static readonly FrozenDictionary<uint, CardInfo> SampleCards = new Dictionary<uint, CardInfo>
    {
        [5] = new(5, "Defense Plug-In C")
        {
            Type = 1, SubType = 2, BuyPrice = 500, SellPrice = 250, Flag19 = 1,
            MaxStack = 999, Trigger = CardTrigger.Defense, Restriction = 0,
            Col26 = 32000, Col27 = 1, Col28 = 1_000_000, Col29 = 1, Col30 = 1,
        },
        [19] = new(19, "Recovery Floppy L")
        {
            Type = 2, SubType = 1, BuyPrice = 500, SellPrice = 250, Flag19 = 1,
            MaxStack = 999, Trigger = CardTrigger.Attack, Restriction = 0,
            Col26 = 32000, Col27 = 1, Col28 = 1_000_000, Col29 = 1, Col30 = 1,
        },
        [74] = new(74, "Golden Drill X")
        {
            Type = 4, SubType = 1, BuyPrice = 1, SellPrice = 0, Flag19 = 0,
            MaxStack = 999, Trigger = CardTrigger.Attack, Restriction = 1,
            Col26 = 32000, Col27 = 1, Col28 = 1_000_000, Col29 = 1, Col30 = 0,
        },
    }.ToFrozenDictionary();

    private static byte[] CardMovePayload(ItemPlace from, ItemPlace to, int fromA, int fromB, int toA, int toB, uint uid, uint cardId)
    {
        var w = new PacketWriter();
        new ItemMoveHeader(from, to, fromA, fromB, toA, toB).Write(w);
        w.WriteU32(uid);
        w.WriteU32(cardId);
        w.WriteZeros(CardRecord.Length - 8);
        return w.Payload.ToArray();
    }

    private static (Player Player, Session Session) NewPlayerWithCards()
    {
        var inventory = new Inventory(TestSupport.Data.FindItem, 999, id => SampleCards.GetValueOrDefault(id));
        var character = new Character(Entities.Digimon(), inventory)
        {
            Uid = 1, AccountUid = 1, Slot = 0, Name = "CardTester", TamerId = 1, MapId = 79,
        };
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);
        session.Player = player;
        return (player, session);
    }

    [Fact]
    public void CardMoveHandler_equips_swaps_reorders_and_unequips_cards()
    {
        var (player, session) = NewPlayerWithCards();
        var inv = player.Character.Inventory;
        var drill = inv.Battle.Add(74, 50)[0];    // slot 0 (row 0, col 0)
        var defense = inv.Battle.Add(5, 10)[0];   // slot 1 (row 0, col 1)
        var handler = new CardMoveHandler();

        // 3 → 2: equipar Golden Drill X no Card Slot 0
        handler.Handle(session, new PacketReader(CardMovePayload(ItemPlace.Inventory, ItemPlace.CardSlots, 0, 0, 0, 0, drill.Uid, 74)));
        Assert.Null(inv.Battle[0]);
        Assert.Same(drill, inv.CardSlots[0]);
        Assert.Equal(0, drill.Slot);
        var frame1 = Assert.Single(session.DrainOutgoing());
        Assert.Equal((ushort)MapOpcode.CardMove, PacketHeader.Read(frame1).Opcode);
        Assert.Equal(172, frame1.Length - PacketHeader.Length);

        // 3 → 2: equipar Defense Plug-In C no Card Slot 0 já ocupado (troca com Golden Drill X)
        handler.Handle(session, new PacketReader(CardMovePayload(ItemPlace.Inventory, ItemPlace.CardSlots, 0, 1, 0, 0, defense.Uid, 5)));
        Assert.Same(defense, inv.CardSlots[0]);
        Assert.Same(drill, inv.Battle[1]);
        Assert.Equal(1, drill.Slot);
        var frame2 = Assert.Single(session.DrainOutgoing());
        var payload2 = frame2.AsSpan(PacketHeader.Length);
        Assert.Equal(5u, BitConverter.ToUInt32(payload2[(ItemMoveHeader.Length + 4)..]));
        Assert.Equal(74u, BitConverter.ToUInt32(payload2[(ItemMoveHeader.Length + CardRecord.Length + 4)..]));

        // 2 → 2: mover dentro dos 6 Card Slots (slot 0 → slot 5)
        handler.Handle(session, new PacketReader(CardMovePayload(ItemPlace.CardSlots, ItemPlace.CardSlots, 0, 0, 0, 5, defense.Uid, 5)));
        Assert.Null(inv.CardSlots[0]);
        Assert.Same(defense, inv.CardSlots[5]);
        Assert.Equal(5, defense.Slot);
        Assert.Single(session.DrainOutgoing());

        // 2 → 3: desequipar do Card Slot 5 para a mochila (row 2, col 3 = slot 15)
        handler.Handle(session, new PacketReader(CardMovePayload(ItemPlace.CardSlots, ItemPlace.Inventory, 0, 5, 2, 3, defense.Uid, 5)));
        Assert.Null(inv.CardSlots[5]);
        Assert.Same(defense, inv.Battle[15]);
        Assert.Equal(15, defense.Slot);
        Assert.Single(session.DrainOutgoing());

        // 3 → 3: mover na mochila (slot 15 → slot 23)
        handler.Handle(session, new PacketReader(CardMovePayload(ItemPlace.Inventory, ItemPlace.Inventory, 2, 3, 3, 5, defense.Uid, 5)));
        Assert.Null(inv.Battle[15]);
        Assert.Same(defense, inv.Battle[23]);
        Assert.Single(session.DrainOutgoing());
    }

    [Fact]
    public void CardMoveHandler_rejects_invalid_card_slot_indices()
    {
        var (player, session) = NewPlayerWithCards();
        var drill = player.Character.Inventory.Battle.Add(74, 10)[0];
        var handler = new CardMoveHandler();

        // row != 0 ou col >= 6 nos Card Slots são inválidos
        handler.Handle(session, new PacketReader(CardMovePayload(ItemPlace.Inventory, ItemPlace.CardSlots, 0, 0, 1, 0, drill.Uid, 74)));
        handler.Handle(session, new PacketReader(CardMovePayload(ItemPlace.Inventory, ItemPlace.CardSlots, 0, 0, 0, 6, drill.Uid, 74)));
        Assert.Empty(session.DrainOutgoing());
        Assert.Same(drill, player.Character.Inventory.Battle[0]);
    }

    [Fact]
    public void CardRecord_matches_captured_76_byte_layout()
    {
        var inv = new Inventory(TestSupport.Data.FindItem, 999, id => SampleCards.GetValueOrDefault(id));
        var defense = inv.CardSlots.Add(5, 3)[0];
        var w = new PacketWriter();
        CardRecord.Write(w, defense);
        var buf = w.Payload;

        Assert.Equal(76, buf.Length);
        Assert.Equal(defense.Uid, BitConverter.ToUInt32(buf[0..]));
        Assert.Equal(5u, BitConverter.ToUInt32(buf[4..]));
        Assert.Equal((byte)1, buf[8]);                           // Type
        Assert.Equal((byte)2, buf[9]);                           // SubType
        Assert.Equal((byte)1, buf[10]);                          // Flag19
        Assert.Equal((byte)CardTrigger.Defense, buf[11]);        // Trigger = Defense (2)
        Assert.Equal((byte)0, buf[12]);                          // Restriction
        Assert.Equal((ushort)3, BitConverter.ToUInt16(buf[14..]));// Amount
        Assert.Equal(999u, BitConverter.ToUInt32(buf[16..]));    // MaxStack
        Assert.Equal(500u, BitConverter.ToUInt32(buf[20..]));    // BuyPrice
        Assert.Equal(250u, BitConverter.ToUInt32(buf[24..]));    // SellPrice
        Assert.Equal(5000u, BitConverter.ToUInt32(buf[28..]));   // EffectDuration
        Assert.Equal(5u, BitConverter.ToUInt32(buf[32..]));      // EffectCode
        Assert.Equal(20u, BitConverter.ToUInt32(buf[36..]));     // EffectValue
        Assert.Equal((ushort)9999, BitConverter.ToUInt16(buf[64..]));
        Assert.Equal((ushort)32000, BitConverter.ToUInt16(buf[66..]));
        Assert.Equal(1_000_000u, BitConverter.ToUInt32(buf[68..]));
        Assert.Equal((byte)1, buf[72]);
        Assert.Equal((byte)1, buf[73]);
        Assert.Equal((byte)0, buf[74]);
        Assert.Equal((byte)1, buf[75]);
    }

    [Fact]
    public void Snapshot_and_factory_round_trip_equipped_card_slots()
    {
        var data = new GameData
        {
            Species = TestSupport.Data.Species,
            Items = TestSupport.Data.Items,
            Cards = SampleCards,
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
        var inv = new Inventory(data.FindItem, 999, data.FindCard);
        var drill = inv.CardSlots.Add(74, 50)[0];
        var def = inv.Battle.Add(5, 12)[0];
        var digimon = new Digimon(data, data.Species[10], 1) { Uid = 100 };
        var character = new Character(digimon, inv)
        {
            Uid = 7, AccountUid = 1, Slot = 0, Name = "RoundTrip", TamerId = 1, MapId = 79,
        };

        var snap = CharacterSnapshot.Of(character);
        Assert.Contains(snap.Items, i => i.Bag == (byte)BagKind.EquippedCards && i.Slot == 0 && i.ItemId == 74 && i.Amount == 50);
        Assert.Contains(snap.Items, i => i.Bag == (byte)BagKind.Battle && i.Slot == 0 && i.ItemId == 5 && i.Amount == 12);

        var row = new CharacterRow { Uid = 7, AccountUid = 1, Slot = 0, Name = "RoundTrip", TamerId = 1, Level = 1, MapId = 79 };
        var dRow = new DigimonRow { Uid = 100, Slot = 0, DigimonId = 10, Name = "A", Level = 1 };
        var loaded = new CharacterFactory(data, TestSupport.GameOptions()).Build(
            new CharacterRecord(row, [dRow], [], snap.Items, []))!;

        Assert.Equal((74u, 50u, drill.Uid), (loaded.Inventory.CardSlots[0]!.ItemId, loaded.Inventory.CardSlots[0]!.Amount, loaded.Inventory.CardSlots[0]!.Uid));
        Assert.Equal(CardTrigger.Attack, loaded.Inventory.CardSlots[0]!.CardInfo!.Trigger);
        Assert.Equal((5u, 12u, def.Uid), (loaded.Inventory.Battle[0]!.ItemId, loaded.Inventory.Battle[0]!.Amount, loaded.Inventory.Battle[0]!.Uid));
        Assert.Equal(CardTrigger.Defense, loaded.Inventory.Battle[0]!.CardInfo!.Trigger);
    }

    private static byte[] BattleCardPayload(uint slot, uint uid, uint cardId)
    {
        var w = new PacketWriter();
        w.WriteU32(slot);
        w.WriteU32(uid);
        w.WriteU32(cardId);
        w.WriteZeros(98 - 12);
        return w.Payload.ToArray();
    }

    private static (Battle Battle, Player Player, Session Session, GameWorld World) StartBattleWithCards(
        Func<Inventory, Action>? setupInventory = null)
    {
        var world = TestSupport.NewWorld();
        var zones = new ZoneSystem(TestSupport.Data, world, NullLogger<ZoneSystem>.Instance);
        var battles = new BattleSystem(world, TestSupport.Data, TestSupport.GameOptions(), NullLogger<BattleSystem>.Instance);

        var inv = new Inventory(TestSupport.Data.FindItem, 999, id => SampleCards.GetValueOrDefault(id));
        setupInventory?.Invoke(inv)();
        var leader = new Digimon(TestSupport.Data, TestSupport.Data.Species[10], 1) { Uid = 10, Name = "Guilmon" };
        var character = new Character(leader, inv)
        {
            Uid = 1, AccountUid = 1, Slot = 0, Name = "CardSlasher", TamerId = 1, MapId = 79, X = 56, Y = 56,
        };
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);
        session.Player = player;
        zones.Find(79)!.Enter(player);

        var wild = zones.Find(79)!.WildDigimons.First();
        var battle = battles.StartPve(player, wild.Serial)!;
        battle.SendTeams(player);
        battle.ClientReady(player);
        session.DrainOutgoing();
        return (battle, player, session, world);
    }

    [Fact]
    public void BattleCardHandlers_equip_and_unequip_slots_and_reject_unequipped_bag_cards()
    {
        InventoryItem? equippedDrill = null;
        InventoryItem? bagOnlyDefense = null;
        var (battle, player, session, _) = StartBattleWithCards(inv => () =>
        {
            equippedDrill = inv.CardSlots.Add(74, 50)[0];
            bagOnlyDefense = inv.Battle.Add(5, 10)[0];
        });

        var equip = new BattleCardEquipHandler();
        var unequip = new BattleCardUnequipHandler();

        // Card nos 6 CardSlots equipa normalmente no slot 1 (sem resposta S2C)
        equip.Handle(session, new PacketReader(BattleCardPayload(1, equippedDrill!.Uid, 74)));
        Assert.Empty(session.DrainOutgoing());

        // Card apenas na mochila (não nos 6 CardSlots) é rejeitado
        Assert.False(battle.EquipCard(player, 0, bagOnlyDefense!.Uid, 5));

        // Impede equipar a mesma pilha (mesmo UID) em dois slots de batalha ao mesmo tempo
        Assert.False(battle.EquipCard(player, 2, equippedDrill.Uid, 74));

        // Rejeita slot fora do intervalo (0..2) e mismatch de cardId
        Assert.False(battle.EquipCard(player, 3, equippedDrill.Uid, 74));
        Assert.False(battle.EquipCard(player, 0, equippedDrill.Uid, 5));

        // Desequipar slot 1 via 0xA1 (sem resposta S2C)
        unequip.Handle(session, new PacketReader(BattleCardPayload(1, equippedDrill.Uid, 74)));
        Assert.Empty(session.DrainOutgoing());
        Assert.Equal(CardBattleState.MaxCardSlashesPerBattle, battle.CardSlashesRemaining(player));
    }

    [Fact]
    public void Attack_and_defense_cards_trigger_separately_and_write_0x67_offsets_40_to_63()
    {
        InventoryItem? defCard = null;
        InventoryItem? drillCard = null;
        InventoryItem? healCard = null;
        var (battle, player, session, _) = StartBattleWithCards(inv => () =>
        {
            defCard = inv.CardSlots.Add(5, 3)[0];       // Defense Plug-In C (Trigger = Defense)
            drillCard = inv.CardSlots.Add(74, 50)[0];   // Golden Drill X (Trigger = Attack, AoE)
            healCard = inv.CardSlots.Add(19, 1)[0];     // Recovery Floppy L (Trigger = Attack, +50 HP, última unidade)
        });

        // Equipar nos 3 slots de batalha: slot 0 = Defesa, slot 1 = AoE, slot 2 = Cura (1 unidade)
        Assert.True(battle.EquipCard(player, 0, defCard!.Uid, 5));
        Assert.True(battle.EquipCard(player, 1, drillCard!.Uid, 74));
        Assert.True(battle.EquipCard(player, 2, healCard!.Uid, 19));

        // Dano prévio no líder para comprovar a cura de +50 HP do Recovery Floppy L
        var leader = player.Leader;
        leader.TakeDamage(80);
        int hpBeforeAttack = leader.Hp;

        // 1) Turno de ataque do jogador: apenas slots 1 e 2 (Ataque) ativam; slot 0 (Defesa) fica intacto
        var enemy = battle.TeamB[0];
        battle.TeamA[0].Gauge = BattleFormulas.MaxGauge;
        battle.RequestAction(player, BattleIcon.Attack, leader.Serial, enemy.Digimon.Serial);
        session.DrainOutgoing();

        // Avança o tempo até o tick disparar o ataque do jogador
        for (int i = 0; i < 10 && battle.IsRunning; i++)
            battle.Update(Battle.TickInterval);

        Assert.Equal(19, battle.CardSlashesRemaining(player));
        Assert.Equal(3u, defCard.Amount);               // defesa NÃO foi consumida no ataque
        Assert.Equal(49u, drillCard.Amount);            // 50 → 49
        Assert.Equal(0u, healCard.Amount);              // 1 → 0 (esvaziado)
        Assert.Null(player.Character.Inventory.CardSlots[2]);
        Assert.Equal(hpBeforeAttack + 50, leader.Hp);   // Recovery Floppy L curou +50 HP

        var atk0x67 = session.DrainOutgoing()
            .First(f => PacketHeader.Read(f).Opcode == (ushort)MapOpcode.BattleActionResults);
        var atkEntry = atk0x67.AsSpan(PacketHeader.Length, ActionResultsPacket.EntryLength);
        // Preserva índices dos slots 0..2: [0, 74, 19] e quantidades restantes [0, 49, 0]
        Assert.Equal(0u, BitConverter.ToUInt32(atkEntry[40..]));
        Assert.Equal(74u, BitConverter.ToUInt32(atkEntry[44..]));
        Assert.Equal(19u, BitConverter.ToUInt32(atkEntry[48..]));
        Assert.Equal(0u, BitConverter.ToUInt32(atkEntry[52..]));
        Assert.Equal(49u, BitConverter.ToUInt32(atkEntry[56..]));
        Assert.Equal(0u, BitConverter.ToUInt32(atkEntry[60..]));

        // 2) Turno de ataque inimigo: apenas slot 0 (Defesa) ativa e grava na entrada do defensor (posição 5 do 0x67)
        Assert.False(battle.IsOver);
        battle.ClientReady(player);
        session.DrainOutgoing();
        battle.TeamA[0].Gauge = 0;
        battle.TeamB.First(c => !c.Digimon.IsKnockedOut).Gauge = BattleFormulas.MaxGauge;
        for (int i = 0; i < 10 && battle.IsRunning; i++)
            battle.Update(Battle.TickInterval);

        Assert.Equal(18, battle.CardSlashesRemaining(player));
        Assert.Equal(2u, defCard.Amount);           // 3 → 2
        Assert.Equal(49u, drillCard.Amount);        // ataque NÃO foi consumido na defesa

        var def0x67 = session.DrainOutgoing()
            .First(f => PacketHeader.Read(f).Opcode == (ushort)MapOpcode.BattleActionResults);
        // Na entrada do defensor (posição 5 do 0x67 = 5 × 136 = 680)
        var defEntry = def0x67.AsSpan(PacketHeader.Length + 5 * ActionResultsPacket.EntryLength, ActionResultsPacket.EntryLength);
        Assert.Equal(5u, BitConverter.ToUInt32(defEntry[40..]));
        Assert.Equal(0u, BitConverter.ToUInt32(defEntry[44..]));
        Assert.Equal(0u, BitConverter.ToUInt32(defEntry[48..]));
        Assert.Equal(2u, BitConverter.ToUInt32(defEntry[52..]));
        Assert.Equal(0u, BitConverter.ToUInt32(defEntry[56..]));
        Assert.Equal(0u, BitConverter.ToUInt32(defEntry[60..]));
    }

    [Fact]
    public void Card_slash_limit_stops_activating_after_20_turns()
    {
        var (player, _) = NewPlayerWithCards();
        var stack = player.Character.Inventory.CardSlots.Add(74, 25)[0];
        var state = new CardBattleState();
        Assert.True(state.Equip(player, 0, stack.Uid, 74));

        for (int turn = 1; turn <= 20; turn++)
        {
            Assert.True(state.TryActivate(player, CardTrigger.Attack, out var activated));
            Assert.Equal(74u, activated[0]!.Value.CardId);
            Assert.Equal((uint)(25 - turn), activated[0]!.Value.RemainingAmount);
            Assert.Equal(20 - turn, state.SlashesRemaining);
        }

        // 21º turno: contador em 0, não ativa nem consome mais
        Assert.False(state.TryActivate(player, CardTrigger.Attack, out var blocked));
        Assert.False(blocked.Any);
        Assert.Equal(0, state.SlashesRemaining);
        Assert.Equal(5u, stack.Amount);
    }

    [Fact]
    public void CardEffectRegistry_evaluates_attack_defense_slot_return_and_haste()
    {
        // Ataque combinado: Offense Plug-In A (+20%) + Boost Chip (+50%) + Golden Drill X (AoE)
        var atk = new ActivatedCards();
        atk[0] = new ActivatedCard(3, 9);
        atk[1] = new ActivatedCard(7, 4);
        atk[2] = new ActivatedCard(74, 49);
        var atkCtx = CardEffectRegistry.EvaluateAttack(atk);
        Assert.Equal(1.70, atkCtx.DamageMultiplier, 6);
        Assert.True(atkCtx.ForceAllTargets);

        // Defesa: Defense Plug-In C (+20% DEF) + Holy Orb (-50% dano)
        var def = new ActivatedCards();
        def[0] = new ActivatedCard(5, 2);
        def[1] = new ActivatedCard(15, 1);
        var defCtx = CardEffectRegistry.EvaluateDefense(def);
        Assert.Equal(1.20, defCtx.DefenseMultiplier, 6);
        Assert.Equal(0.50, defCtx.IncomingDamageMultiplier, 6);

        // White Wings (-100% dano) reduz dano final para 0 mesmo em acerto
        var wings = new ActivatedCards();
        wings[0] = new ActivatedCard(63, 1);
        var wingsCtx = CardEffectRegistry.EvaluateDefense(wings);
        var hit = BattleFormulas.Attack(Entities.Digimon(50, 20), Entities.Digimon(10, 1), new Random(1),
            damageMultiplier: wingsCtx.IncomingDamageMultiplier);
        Assert.True(hit.Landed);
        Assert.Equal(0, hit.Damage);

        // Slot Return (#41) impede consumo dos cards nos slots seguintes
        var (player, _) = NewPlayerWithCards();
        var slotReturn = player.Character.Inventory.CardSlots.Add(41, 2)[0];
        var drill = player.Character.Inventory.CardSlots.Add(74, 10)[0];
        var state = new CardBattleState();
        Assert.True(state.Equip(player, 0, slotReturn.Uid, 41));
        Assert.True(state.Equip(player, 1, drill.Uid, 74));
        Assert.True(state.TryActivate(player, CardTrigger.Attack, out var retAct));
        Assert.Equal(1u, slotReturn.Amount);            // Slot Return consumiu 1
        Assert.Equal(10u, drill.Amount);                // Golden Drill X atrás NÃO foi consumido
        Assert.Equal(10u, retAct[1]!.Value.RemainingAmount);

        // Haste (#81) não ativa em dois turnos consecutivos
        var haste = player.Character.Inventory.CardSlots.Add(81, 5)[0];
        var hasteState = new CardBattleState();
        Assert.True(hasteState.Equip(player, 0, haste.Uid, 81));
        Assert.True(hasteState.TryActivate(player, CardTrigger.Attack, out _));
        Assert.Equal(4u, haste.Amount);
        Assert.False(hasteState.TryActivate(player, CardTrigger.Attack, out _));
        Assert.Equal(4u, haste.Amount);
        Assert.True(hasteState.TryActivate(player, CardTrigger.Attack, out _));
        Assert.Equal(3u, haste.Amount);
    }
}
