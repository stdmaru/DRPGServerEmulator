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

/// <summary>
/// Registro de item de 108 bytes, igual no 0x03 e no 0x97 (✅ sessões 52–56).
/// uid, item, 92 bytes de dados do item (❓ tipo, limites, preço: vêm da tabela do oficial),
/// checagem = uid + item e 4 bytes zero. A quantidade fica em +12.
/// </summary>
internal static class ItemRecord
{
    public const int Length = 108;

    public static void Write(PacketWriter w, InventoryItem item)
    {
        w.WriteU32(item.Uid);
        w.WriteU32(item.ItemId);
        // ❓ modelo capturado de carta/livro (0C 02); sementes usam 05 01 e limite 999
        w.WriteBytes([0x0C, 0x02, 0x00, 0x00]);  // +8
        w.WriteU32(item.Amount);                 // +12 quantidade
        w.WriteSparse(84,                        // +16
            (0, [0x01, 0x00, 0x00, 0x00, 0x01]),
            (16, [0x01]),
            (72, [0x0F, 0x27, 0x00, 0x7D, 0x40, 0x42, 0x0F]));
        w.WriteU32(item.Uid + item.ItemId);      // +100 checagem
        w.WriteU32(0);
    }
}

/// <summary>
/// 0x03: inventário na entrada do mapa (4416 bytes). ✅ sessões 52–56:
/// 24 registros de 76 bytes (❓ bolsa de itens de evolução: Digimentals, Crests, Woodmon's Leaf)
/// e 24 registros de <see cref="ItemRecord"/> (inventário, pela posição).
/// </summary>
public sealed class InventoryDataPacket(Inventory inventory) : ServerPacket
{
    public const int EvolutionRecord = 76;

    public override ushort Opcode => (ushort)MapOpcode.InventoryData;

    protected override void Write(PacketWriter w)
    {
        w.WriteZeros(Inventory.Size * EvolutionRecord);  // ❓ a bolsa de evolução ainda não existe no emulador
        var bySlot = inventory.Items.ToDictionary(i => i.Slot);
        for (int slot = 0; slot < Inventory.Size; slot++)
        {
            if (bySlot.TryGetValue(slot, out var item)) ItemRecord.Write(w, item);
            else w.WriteZeros(ItemRecord.Length);
        }
    }
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
        ItemRecord.Write(w, item);
        w.WriteZeros(480);
    }
}
