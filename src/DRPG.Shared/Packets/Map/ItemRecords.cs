using DRPG.Shared.Protocol;

namespace DRPG.Shared.Packets.Map;

/// <summary>✅ One of the (up to four) effect triplets of an item record: 1..4 = STR/AGI/CON/INT, 301..305 = ATK/DEF/BR/HP/VP.</summary>
public readonly record struct ItemEffect(uint Id, uint Code, int Value);

/// <summary>
/// 108-byte item record (0x03, 0x13, 0x91, 0x97, 0xBD, 0xC9, 0xFC; docs/protocolo/structs/inventario.md). An empty slot
/// is all zeros. The check at +100 is always <c>uid + itemId</c> ✅, so it is computed on write and verified on read.
/// <see cref="Amount"/> is the quantity, or the Unix expiry time of a timed item; <see cref="Stack"/> is the stack
/// size (1..999) or, above 999, the duration in seconds ✅.
/// </summary>
public sealed record ItemData(
    uint Uid, uint ItemId, byte Type, byte SubType, uint Amount, uint Stack, uint BuyPrice, uint SellPrice,
    uint Unknown28, uint Level, ItemEffect[] Effects, ushort Unknown88, ushort CoinPrice, uint LadderPrice,
    byte Column23, byte Column24, byte Column32)
{
    public const int Length = 108;
    public const int MaxEffects = 4;
    private const int EffectBytes = 52;

    public static readonly ItemData Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], 0, 0, 0, 0, 0, 0);

    public bool IsEmpty => Uid == 0 && ItemId == 0;

    public static ItemData Read(PacketReader r)
    {
        var uid = r.ReadU32();
        var itemId = r.ReadU32();
        var type = r.ReadU8();
        var subType = r.ReadU8();
        r.ReadU16();                            // 0
        var amount = r.ReadU32();
        var stack = r.ReadU32();
        var buy = r.ReadU32();
        var sell = r.ReadU32();
        var unknown28 = r.ReadU32();
        var level = r.ReadU32();
        var effects = new List<ItemEffect>(MaxEffects);
        for (int i = 0; i < MaxEffects; i++)
        {
            var effect = new ItemEffect(r.ReadU32(), r.ReadU32(), r.ReadI32());
            if (effect.Code != 0 && effects.Count == i) effects.Add(effect);   // the unused triplets are zero
        }
        r.Skip(EffectBytes - MaxEffects * 12);
        var unknown88 = r.ReadU16();
        var coin = r.ReadU16();
        var ladder = r.ReadU32();
        var col23 = r.ReadU8();
        var col24 = r.ReadU8();
        r.ReadU8();                             // 0
        var col32 = r.ReadU8();
        var check = r.ReadU32();
        r.Skip(4);
        if (check != unchecked(uid + itemId))
            throw new PacketFormatException($"item record {uid}/{itemId}: check {check}");
        return new(uid, itemId, type, subType, amount, stack, buy, sell, unknown28, level, effects.ToArray(),
            unknown88, coin, ladder, col23, col24, col32);
    }

    public void Write(PacketWriter w)
    {
        if (Effects.Length > MaxEffects)
            throw new InvalidOperationException($"item {ItemId}: {Effects.Length} effects, at most {MaxEffects}");
        w.WriteU32(Uid);                        // 0
        w.WriteU32(ItemId);                     // 4
        w.WriteU8(Type);                        // 8
        w.WriteU8(SubType);
        w.WriteU16(0);
        w.WriteU32(Amount);                     // 12
        w.WriteU32(Stack);                      // 16
        w.WriteU32(BuyPrice);                   // 20
        w.WriteU32(SellPrice);                  // 24
        w.WriteU32(Unknown28);                  // 28 ❓
        w.WriteU32(Level);                      // 32
        foreach (var effect in Effects)         // 36..87
        {
            w.WriteU32(effect.Id);
            w.WriteU32(effect.Code);
            w.WriteI32(effect.Value);
        }
        w.WriteZeros(EffectBytes - Effects.Length * 12);
        w.WriteU16(Unknown88);                  // 88 ❓
        w.WriteU16(CoinPrice);                  // 90
        w.WriteU32(LadderPrice);                // 92
        w.WriteU8(Column23);                    // 96
        w.WriteU8(Column24);
        w.WriteU8(0);
        w.WriteU8(Column32);
        w.WriteU32(unchecked(Uid + ItemId));    // 100 check
        w.WriteU32(0);                          // 104
    }
}

/// <summary>
/// 76-byte card record (0x02 offsets 784..1239, 0x03/0xFC first 24 slots, 0x90, 0xC8). ✅ Layout confirmed against
/// v322 captures: +0 uid, +4 card, +8 type, +9 subtype, +10 flag (column 19), +11 trigger (1 attack / 2 defence),
/// +12 restriction, +14 u16 amount, +16 stack, +20 buy, +24 sell, +28..39 effect (duration, code, value), +64 u16
/// ❓ (9999), +66 coin price, +68 ladder price, +72..75 flags. An empty slot is all zeros.
/// </summary>
public sealed record CardData(
    uint Uid, uint CardId, byte Type, byte SubType, byte Flag19, byte Trigger, byte Restriction, ushort Amount,
    uint Stack, uint BuyPrice, uint SellPrice, uint EffectDuration, uint EffectCode, uint EffectValue,
    ushort Unknown64, ushort CoinPrice, uint LadderPrice, byte Flag72, byte Flag73, byte Flag75)
{
    public const int Length = 76;

    public static readonly CardData Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public bool IsEmpty => Uid == 0 && CardId == 0;

    public static CardData Read(PacketReader r)
    {
        var uid = r.ReadU32();
        var cardId = r.ReadU32();
        var type = r.ReadU8();
        var subType = r.ReadU8();
        var flag19 = r.ReadU8();
        var trigger = r.ReadU8();
        var restriction = r.ReadU8();
        r.ReadU8();                             // 0
        var amount = r.ReadU16();
        var stack = r.ReadU32();
        var buy = r.ReadU32();
        var sell = r.ReadU32();
        var duration = r.ReadU32();
        var code = r.ReadU32();
        var value = r.ReadU32();
        r.Skip(24);                             // 40..63 zeros
        var unknown64 = r.ReadU16();
        var coin = r.ReadU16();
        var ladder = r.ReadU32();
        var flag72 = r.ReadU8();
        var flag73 = r.ReadU8();
        r.ReadU8();                             // 0
        var flag75 = r.ReadU8();
        return new(uid, cardId, type, subType, flag19, trigger, restriction, amount, stack, buy, sell,
            duration, code, value, unknown64, coin, ladder, flag72, flag73, flag75);
    }

    public void Write(PacketWriter w)
    {
        w.WriteU32(Uid);                        // 0
        w.WriteU32(CardId);                     // 4
        w.WriteU8(Type);                        // 8
        w.WriteU8(SubType);                     // 9
        w.WriteU8(Flag19);                      // 10
        w.WriteU8(Trigger);                     // 11
        w.WriteU8(Restriction);                 // 12
        w.WriteU8(0);                           // 13
        w.WriteU16(Amount);                     // 14
        w.WriteU32(Stack);                      // 16
        w.WriteU32(BuyPrice);                   // 20
        w.WriteU32(SellPrice);                  // 24
        w.WriteU32(EffectDuration);             // 28
        w.WriteU32(EffectCode);                 // 32
        w.WriteU32(EffectValue);                // 36
        w.WriteZeros(24);                       // 40..63
        w.WriteU16(Unknown64);                  // 64
        w.WriteU16(CoinPrice);                  // 66
        w.WriteU32(LadderPrice);                // 68
        w.WriteU8(Flag72);                      // 72
        w.WriteU8(Flag73);                      // 73
        w.WriteU8(0);                           // 74
        w.WriteU8(Flag75);                      // 75
    }
}
