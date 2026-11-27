using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Shared.Tests;

public sealed class MapEntryPacketTests
{
    private static byte[] Payload<T>(T packet) where T : ITwoWayPacket<T> => TwoWayPacket.EncodePayload(packet);

    private static T RoundTrip<T>(T packet) where T : ITwoWayPacket<T> => ClientPacket.Parse<T>(Payload(packet));

    /// <summary>Read(Write(x)) writes the same bytes again (records with arrays do not compare by value).</summary>
    private static void AssertRoundTrip<T>(T packet) where T : ITwoWayPacket<T> =>
        Assert.Equal(Payload(packet), Payload(RoundTrip(packet)));

    private static ItemData Item(uint uid = 7, uint itemId = 215, params ItemEffect[] effects) =>
        new(uid, itemId, 2, 1, 3, 999, 100, 50, 10_000, 5, effects, 9999, 32000, 1_000_000, 1, 2, 3);

    private static CardData Card(uint uid = 9, uint cardId = 74) =>
        new(uid, cardId, 3, 4, 5, 1, 6, 12, 999, 100, 50, 3000, 7, 20, 9999, 32000, 1_000_000, 1, 2, 3);

    private static DigimonStatus Digimon(uint uid = 11, string name = "Agumon") =>
        new(uid, new Serial(0x1122334455667788, 0x99AABBCCDDEEFF00), 619, 3, name, 14, 10, 11, 12, 13,
            2, 1, 0x80, 100, 90, 80, 30, 20, 10, 0x8000_0000, 123_456_789_012, 500, 1000, 900, 800, 100_000,
            987_654_321_098, 6, 9, 4, 3, new DigimonSkill(21, 11, 3), new DigimonSkill(0, 0, 0), 77, 0, 8,
            20030, 1, 1, 2, 3, 4);

    private static ItemData[] Items(int count) =>
        Enumerable.Range(0, count).Select(i => i % 3 == 0 ? ItemData.Empty : Item((uint)(100 + i), (uint)(200 + i))).ToArray();

    private static CharacterDataPacket Character(Action<List<ItemData>>? editTail = null)
    {
        var tail = Items(CharacterDataPacket.TailOffsets.Length).ToList();
        editTail?.Invoke(tail);
        return new(
            1234, new Serial(1, 2), "Tamer", 3, 1, 14, 0, 0, 75, 1234.5, 6, 9, 0, 0, 1800, 4, 77,
            Items(CharacterDataPacket.PrimaryCount),
            Enumerable.Range(0, CharacterDataPacket.CardSlotCount).Select(i => i % 2 == 0 ? CardData.Empty : Card((uint)i)).ToArray(),
            [Digimon(), DigimonStatus.Empty, Digimon(12, "Gabumon"), DigimonStatus.Empty, DigimonStatus.Empty],
            57, 91, tail.ToArray(), 3, 4, 1_800_000_123);
    }

    private static InventoryBags Bags() => new(
        Enumerable.Range(0, InventoryBags.Size).Select(i => i % 4 == 0 ? Card((uint)(i + 1)) : CardData.Empty).ToArray(),
        Enumerable.Range(0, InventoryBags.Size).Select(i => i % 5 == 0 ? Item((uint)(i + 1), 41) : ItemData.Empty).ToArray());

    [Fact]
    public void Every_packet_round_trips()
    {
        AssertRoundTrip(new JoinCharacterRequest("Tamer", "user"));
        AssertRoundTrip(new MoveCharacterRequest(57, 91));
        AssertRoundTrip(new MoveCharacterRequest(-1, short.MaxValue));
        AssertRoundTrip(Character());
        AssertRoundTrip(new InventoryDataPacket(Bags()));
        AssertRoundTrip(new InventoryDataPacket(new InventoryBags(
            Enumerable.Repeat(CardData.Empty, InventoryBags.Size).ToArray(),
            Enumerable.Repeat(ItemData.Empty, InventoryBags.Size).ToArray())));
    }

    [Fact]
    public void Fields_survive_the_round_trip()
    {
        var join = RoundTrip(new JoinCharacterRequest("Tamer", "user"));
        Assert.Equal(("Tamer", "user"), (join.CharacterName, join.Username));
        Assert.Equal(new MoveCharacterRequest(57, 91), RoundTrip(new MoveCharacterRequest(57, 91)));

        var character = Character();
        var read = RoundTrip(character);
        Assert.Equal((character.Uid, character.Serial, character.Name, character.Level, character.Bits),
            (read.Uid, read.Serial, read.Name, read.Level, read.Bits));
        Assert.Equal((57, 91, 3, 4, 1_800_000_123u), (read.X, read.Y, read.Medals, read.Coins, read.ServerUnixTime));
        Assert.Equal(character.Party[0], read.Party[0]);
        Assert.True(read.Party[1].IsEmpty);
        Assert.Equal(character.CardSlots[1], read.CardSlots[1]);
        Assert.Equal(character.TailEquipment[1].Uid, read.TailEquipment[1].Uid);
        Assert.Equal(CharacterDataPacket.TailOffsets.Length, read.TailEquipment.Length);

        var bags = RoundTrip(new InventoryDataPacket(Bags())).Bags;
        Assert.Equal(Card(1), bags.Cards[0]);
        Assert.True(bags.Cards[1].IsEmpty);
        Assert.Equal(41u, bags.Items[5].ItemId);
    }

    [Fact]
    public void Records_keep_their_effect_triplets()
    {
        var item = Item(5, 107, new ItemEffect(10_003, 301, 3), new ItemEffect(1, 1, 7));
        var read = ItemRoundTrip(item);
        Assert.Equal(item.Effects, read.Effects);
        Assert.Equal(item with { Effects = [] }, read with { Effects = [] });
        Assert.Empty(ItemRoundTrip(Item()).Effects);
        Assert.Equal(DigimonStatus.Empty, DigimonRoundTrip(DigimonStatus.Empty));
        Assert.Equal(Digimon(), DigimonRoundTrip(Digimon()));
        Assert.Equal(Card(), CardRoundTrip(Card()));
    }

    [Fact]
    public void Layouts_match_the_captured_packets()
    {
        // ✅ docs/protocolo/opcodes.md: 0x20 is i16 5, x, y and 14 zero bytes
        Assert.Equal(Convert.FromHexString("0500" + "3900" + "5B00" + new string('0', 28)), Payload(new MoveCharacterRequest(57, 91)));
        Assert.Equal(57, Payload(new JoinCharacterRequest("Tamer", "user")).Length);

        var item = Payload(new ItemRecordPacket(Item(7, 215)));
        Assert.Equal(7u, BitConverter.ToUInt32(item, 0));
        Assert.Equal(215u, BitConverter.ToUInt32(item, 4));
        Assert.Equal(222u, BitConverter.ToUInt32(item, 100));           // ✅ the check is uid + item

        var card = Payload(new CardRecordPacket(Card(9, 74)));
        Assert.Equal(74u, BitConverter.ToUInt32(card, 4));
        Assert.Equal(9999, BitConverter.ToUInt16(card, 64));

        var status = Payload(new StatusBlockPacket(Digimon()));
        Assert.Equal((ushort)619, BitConverter.ToUInt16(status, 20));
        Assert.Equal((byte)2, status[160]);                               // stage
        Assert.Equal((ushort)0x80, BitConverter.ToUInt16(status, 162));   // element mask
        Assert.Equal(21u, BitConverter.ToUInt32(status, 276));            // skill 1
        Assert.Equal(20030u, BitConverter.ToUInt32(status, 320));         // title
    }

    [Fact]
    public void Payload_sizes_match_the_captures()
    {
        Assert.Equal(57, Payload(new JoinCharacterRequest("a", "b")).Length);
        Assert.Equal(20, Payload(new MoveCharacterRequest(1, 2)).Length);
        Assert.Equal(7600, Payload(Character()).Length);                  // ✅ v322 0x02
        Assert.Equal(4416, Payload(new InventoryDataPacket(Bags())).Length); // ✅ v322 0x03
        Assert.Equal(108, Payload(new ItemRecordPacket(Item())).Length);
        Assert.Equal(76, Payload(new CardRecordPacket(Card())).Length);
        Assert.Equal(696, Payload(new StatusBlockPacket(Digimon())).Length);
    }

    [Fact]
    public void The_tail_currencies_sit_after_Spirit3()
    {
        var payload = Payload(Character());
        const int tail = 4724;
        Assert.Equal((ushort)3, BitConverter.ToUInt16(payload, tail + 1844));       // ✅ 0x19A8
        Assert.Equal((ushort)4, BitConverter.ToUInt16(payload, tail + 1846));       // ✅ 0x19AA
        Assert.Equal(1_800_000_123u, BitConverter.ToUInt32(payload, tail + 1852));  // ✅ 0x19B0
        Assert.Equal((short)57, BitConverter.ToInt16(payload, 4720));
    }

    [Fact]
    public void An_empty_payload_reads_as_empty_records()
    {
        Assert.Equal(DigimonStatus.Empty, ClientPacket.Parse<StatusBlockPacket>(new byte[696]).Status);
        Assert.True(ClientPacket.Parse<ItemRecordPacket>(new byte[108]).Item.IsEmpty);
        Assert.True(ClientPacket.Parse<CardRecordPacket>(new byte[76]).Card.IsEmpty);
        Assert.Equal(new byte[696], Payload(new StatusBlockPacket(DigimonStatus.Empty)));
    }

    [Fact]
    public void A_truncated_or_padded_payload_is_malformed()
    {
        foreach (var packet in new[] { Payload(Character()), Payload(new InventoryDataPacket(Bags())) })
        {
            Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<CharacterDataPacket>(packet[..^1]));
            Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<InventoryDataPacket>(packet[..^1]));
            Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<InventoryDataPacket>(packet.Append((byte)0).ToArray()));
        }
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<CharacterDataPacket>(Array.Empty<byte>()));
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<JoinCharacterRequest>(new byte[56]));
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<JoinCharacterRequest>(new byte[58]));
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<MoveCharacterRequest>(new byte[19]));
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<MoveCharacterRequest>(new byte[21]));
    }

    [Fact]
    public void An_item_record_with_a_wrong_check_is_malformed()
    {
        var payload = Payload(new InventoryDataPacket(Bags()));
        int firstItem = InventoryBags.Size * CardData.Length;       // item slot 0 (uid 1, item 41)
        payload[firstItem + 100] ^= 0x01;
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<InventoryDataPacket>(payload));

        var character = Payload(Character());
        character[100 + 108 + 100] ^= 0x01;                          // primary equipment 1
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<CharacterDataPacket>(character));
    }

    [Fact]
    public void Hostile_strings_and_numbers_are_read_without_failing()
    {
        var hostile = new byte[57];
        Array.Fill(hostile, (byte)0xFF);                                // no \0 in either name
        var join = ClientPacket.Parse<JoinCharacterRequest>(hostile);
        Assert.Equal(21, join.CharacterName.Length);
        Assert.Equal(21, join.Username.Length);

        var move = ClientPacket.Parse<MoveCharacterRequest>(Enumerable.Repeat((byte)0xFF, 20).ToArray());
        Assert.Equal(new MoveCharacterRequest(-1, -1), move);

        var status = new byte[696];
        Array.Fill(status, (byte)0xFF);
        var digimon = ClientPacket.Parse<StatusBlockPacket>(status).Status;
        Assert.Equal(21, digimon.Name.Length);
        Assert.Equal(-1, digimon.Hp);
    }

    [Fact]
    public void A_packet_that_cannot_be_written_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => Payload(Character() with { Party = [] }));
        Assert.Throws<InvalidOperationException>(() => Payload(Character() with { PrimaryEquipment = Items(5) }));
        Assert.Throws<InvalidOperationException>(() => Payload(Character() with { CardSlots = [CardData.Empty] }));
        Assert.Throws<InvalidOperationException>(() => Payload(Character() with { TailEquipment = Items(22) }));
        Assert.Throws<InvalidOperationException>(() => Payload(new InventoryDataPacket(new InventoryBags([], []))));
        var tooMany = Item(1, 2, Enumerable.Repeat(new ItemEffect(1, 1, 1), 5).ToArray());
        Assert.Throws<InvalidOperationException>(() => Payload(new ItemRecordPacket(tooMany)));
    }

    [Fact]
    public void The_tail_offsets_are_in_order_and_do_not_overlap()
    {
        var offsets = CharacterDataPacket.TailOffsets;
        for (int i = 1; i < offsets.Length; i++)
            Assert.True(offsets[i] >= offsets[i - 1] + ItemData.Length);
        Assert.True(offsets[^1] + ItemData.Length <= 2876);
        Assert.Contains(offsets, o => o > 1844);
        Assert.DoesNotContain(offsets, o => o > 1844 - ItemData.Length && o < 1856);   // room for the currencies
    }

    private static ItemData ItemRoundTrip(ItemData item) => RoundTrip(new ItemRecordPacket(item)).Item;
    private static CardData CardRoundTrip(CardData card) => RoundTrip(new CardRecordPacket(card)).Card;
    private static DigimonStatus DigimonRoundTrip(DigimonStatus status) => RoundTrip(new StatusBlockPacket(status)).Status;

    // single-record wrappers so each record has a packet-shaped round trip of its own
    private sealed record ItemRecordPacket(ItemData Item) : ITwoWayPacket<ItemRecordPacket>
    {
        public static ushort Opcode => 0;
        public static PacketLength Length => PacketLength.Exact(ItemData.Length);
        public static ItemRecordPacket Read(PacketReader r) => new(ItemData.Read(r));
        public void Write(PacketWriter w) => Item.Write(w);
    }

    private sealed record CardRecordPacket(CardData Card) : ITwoWayPacket<CardRecordPacket>
    {
        public static ushort Opcode => 0;
        public static PacketLength Length => PacketLength.Exact(CardData.Length);
        public static CardRecordPacket Read(PacketReader r) => new(CardData.Read(r));
        public void Write(PacketWriter w) => Card.Write(w);
    }

    private sealed record StatusBlockPacket(DigimonStatus Status) : ITwoWayPacket<StatusBlockPacket>
    {
        public static ushort Opcode => 0;
        public static PacketLength Length => PacketLength.Exact(DigimonStatus.Length);
        public static StatusBlockPacket Read(PacketReader r) => new(DigimonStatus.Read(r));
        public void Write(PacketWriter w) => Status.Write(w);
    }
}
