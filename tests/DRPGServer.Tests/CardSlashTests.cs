using System.Collections.Frozen;
using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;

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
}
