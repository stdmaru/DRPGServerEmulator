using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Entities;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>✅ Códigos de resultado de <c>S2C 0x00DD</c> (confirmados em <c>DigimonRPG.bin @ 0x00AFD06B..0x00AFD0CE</c>).</summary>
public enum CashDepositoryWithdrawResult : uint
{
    Failed = 0,         // string 0xE69
    Ok = 1,             // string 0xE6A (cliente limpa o slot pelo Uid e recebe 0xC8/0xC9)
    InventoryFull = 2,  // string 0xE6B
    NotFound = 3,       // string 0xE6C
}

/// <summary>
/// ✅ Map 0x0159 (64 B): resposta de abertura do Cash Shop / consulta de saldo.
/// Confirmado em <c>captures.db</c> (sessão 53 mark 11) e <c>DigimonRPG.bin @ 0x00ACA318 / 0x0097C610</c>:
/// o cliente exibe <c>cash1 + cash2</c> em <c>[InGameShop+0x1548]</c> e abre a janela <c>0x12C</c>.
/// </summary>
public sealed class CashShopOpenPacket(
    uint headerWord,
    int errorCode,
    uint accountNo,
    uint cash1,
    uint cash2,
    string username) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.CashShopOpen;

    protected override void Write(PacketWriter w)
    {
        w.WriteU32(headerWord);            // +0x00
        w.WriteI32(errorCode);             // +0x04 (0 = sucesso; != 0 exibe erro)
        w.WriteU8(2);                      // +0x08 (2 = resposta)
        w.WriteZeros(7);                   // +0x09..+0x0F
        w.WriteI64(accountNo);             // +0x10 (armazenado em [InGameShop+0x1180])
        w.WriteI64(cash1);                 // +0x18 (cash principal)
        w.WriteI64(cash2);                 // +0x20 (cash bônus)
        w.WriteFixedString(username, 24);  // +0x28..+0x3F (64 B total)
    }
}

/// <summary>
/// ✅ Map 0x0160 (56 B): resposta de compra no Cash Shop.
/// Confirmado em <c>DigimonRPG.bin @ 0x0096C330 / 0x00ACA380</c>:
/// o cliente soma <c>cash1After (+0x28) + cash2After (+0x30)</c>, atualiza o saldo e mostra confirmação (<c>0x12E</c> modo 3).
/// </summary>
public sealed class CashShopBuyPacket(
    uint headerWord,
    int errorCode,
    uint accountNo,
    uint productId,
    uint quantity,
    uint unitPrice,
    uint cash1After,
    uint cash2After) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.CashShopBuy;

    protected override void Write(PacketWriter w)
    {
        w.WriteU32(headerWord);            // +0x00
        w.WriteI32(errorCode);             // +0x04
        w.WriteU8(2);                      // +0x08 (2 = resposta)
        w.WriteZeros(7);                   // +0x09..+0x0F
        w.WriteI64(accountNo);             // +0x10
        w.WriteU32(productId);             // +0x18
        w.WriteU32(quantity);              // +0x1C
        w.WriteI64(unitPrice);             // +0x20
        w.WriteI64(cash1After);            // +0x28 (lido em 0x00ACA39E)
        w.WriteI64(cash2After);            // +0x30 (lido em 0x00ACA392; 56 B total)
    }
}

/// <summary>
/// ✅ Map 0x0161 (992 B = 32 B cabeçalho + 20 × 48 B registros): histórico de compras do Cash Shop.
/// Confirmado em <c>captures.db</c> (conn 137/399) e <c>DigimonRPG.bin @ 0x00ACA3E9 / 0x0097C6E0 / 0x0097AC93..0x0097B03F</c>.
/// Cada registro (48 B): +0x00 i64 orderId, +0x08 u32 itemDataId, +0x0C u32 quantity, +0x10 i64 unitPrice,
/// +0x18 i64 cashAfter, +0x20 i64 bonusCashAfter, +0x28 i64 purchasedAt (Unix timestamp UTC).
/// </summary>
public sealed class CashShopHistoryPacket(
    uint headerWord,
    int errorCode,
    ushort year,
    ushort month,
    ushort page,
    ushort totalPages,
    uint accountNo,
    IReadOnlyList<CashShopHistoryEntry>? entries = null) : ServerPacket
{
    public const int MaxEntries = 20;
    public const int EntrySize = 48;

    public override ushort Opcode => (ushort)MapOpcode.CashShopHistory;

    protected override void Write(PacketWriter w)
    {
        w.WriteU32(headerWord);            // +0x00
        w.WriteI32(errorCode);             // +0x04
        w.WriteU8(2);                      // +0x08 (2 = resposta)
        w.WriteZeros(3);                   // +0x09..+0x0B
        w.WriteU16(year);                  // +0x0C
        w.WriteU16(month);                 // +0x0E
        w.WriteU16(page);                  // +0x10
        w.WriteU16(totalPages);            // +0x12
        w.WriteZeros(4);                   // +0x14..+0x17
        w.WriteI64(accountNo);             // +0x18

        int written = 0;
        if (entries is not null)
        {
            foreach (var e in entries)
            {
                if (written >= MaxEntries) break;
                w.WriteI64(e.OrderId);         // +0x00
                w.WriteU32(e.ItemDataId);      // +0x08
                w.WriteU32(e.Quantity);        // +0x0C
                w.WriteI64(e.UnitPrice);       // +0x10
                w.WriteI64(e.CashAfter);       // +0x18
                w.WriteI64(e.BonusCashAfter);  // +0x20
                w.WriteI64(e.PurchasedAt);     // +0x28 (48 B)
                written++;
            }
        }
        if (written < MaxEntries)
            w.WriteZeros((MaxEntries - written) * EntrySize);
    }
}

/// <summary>
/// ✅ Map 0x00CA (103 B): resposta de abertura/autenticação do Cash Depository (janela <c>0x46</c>).
/// Confirmado em <c>captures.db</c> (sessão 56 marks 61/75) e <c>DigimonRPG.bin @ 0x00AC0DDD</c>:
/// <c>result=1</c> (+101) e <c>action=1</c> (+102) abrem a janela e disparam <c>C2S 0x00DE</c>.
/// </summary>
public sealed class CashDepositoryAuthPacket(
    string username,
    string passwordHash,
    byte result,
    byte action) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.CashDepositoryAuth;

    protected override void Write(PacketWriter w)
    {
        w.WriteFixedString(username, 21);      // +0x00..+0x14 (21 B)
        w.WriteFixedString(passwordHash, 80);  // +0x15..+0x64 (80 B)
        w.WriteU8(result);                     // +0x65 (1 = sucesso)
        w.WriteU8(action);                     // +0x66 (1 = abrir; 103 B total)
    }
}

/// <summary>
/// ✅ Map 0x00DE (4420 B = 24 × 76 B cards + 24 × 108 B items + 4 B resultado): lista do Cash Depository.
/// Confirmado em <c>captures.db</c> (conn 121/137/141) e <c>DigimonRPG.bin @ 0x00AC010D / 0x00AFCD30</c>.
/// </summary>
public sealed class CashDepositoryListPacket(
    IReadOnlyList<InventoryItem?> cards,
    IReadOnlyList<InventoryItem?> items,
    bool success = true) : ServerPacket
{
    public const int SlotCount = 24;

    public override ushort Opcode => (ushort)MapOpcode.CashDepositoryList;

    protected override void Write(PacketWriter w)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (i < cards.Count && cards[i] is { } card)
                CardRecord.Write(w, card);
            else
                w.WriteZeros(CardRecord.Length);
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (i < items.Count && items[i] is { } item)
                ItemRecord.Write(w, item);
            else
                w.WriteZeros(ItemRecord.Length);
        }

        w.WriteU32(success ? 1u : 0u);     // +0x1140 (4420 B total)
    }
}

/// <summary>
/// ✅ Map 0x00DD (192 B = 4 B kind + 76 B CardRecord + 108 B ItemRecord + 4 B result): retirada do Cash Depository.
/// Confirmado em <c>captures.db</c> (conn 121/141) e <c>DigimonRPG.bin @ 0x00AC014A / 0x00AFD060</c>.
/// </summary>
public sealed class CashDepositoryWithdrawPacket(
    CashShopItemKind kind,
    InventoryItem? item,
    CashDepositoryWithdrawResult result) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.CashDepositoryWithdraw;

    protected override void Write(PacketWriter w)
    {
        w.WriteU32((uint)kind);            // +0x00 (1 = Card, 2 = Item)

        if (kind == CashShopItemKind.Card && item is not null)
            CardRecord.Write(w, item);     // +0x04..+0x4F (76 B)
        else
            w.WriteZeros(CardRecord.Length);

        if (kind == CashShopItemKind.Item && item is not null)
            ItemRecord.Write(w, item);     // +0x50..+0xBB (108 B)
        else
            w.WriteZeros(ItemRecord.Length);

        w.WriteU32((uint)result);          // +0xBC (192 B total)
    }
}
