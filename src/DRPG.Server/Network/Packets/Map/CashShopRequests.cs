using DRPG.Server.Data.GameData;
using DRPG.Shared.Protocol;

namespace DRPG.Server.Network.Packets.Map;

/// <summary>✅ 0x159 (C2S 78 B, v322): abrir o Cash Shop. u32 palavra de cabeçalho (ecoada) e 74 B não usados.</summary>
public sealed record CashShopOpenRequest(uint HeaderWord) : IClientPacket<CashShopOpenRequest>
{
    public static PacketLength Length => PacketLength.Exact(78);

    public static CashShopOpenRequest Read(PacketReader r)
    {
        var header = r.ReadU32();
        r.Skip(74);
        return new(header);
    }
}

/// <summary>
/// ✅ 0x160 (C2S 70 B, DigimonRPG.bin @ 0x0096C386): comprar. u32 palavra de cabeçalho, 20 B (erro, ação, pad, conta),
/// u32 itemDataId, u32 quantidade, u32 preço unitário (ignorado: o servidor usa o catálogo) e 34 B não usados.
/// </summary>
public sealed record CashShopBuyRequest(uint HeaderWord, uint ProductId, uint Quantity) : IClientPacket<CashShopBuyRequest>
{
    public static PacketLength Length => PacketLength.Exact(70);

    public static CashShopBuyRequest Read(PacketReader r)
    {
        var header = r.ReadU32();               // +0x00
        r.Skip(20);                             // +0x04..+0x17
        var productId = r.ReadU32();            // +0x18
        var quantity = r.ReadU32();             // +0x1C
        r.ReadU32();                            // +0x20 preço unitário do cliente, ignorado
        r.Skip(34);
        return new(header, productId, quantity);
    }
}

/// <summary>
/// ✅ 0x161 (C2S 1006 B, v322): histórico de compras. u32 palavra de cabeçalho, 8 B, u16 ano, u16 mês, u16 página e
/// 988 B não usados.
/// </summary>
public sealed record CashShopHistoryRequest(uint HeaderWord, ushort Year, ushort Month, ushort Page) : IClientPacket<CashShopHistoryRequest>
{
    public static PacketLength Length => PacketLength.Exact(1006);

    public static CashShopHistoryRequest Read(PacketReader r)
    {
        var header = r.ReadU32();               // +0x00
        r.Skip(8);                              // +0x04..+0x0B
        var year = r.ReadU16();                 // +0x0C
        var month = r.ReadU16();                // +0x0E
        var page = r.ReadU16();                 // +0x10
        r.Skip(988);
        return new(header, year, month, page);
    }
}

/// <summary>
/// ✅ 0xCA (C2S 117 B, v322): autenticar e abrir o Cash Depository. Usuário[21], hash da senha[80], u8 0, u8 ação
/// (1 = abrir) e 14 B finais.
/// </summary>
public sealed record CashDepositoryAuthRequest(string Username, string PasswordHash, byte Action) : IClientPacket<CashDepositoryAuthRequest>
{
    public static PacketLength Length => PacketLength.Exact(117);

    public static CashDepositoryAuthRequest Read(PacketReader r)
    {
        var username = r.ReadFixedString(21);   // +0x00..+0x14
        var hash = r.ReadFixedString(80);       // +0x15..+0x64
        r.ReadU8();                             // +0x65 (0 no C2S)
        var action = r.ReadU8();                // +0x66
        r.Skip(14);
        return new(username, hash, action);
    }

    public override string ToString() => $"{nameof(CashDepositoryAuthRequest)}({Username}, {Action})";
}

/// <summary>✅ 0xDE (C2S 4434 B, v322): listar os 24 cards e 24 itens do Cash Depository. Nada é lido.</summary>
public sealed record CashDepositoryListRequest : IClientPacket<CashDepositoryListRequest>
{
    public static PacketLength Length => PacketLength.Exact(4434);

    public static CashDepositoryListRequest Read(PacketReader r)
    {
        r.Skip(4434);
        return new();
    }
}

/// <summary>
/// ✅ 0xDD (C2S 206 B, v322): retirar do Cash Depository. u32 tipo (1 = card, 2 = item), registro do card (uid nos
/// primeiros 4 B), registro do item (uid nos primeiros 4 B) e 18 B finais.
/// </summary>
public sealed record CashDepositoryWithdrawRequest(uint Kind, uint CardUid, uint ItemUid) : IClientPacket<CashDepositoryWithdrawRequest>
{
    public static PacketLength Length => PacketLength.Exact(4 + CardRecord.Length + ItemRecord.Length + 18);

    public static CashDepositoryWithdrawRequest Read(PacketReader r)
    {
        var kind = r.ReadU32();                 // +0x00
        var cardUid = r.ReadU32();              // +0x04
        r.Skip(CardRecord.Length - 4);          // +0x08..+0x4F
        var itemUid = r.ReadU32();              // +0x50
        r.Skip(ItemRecord.Length - 4);
        r.Skip(18);
        return new(kind, cardUid, itemUid);
    }

    /// <summary>Uid do registro do tipo pedido; null se o tipo não é card nem item.</summary>
    public uint? UidOf(CashShopItemKind kind) => kind switch
    {
        CashShopItemKind.Card => CardUid,
        CashShopItemKind.Item => ItemUid,
        _ => null,
    };
}
