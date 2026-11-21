using DRPGServer.Game.Entities;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>Trecho comum de item (84..96 bytes capturados, ❓ durabilidade/preço/limites).</summary>
internal static class ItemBlob
{
    // ❓ E7 03 = 999, E8 03 = 1000, 64 = 100, 10 27 = 10000: parecem limites e preço
    public static void WriteLimits(PacketWriter w) =>
        w.WriteBytes([0xE7, 0x03, 0x00, 0x00, 0xE8, 0x03, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0x10, 0x27, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00]);

    public static void WriteFlags(PacketWriter w) =>
        w.WriteBytes([0x0F, 0x27, 0x00, 0x7D, 0x40, 0x42, 0x0F, 0x00, 0x01, 0x01, 0x00, 0x01]);
}

/// <summary>0x03: inventário na entrada do mapa. ❓ Ainda um bloco zerado (inventário vazio).</summary>
public sealed class InventoryDataPacket : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.InventoryData;
    protected override void Write(PacketWriter w) => w.WriteZeros(4416);
}

/// <summary>0x13: item no chão (ou remoção, com o bloco zerado).</summary>
public sealed class ItemDropPacket(ItemDrop drop, bool removed = false) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.ItemDrop;

    protected override void Write(PacketWriter w)
    {
        if (removed)
        {
            w.WriteZeros(108);
        }
        else
        {
            w.WriteU32(drop.Uid);
            w.WriteU32(drop.ItemId);
            w.WriteBytes([0x05, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00]);
            ItemBlob.WriteLimits(w);
            w.WriteZeros(52);
            ItemBlob.WriteFlags(w);
            w.WriteBytes([0xE5, 0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
        }
        w.WriteI16(drop.X);
        w.WriteI16(drop.Y);
    }
}

/// <summary>0x91: o jogador pegou o item do chão; ele vai para a posição do inventário.</summary>
public sealed class ItemDropGetPacket(ItemDrop drop, InventoryItem item) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.ItemDropGet;

    protected override void Write(PacketWriter w)
    {
        w.WriteBytes([0x01, 0x03, 0x00, 0x00]);
        w.WriteI32(drop.Y);
        w.WriteI32(drop.X);
        w.WriteI32(item.Row);
        w.WriteI32(item.Column);
        w.WriteU32(drop.Uid);
        w.WriteU32(drop.ItemId);
        w.WriteBytes([0x05, 0x01, 0x00, 0x00]);
        w.WriteU32(item.Amount);
        ItemBlob.WriteLimits(w);
        w.WriteZeros(52);
        ItemBlob.WriteFlags(w);
        w.WriteU32(drop.Uid + drop.ItemId);     // ❓ checagem
        // ✅ 236 bytes nas capturas; o protótipo copiou o tamanho do pedido C2S (250).
        // ❓ o layout do oficial difere já no offset 0 (03 33 ...); refazer com captura.
        w.WriteZeros(112);
    }
}

/// <summary>0xC9: item recebido (NPC).</summary>
public sealed class ItemReceivePacket(InventoryItem item) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.InventoryItemReceive;

    protected override void Write(PacketWriter w)
    {
        w.WriteBytes([0x01, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
        w.WriteU32((uint)item.Slot);
        w.WriteU32(item.Uid);
        w.WriteU32(item.ItemId);
        w.WriteSparse(92,
            (0, [0x0C, 0x02, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01]),
            (24, [0x01]),
            (80, [0x0F, 0x27, 0x00, 0x7D, 0x40, 0x42, 0x0F]));
        w.WriteU32(item.Uid + item.ItemId);     // ❓ checagem
        w.WriteZeros(484);
    }
}
