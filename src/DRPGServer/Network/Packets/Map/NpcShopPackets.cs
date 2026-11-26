using DRPGServer.Data.GameData;
using DRPGServer.Game.Economy;
using DRPGServer.Game.Entities;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>✅ Ações de compra/venda em lojas de NPC (<c>0x0051</c> e <c>0x0053</c>; <c>DigimonRPG.bin @ 0x00AEE718 / 0x00AEEC90</c>).</summary>
public enum NpcShopAction : uint
{
    Buy = 1,             // Bits (0xDD9)
    Sell = 2,            // Venda por Bits (0xDDD)
    BuyMedals = 3,       // Medals (0xDDA, ItemRecord +88)
    BuyCoins = 4,        // Coins / DR's Earned Coin (0xDDB, ItemRecord +90 = Col16)
    BuyLadderPoints = 5, // Ladder Points (0xDDC, ItemRecord +92 = Col17)
}

public static class NpcShopActions
{
    /// <summary>✅ A ação de compra que o 0x53 usa para cada moeda.</summary>
    public static NpcShopAction BuyWith(CurrencyKind currency) => currency switch
    {
        CurrencyKind.Medals => NpcShopAction.BuyMedals,
        CurrencyKind.Coins => NpcShopAction.BuyCoins,
        CurrencyKind.LadderPoints => NpcShopAction.BuyLadderPoints,
        _ => NpcShopAction.Buy,
    };
}

/// <summary>
/// ✅ Map 0x00CB (16 B): sincroniza Bits (<c>double</c>), Medals (<c>u32 → WORD [player+0x4070]</c>) e
/// Coins (<c>u32 → WORD [player+0x4072]</c>). Confirmado em <c>DigimonRPG.bin @ 0x00ABFAC2</c>.
/// </summary>
public sealed class CurrencyUpdatePacket(Character character) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.CurrencyUpdate;

    protected override void Write(PacketWriter w)
    {
        w.WriteF64(character.Wallet.Bits); // +0x00
        w.WriteU32(character.Wallet.Medals); // +0x08
        w.WriteU32(character.Wallet.Coins);  // +0x0C (16 B total)
    }
}

/// <summary>
/// ✅ Map 0x0050 (3040 B = 40 × 76 B): lista de cards de uma loja de NPC.
/// Confirmado em <c>captures.db</c> (conn 221/226/234) e <c>DigimonRPG.bin @ 0x00ABF37E</c>.
/// </summary>
public sealed class CardShopListPacket(NpcShop shop, GameData data) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.CardShopList;

    protected override void Write(PacketWriter w)
    {
        int written = 0;
        foreach (var entry in shop.Entries)
        {
            if (written >= NpcShop.MaxSlots) break;
            var card = data.FindCard(entry.ItemId);
            if (card is null) continue;
            if (entry.PriceOverride is { } price)
                card = card with { BuyPrice = price };

            var synthetic = new InventoryItem
            {
                Uid = 0,
                ItemId = card.Id,
                CardInfo = card,
                Amount = card.MaxStack,
            };
            CardRecord.Write(w, synthetic);
            written++;
        }

        for (int i = written; i < NpcShop.MaxSlots; i++)
            w.WriteZeros(CardRecord.Length);
    }
}

/// <summary>
/// ✅ Map 0x0052 (4320 B = 40 × 108 B): lista de itens de uma loja de NPC.
/// Confirmado em <c>captures.db</c> (conn 145/150/158/221) e <c>DigimonRPG.bin @ 0x00ABF416</c>.
/// </summary>
public sealed class ItemShopListPacket(NpcShop shop, GameData data, long unixNow) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.ItemShopList;

    protected override void Write(PacketWriter w)
    {
        long now = unixNow;
        int written = 0;
        foreach (var entry in shop.Entries)
        {
            if (written >= NpcShop.MaxSlots) break;
            var info = data.FindItem(entry.ItemId);
            if (info is null) continue;
            if (entry.PriceOverride is { } price)
            {
                info = shop.Currency switch
                {
                    CurrencyKind.Coins => info with { Col16 = (ushort)Math.Min(price, ushort.MaxValue) },
                    CurrencyKind.LadderPoints => info with { Col17 = price },
                    _ => info with { BuyPrice = price },
                };
            }

            uint expiresAt = info.IsTimed
                ? (uint)Math.Clamp(now + info.DurationSeconds, 1, uint.MaxValue)
                : 0u;
            uint amount = info.IsTimed ? 1u : info.MaxStack;
            ItemRecord.Write(w, uid: 0, info.Id, info, amount, expiresAt);
            written++;
        }

        for (int i = written; i < NpcShop.MaxSlots; i++)
            w.WriteZeros(ItemRecord.Length);
    }
}

/// <summary>
/// ✅ Map 0x0051 (112 B): resposta de compra (1) ou venda (2) de card em loja de NPC.
/// Confirmado em <c>DigimonRPG.bin @ 0x00ABF3E1 / 0x00AEE700</c>:
/// +0x00 u32 action, +0x04..+0x4F CardRecord (76 B), +0x50 u32 quantity, +0x54 u32 0,
/// +0x58 double bits, +0x60 u16 place (3), +0x62 u16 0, +0x64 u32 col, +0x68 u32 row, +0x6C u32 shopId.
/// </summary>
public sealed class CardShopTradePacket(
    NpcShopAction action,
    InventoryItem? item,
    uint quantity,
    double bits,
    ItemPlace place,
    int col,
    int row,
    uint shopId) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.CardShopTrade;

    protected override void Write(PacketWriter w)
    {
        w.WriteU32((uint)action);          // +0x00
        if (item is not null && item.Amount > 0)
            CardRecord.Write(w, item);     // +0x04..+0x4F (76 B)
        else
            w.WriteZeros(CardRecord.Length);
        w.WriteU32(quantity);              // +0x50
        w.WriteU32(0);                     // +0x54
        w.WriteF64(bits);                  // +0x58
        w.WriteU16((ushort)place);         // +0x60 (3 = Inventory/Cards)
        w.WriteU16(0);                     // +0x62
        w.WriteI32(col);                   // +0x64
        w.WriteI32(row);                   // +0x68
        w.WriteU32(shopId);                // +0x6C (112 B total)
    }
}

/// <summary>
/// ✅ Map 0x0053 (144 B): resposta de compra (1) ou venda (2) de item em loja de NPC.
/// Confirmado em <c>captures.db</c> (conn 145/221) e <c>DigimonRPG.bin @ 0x00ABF47A / 0x00AEEC90</c>:
/// +0x00 u32 action, +0x04..+0x6F ItemRecord (108 B), +0x70 u32 quantity, +0x74 u32 0,
/// +0x78 double bits, +0x80 u16 0, +0x82 u16 place (3), +0x84 u32 col, +0x88 u32 row, +0x8C u32 shopId.
/// </summary>
public sealed class ItemShopTradePacket(
    NpcShopAction action,
    InventoryItem? item,
    uint quantity,
    double bits,
    ItemPlace place,
    int col,
    int row,
    uint shopId,
    ushort coinsAfter = 0) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.ItemShopTrade;

    protected override void Write(PacketWriter w)
    {
        w.WriteU32((uint)action);          // +0x00
        if (item is not null && item.Amount > 0)
            ItemRecord.Write(w, item);     // +0x04..+0x6F (108 B)
        else
            w.WriteZeros(ItemRecord.Length);
        w.WriteU32(quantity);              // +0x70
        w.WriteU32(0);                     // +0x74
        w.WriteF64(bits);                  // +0x78
        w.WriteU16(coinsAfter);            // +0x80 ✅ novo saldo de Coins quando action == BuyCoins (0x00AEEDE5)
        w.WriteU16((ushort)place);         // +0x82 (3 = Inventory)
        w.WriteI32(col);                   // +0x84
        w.WriteI32(row);                   // +0x88
        w.WriteU32(shopId);                // +0x8C (144 B total)
    }
}
