using DRPGServer.Game;
using DRPGServer.Game.Entities;
using DRPG.Shared.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>
/// ✅ 0x90 (C2S 186 B, v322): mover, equipar, desequipar ou coletar card. <see cref="ItemMoveHeader"/>, registro do card
/// (uid e id nos primeiros 8 B), ❓ segundo registro de card e 14 B finais.
/// </summary>
public sealed record CardMoveRequest(ItemMoveHeader Header, uint Uid, uint CardId) : IClientPacket<CardMoveRequest>
{
    public static PacketLength Length => PacketLength.Exact(ItemMoveHeader.Length + 2 * CardRecord.Length + 14);

    public static CardMoveRequest Read(PacketReader r)
    {
        var header = ItemMoveHeader.Read(r);
        var uid = r.ReadU32();
        var cardId = r.ReadU32();
        r.Skip(CardRecord.Length - 8);          // resto do registro
        r.Skip(CardRecord.Length);              // ❓ segundo registro, não usado
        r.Skip(14);
        return new(header, uid, cardId);
    }
}

/// <summary>
/// ✅ 0x91 (C2S 250 B, v322): mover item (chão → inventário, dentro do inventário, equipar, desequipar).
/// <see cref="ItemMoveHeader"/>, registro do item (uid e id nos primeiros 8 B), ❓ segundo registro e 14 B finais.
/// </summary>
public sealed record ItemMoveRequest(ItemMoveHeader Header, uint Uid, uint ItemId) : IClientPacket<ItemMoveRequest>
{
    public static PacketLength Length => PacketLength.Exact(ItemMoveHeader.Length + 2 * ItemRecord.Length + 14);

    public static ItemMoveRequest Read(PacketReader r)
    {
        var header = ItemMoveHeader.Read(r);
        var uid = r.ReadU32();
        var itemId = r.ReadU32();
        r.Skip(ItemRecord.Length - 8);          // resto do registro
        r.Skip(ItemRecord.Length);              // ❓ segundo registro, não usado
        r.Skip(14);
        return new(header, uid, itemId);
    }
}

/// <summary>
/// ✅ 0xBD (C2S 254 B, v322): jogar item no chão (destrói). <see cref="ItemMoveHeader"/> (inventário → chão), registro do
/// item (uid nos primeiros 4 B), ❓ segundo registro e 18 B finais.
/// </summary>
public sealed record ItemDestroyRequest(ItemMoveHeader Header, uint Uid) : IClientPacket<ItemDestroyRequest>
{
    public static PacketLength Length => PacketLength.Exact(ItemMoveHeader.Length + 2 * ItemRecord.Length + 18);

    public static ItemDestroyRequest Read(PacketReader r)
    {
        var header = ItemMoveHeader.Read(r);
        var uid = r.ReadU32();
        r.Skip(ItemRecord.Length - 4);          // resto do registro
        r.Skip(ItemRecord.Length);              // ❓ segundo registro, não usado
        r.Skip(18);
        return new(header, uid);
    }
}

/// <summary>
/// ✅ 0x148 (C2S 38 B, v322): auto-organizar. u32 ação (0), u32 0, u16 bolsa (1 = inventário, 2 = bolsa de batalha;
/// 3/4 = warehouse), u16 1 e 26 B finais.
/// </summary>
public sealed record InventorySortRequest(uint Action, ushort BagType) : IClientPacket<InventorySortRequest>
{
    public static PacketLength Length => PacketLength.Exact(38);

    public static InventorySortRequest Read(PacketReader r)
    {
        var action = r.ReadU32();
        r.ReadU32();
        var bagType = r.ReadU16();
        r.ReadU16();
        r.Skip(26);
        return new(action, bagType);
    }
}

/// <summary>
/// ✅ 0x41 (C2S 822 B, v322): usar item. u32 3, registro do item (uid nos primeiros 4 B), bloco de status do alvo
/// (u32 uid, 0 no selvagem, e o serial nos 16 B seguintes) e 14 B finais.
/// </summary>
public sealed record ItemUseRequest(uint ItemUid, uint TargetUid, Serial Target) : IClientPacket<ItemUseRequest>
{
    public static PacketLength Length => PacketLength.Exact(4 + ItemRecord.Length + DigimonStatusBlock.Length + 14);

    public static ItemUseRequest Read(PacketReader r)
    {
        r.ReadU32();                            // 3
        var itemUid = r.ReadU32();
        r.Skip(ItemRecord.Length - 4);          // resto do registro
        var targetUid = r.ReadU32();
        var target = Serial.Read(r);
        r.Skip(DigimonStatusBlock.Length - 4 - Serial.Length);
        r.Skip(14);
        return new(itemUid, targetUid, target);
    }
}

/// <summary>
/// ✅ 0x85 (C2S 138 B, v322): teleporte no mesmo mapa com o item 900/907. i32 x, i32 y, u32 lugar (3 = inventário),
/// registro do item (uid e id nos primeiros 8 B) e 18 B zerados (conn=125/234/469).
/// </summary>
public sealed record ItemTeleportRequest(int X, int Y, uint Place, uint Uid, uint ItemId) : IClientPacket<ItemTeleportRequest>
{
    public static PacketLength Length => PacketLength.Exact(12 + ItemRecord.Length + 18);

    public static ItemTeleportRequest Read(PacketReader r)
    {
        var x = r.ReadI32();
        var y = r.ReadI32();
        var place = r.ReadU32();
        var uid = r.ReadU32();
        var itemId = r.ReadU32();
        r.Skip(ItemRecord.Length - 8);          // resto do registro
        r.Skip(18);
        return new(x, y, place, uid, itemId);
    }
}
