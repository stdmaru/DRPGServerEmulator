using DRPGServer.Game.Entities;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>✅ 0x56 (C2S 18 B, v322): escolha num diálogo de NPC. u32 escolha e 14 B finais.</summary>
public sealed record NpcChoiceRequest(uint Choice) : IClientPacket<NpcChoiceRequest>
{
    public static PacketLength Length => PacketLength.Exact(18);

    public static NpcChoiceRequest Read(PacketReader r)
    {
        var choice = r.ReadU32();
        r.Skip(14);
        return new(choice);
    }
}

/// <summary>✅ 0x50 (cards) e 0x52 (itens) (C2S 18 B, v322): abrir a lista de uma loja de NPC. u32 loja e 14 B finais.</summary>
public sealed record NpcShopListRequest(uint ShopId) : IClientPacket<NpcShopListRequest>
{
    public static PacketLength Length => PacketLength.Exact(18);

    public static NpcShopListRequest Read(PacketReader r)
    {
        var shopId = r.ReadU32();
        r.Skip(14);
        return new(shopId);
    }
}

/// <summary>
/// ✅ 0x51 (C2S 126 B, DigimonRPG.bin): comprar (1) ou vender (2) card. u32 ação, registro do card (uid e id nos
/// primeiros 8 B), u32 quantidade, 12 B (pad + f64 bits), u16 lugar, u16, i32 coluna, i32 linha, u32 loja e 14 B finais.
/// </summary>
public sealed record CardShopTradeRequest(
    NpcShopAction Action, uint Uid, uint CardId, uint Quantity, ItemPlace Place, int Column, int Row, uint ShopId)
    : IClientPacket<CardShopTradeRequest>
{
    public static PacketLength Length => PacketLength.Exact(4 + CardRecord.Length + 4 + 12 + 4 + 12 + 14);

    public static CardShopTradeRequest Read(PacketReader r)
    {
        var action = (NpcShopAction)r.ReadU32();    // +0x00
        var uid = r.ReadU32();                      // +0x04 (registro +0)
        var cardId = r.ReadU32();                   // +0x08 (registro +4)
        r.Skip(CardRecord.Length - 8);              // +0x0C..+0x4F
        var quantity = r.ReadU32();                 // +0x50
        r.Skip(12);                                 // +0x54..+0x5F (pad + f64 bits)
        var place = (ItemPlace)r.ReadU16();         // +0x60
        r.Skip(2);                                  // +0x62
        var column = r.ReadI32();                   // +0x64
        var row = r.ReadI32();                      // +0x68
        var shopId = r.ReadU32();                   // +0x6C
        r.Skip(14);
        return new(action, uid, cardId, quantity, place, column, row, shopId);
    }
}

/// <summary>
/// ✅ 0x53 (C2S 158 B, v322): comprar (1) ou vender (2) item. u32 ação, registro do item (uid e id nos primeiros 8 B),
/// u32 quantidade, 14 B (pad + f64 bits + u16 0), u16 lugar, i32 coluna, i32 linha, u32 loja e 14 B finais.
/// ✅ DigimonRPG.bin @ 0x00ADD1CB: o cliente manda ação 1 para qualquer moeda; a loja define a moeda.
/// </summary>
public sealed record ItemShopTradeRequest(
    NpcShopAction Action, uint Uid, uint ItemId, uint Quantity, ItemPlace Place, int Column, int Row, uint ShopId)
    : IClientPacket<ItemShopTradeRequest>
{
    public static PacketLength Length => PacketLength.Exact(4 + ItemRecord.Length + 4 + 14 + 2 + 12 + 14);

    public static ItemShopTradeRequest Read(PacketReader r)
    {
        var action = (NpcShopAction)r.ReadU32();    // +0x00
        var uid = r.ReadU32();                      // +0x04 (registro +0)
        var itemId = r.ReadU32();                   // +0x08 (registro +4)
        r.Skip(ItemRecord.Length - 8);              // +0x0C..+0x6F
        var quantity = r.ReadU32();                 // +0x70
        r.Skip(14);                                 // +0x74..+0x81 (pad + f64 bits + u16 0)
        var place = (ItemPlace)r.ReadU16();         // +0x82
        var column = r.ReadI32();                   // +0x84
        var row = r.ReadI32();                      // +0x88
        var shopId = r.ReadU32();                   // +0x8C
        r.Skip(14);
        return new(action, uid, itemId, quantity, place, column, row, shopId);
    }
}

/// <summary>
/// ✅ 0x89 (C2S 750 B, v322): Digimon Depository. u16 página, u8 ação (1 abrir, 2 depositar, 3 retirar, 4 trocar), u8,
/// u32 uid do digimon do time, u32 uid do digimon guardado e 738 B não usados.
/// </summary>
public sealed record DepositoryActionRequest(ushort Page, DepositoryAction Action, uint PartyDigimonUid, uint DepositoryDigimonUid)
    : IClientPacket<DepositoryActionRequest>
{
    public static PacketLength Length => PacketLength.Exact(750);

    public static DepositoryActionRequest Read(PacketReader r)
    {
        var page = r.ReadU16();
        var action = (DepositoryAction)r.ReadU8();
        r.ReadU8();
        var partyUid = r.ReadU32();
        var depositoryUid = r.ReadU32();
        r.Skip(738);
        return new(page, action, partyUid, depositoryUid);
    }
}

/// <summary>
/// ✅ 0x94 (DigimonRPG.bin @ 0x00B7BBDA): expandir o Digimon Depository com o item 901/929. Nada é lido.
/// ❓ Tamanho do C2S não capturado na v322: aceita qualquer tamanho.
/// </summary>
public sealed record DepositoryExpandRequest : IClientPacket<DepositoryExpandRequest>
{
    public static PacketLength Length => PacketLength.AtLeast(0);

    public static DepositoryExpandRequest Read(PacketReader r)
    {
        r.Skip(r.Remaining);
        return new();
    }
}
