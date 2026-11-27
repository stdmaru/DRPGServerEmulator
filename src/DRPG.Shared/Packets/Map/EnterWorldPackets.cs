using DRPG.Shared.Protocol;

namespace DRPG.Shared.Packets.Map;

/// <summary>
/// ✅ 0x02 S2C (7600 B, v322): the full tamer and the five party digimon. Required to load the map. Layout in
/// docs/protocolo/structs/tamer-resumo.md and status-digimon.md; offsets in the comments are from the payload start.
/// </summary>
public sealed record CharacterDataPacket(
    uint Uid, Serial Serial, string Name, byte TamerId, ushort Unknown42, ushort Level,
    int Fame, int Exp, int ExpToNextLevel, double Bits, int TotalWins, int TotalBattles,
    short RankedWins, short RankedBattles, int RankedPoints, ushort Rank, uint LadderPoints,
    ItemData[] PrimaryEquipment, CardData[] CardSlots, DigimonStatus[] Party, short X, short Y,
    ItemData[] TailEquipment, ushort Medals, ushort Coins, uint ServerUnixTime) : ITwoWayPacket<CharacterDataPacket>
{
    public const int NameBytes = 21;
    public const int PrimaryCount = 6;
    public const int CardSlotCount = 6;
    public const int PartySize = 5;

    /// <summary>✅ Offsets of the tail equipment records, from the start of the tail (<c>offset_0x02 - 4724</c>), in order.</summary>
    public static readonly int[] TailOffsets =
    [
        0, 108, 224, 332, 440, 548, 656, 764, 872, 980, 1088, 1196, 1304, 1412, 1520, 1628, 1736, 2144, 2252, 2360,
        2468, 2576, 2684,
    ];

    private const int TailLength = 2876;

    /// <summary>✅ Medals (u16), Coins (u16), 4 zero bytes and the server clock (u32) sit 1844 bytes into the tail, after Spirit3.</summary>
    private const int CurrencyOffset = 1844;
    private const int CurrencyLength = 12;

    public static ushort Opcode => (ushort)MapOpcode.CharacterData;
    public static PacketLength Length => PacketLength.Exact(7600);

    public static CharacterDataPacket Read(PacketReader r)
    {
        var uid = r.ReadU32();
        var serial = Serial.Read(r);
        var name = r.ReadFixedString(NameBytes);
        var tamerId = r.ReadU8();
        var unknown42 = r.ReadU16();
        var level = r.ReadU16();
        r.Skip(2);
        var fame = r.ReadI32();
        var exp = r.ReadI32();
        var toNext = r.ReadI32();
        r.Skip(4);
        var bits = r.ReadF64();
        var wins = r.ReadI32();
        var battles = r.ReadI32();
        var rankedWins = r.ReadI16();
        var rankedBattles = r.ReadI16();
        var rankedPoints = r.ReadI32();
        var rank = r.ReadU16();
        r.Skip(2);
        var ladder = r.ReadU32();
        r.Skip(4);
        var primary = new ItemData[PrimaryCount];
        for (int i = 0; i < primary.Length; i++) primary[i] = ItemData.Read(r);
        r.Skip(36);
        var cards = new CardData[CardSlotCount];
        for (int i = 0; i < cards.Length; i++) cards[i] = CardData.Read(r);
        var party = new DigimonStatus[PartySize];
        for (int i = 0; i < party.Length; i++) party[i] = DigimonStatus.Read(r);
        var x = r.ReadI16();
        var y = r.ReadI16();

        var tail = new ItemData[TailOffsets.Length];
        ushort medals = 0, coins = 0;
        uint time = 0;
        int cursor = 0;
        bool currencies = false;
        for (int i = 0; i < tail.Length; i++)
        {
            if (!currencies && TailOffsets[i] > CurrencyOffset)
            {
                r.Skip(CurrencyOffset - cursor);
                medals = r.ReadU16();
                coins = r.ReadU16();
                r.Skip(4);
                time = r.ReadU32();
                cursor = CurrencyOffset + CurrencyLength;
                currencies = true;
            }
            r.Skip(TailOffsets[i] - cursor);
            tail[i] = ItemData.Read(r);
            cursor = TailOffsets[i] + ItemData.Length;
        }
        r.Skip(TailLength - cursor);
        return new(uid, serial, name, tamerId, unknown42, level, fame, exp, toNext, bits, wins, battles,
            rankedWins, rankedBattles, rankedPoints, rank, ladder, primary, cards, party, x, y, tail, medals, coins, time);
    }

    public void Write(PacketWriter w)
    {
        Require(PrimaryEquipment.Length, PrimaryCount, nameof(PrimaryEquipment));
        Require(CardSlots.Length, CardSlotCount, nameof(CardSlots));
        Require(Party.Length, PartySize, nameof(Party));
        Require(TailEquipment.Length, TailOffsets.Length, nameof(TailEquipment));

        w.WriteU32(Uid);                        // 0
        Serial.Write(w);                        // 4
        w.WriteFixedString(Name, NameBytes);    // 20
        w.WriteU8(TamerId);                     // 41
        w.WriteU16(Unknown42);                  // 42 ❓ 1
        w.WriteU16(Level);                      // 44
        w.WriteZeros(2);
        w.WriteI32(Fame);                       // 48
        w.WriteI32(Exp);                        // 52
        w.WriteI32(ExpToNextLevel);             // 56
        w.WriteZeros(4);
        w.WriteF64(Bits);                       // 64
        w.WriteI32(TotalWins);                  // 72
        w.WriteI32(TotalBattles);               // 76
        w.WriteI16(RankedWins);                 // 80
        w.WriteI16(RankedBattles);              // 82
        w.WriteI32(RankedPoints);               // 84
        w.WriteU16(Rank);                       // 88
        w.WriteZeros(2);
        w.WriteU32(LadderPoints);               // 92
        w.WriteZeros(4);
        foreach (var item in PrimaryEquipment) item.Write(w);   // 100..747
        w.WriteZeros(36);                       // 748..783
        foreach (var card in CardSlots) card.Write(w);          // 784..1239
        foreach (var digimon in Party) digimon.Write(w);        // 1240
        w.WriteI16(X);                          // 4720
        w.WriteI16(Y);

        int cursor = 0;
        bool currencies = false;
        for (int i = 0; i < TailOffsets.Length; i++)            // 4724..7599
        {
            if (!currencies && TailOffsets[i] > CurrencyOffset)
            {
                w.WriteZeros(CurrencyOffset - cursor);
                w.WriteU16(Medals);
                w.WriteU16(Coins);
                w.WriteZeros(4);
                w.WriteU32(ServerUnixTime);
                cursor = CurrencyOffset + CurrencyLength;
                currencies = true;
            }
            w.WriteZeros(TailOffsets[i] - cursor);
            TailEquipment[i].Write(w);
            cursor = TailOffsets[i] + ItemData.Length;
        }
        w.WriteZeros(TailLength - cursor);
    }

    private static void Require(int actual, int expected, string field)
    {
        if (actual != expected) throw new InvalidOperationException($"{field}: {actual} entries, expected {expected}");
    }
}

/// <summary>
/// The two bags as the 0x03 and 0xFC packets carry them: 24 card records (76 B) then 24 item records (108 B), by slot
/// (6 columns). An empty slot is all zeros. ✅ Sessions 52–56.
/// </summary>
public sealed record InventoryBags(CardData[] Cards, ItemData[] Items)
{
    public const int Size = 24;
    public const int Length = Size * (CardData.Length + ItemData.Length);

    public static InventoryBags Read(PacketReader r)
    {
        var cards = new CardData[Size];
        for (int i = 0; i < Size; i++) cards[i] = CardData.Read(r);
        var items = new ItemData[Size];
        for (int i = 0; i < Size; i++) items[i] = ItemData.Read(r);
        return new(cards, items);
    }

    public void Write(PacketWriter w)
    {
        if (Cards.Length != Size || Items.Length != Size)
            throw new InvalidOperationException($"bags need {Size} cards and {Size} items, not {Cards.Length} and {Items.Length}");
        foreach (var card in Cards) card.Write(w);
        foreach (var item in Items) item.Write(w);
    }
}

/// <summary>✅ 0x03 S2C (4416 B, v322): the inventory on entering the map. Required to load the map.</summary>
public sealed record InventoryDataPacket(InventoryBags Bags) : ITwoWayPacket<InventoryDataPacket>
{
    public static ushort Opcode => (ushort)MapOpcode.InventoryData;
    public static PacketLength Length => PacketLength.Exact(InventoryBags.Length);

    public static InventoryDataPacket Read(PacketReader r) => new(InventoryBags.Read(r));

    public void Write(PacketWriter w) => Bags.Write(w);
}
