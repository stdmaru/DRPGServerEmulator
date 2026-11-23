using DRPGServer.Data.GameData;
using DRPGServer.Game.Entities;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>
/// Registro de item de 108 bytes (0x03, 0x13, 0x91, 0x97, 0xBD, 0xC9, 0xFC). ✅ Campos tirados do <c>item_info_us</c>
/// (21 itens das sessões 52–56, ver docs/protocolo/structs/inventario.md):
/// uid, item, u8 tipo (col. 2), u8 subtipo (3), u16 0, quantidade, pilha (27), preço (18), venda (19), ❓, nível (21),
/// 52 B de efeitos ❓, u16 9999 (32000 nos do tipo 7), u16 col. 16, u32 col. 17, col. 24, 25, 0, 33, checagem uid + item, 0.
/// </summary>
public static class ItemRecord
{
    public const int Length = 108;

    /// <summary>❓ u32 em +28: 10000 nas sementes e lágrimas, 500 no Double Burger, 100 no ticket; o resto 0.</summary>
    private static uint Unknown28(uint itemId) => itemId switch
    {
        41 or 48 => 10_000,
        29 => 500,
        20 => 100,
        _ => 0,
    };

    public static void Write(PacketWriter w, InventoryItem item) => Write(w, item.Uid, item.ItemId, item.Info, item.Amount);

    public static void Write(PacketWriter w, uint uid, uint itemId, ItemInfo? info, uint amount)
    {
        info ??= new ItemInfo(itemId, "");
        w.WriteU32(uid);                         // 0
        w.WriteU32(itemId);                      // 4
        w.WriteU8(info.Type);                    // 8
        w.WriteU8(info.SubType);
        w.WriteU16(0);
        w.WriteU32(amount);                      // 12
        w.WriteU32(info.MaxStack);               // 16
        w.WriteU32(info.BuyPrice);               // 20
        w.WriteU32(info.SellPrice);              // 24
        w.WriteU32(Unknown28(itemId));           // 28 ❓
        w.WriteU32(info.Level);                  // 32
        w.WriteZeros(52);                        // 36 ❓ efeitos (HP +150 etc.); zero nos itens do tutorial
        w.WriteU16(info.Type == 7 ? (ushort)32000 : (ushort)9999); // 88 ❓
        w.WriteU16(info.Col16);                  // 90
        w.WriteU32(info.Col17);                  // 92
        w.WriteU8(info.Flag24);                  // 96
        w.WriteU8(info.Flag25);
        w.WriteU8(0);
        w.WriteU8(info.Flag33);
        w.WriteU32(uid + itemId);                // 100 checagem
        w.WriteU32(0);                           // 104
    }
}

/// <summary>
/// ❓ Registro de 76 bytes da bolsa de batalha (os 24 primeiros do 0x03; 0xC8, 0x12). Só o Woodmon's Leaf do
/// tutorial tem modelo capturado; os outros saem com as colunas da tabela no mesmo lugar do registro de 108.
/// </summary>
public static class BattleItemRecord
{
    public const int Length = 76;

    public static void Write(PacketWriter w, InventoryItem item)
    {
        var info = item.Info ?? new ItemInfo(item.ItemId, "");
        w.WriteU32(item.Uid);                    // 0
        w.WriteU32(item.ItemId);                 // 4
        if (item.ItemId == 74)
        {
            // ✅ Woodmon's Leaf (sessões 52–56): 04 01 00 01, u8 1, u8 0, u16 quantidade, 999, 1, …, 105, 1
            w.WriteBytes([0x04, 0x01, 0x00, 0x01, 0x01, 0x00]);
            w.WriteU16((ushort)item.Amount);
            w.WriteU32(999);
            w.WriteU32(1);
            w.WriteZeros(8);
            w.WriteU32(105);
            w.WriteU32(1);
            w.WriteZeros(24);
        }
        else
        {
            w.WriteU8(info.Type);
            w.WriteU8(info.SubType);
            w.WriteU8(info.Flag24);
            w.WriteU8(info.Flag33);
            w.WriteU16(0);
            w.WriteU16((ushort)item.Amount);     // 14 ✅ quantidade
            w.WriteU32(info.MaxStack);
            w.WriteU32(info.BuyPrice);
            w.WriteU32(info.SellPrice);
            w.WriteZeros(36);
        }
        w.WriteU16(9999);                        // 64
        w.WriteU16(info.Col16);
        w.WriteU32(info.Col17);
        w.WriteU32(0);                           // 72
    }
}

/// <summary>As duas bolsas como no 0x03: 24 × 76 (bolsa de batalha) e 24 × 108 (inventário), pela posição.</summary>
internal static class InventoryLayout
{
    public const int Length = Inventory.Size * (BattleItemRecord.Length + ItemRecord.Length);

    public static void Write(PacketWriter w, Inventory inventory)
    {
        for (int slot = 0; slot < Inventory.Size; slot++)
        {
            if (inventory.Battle[slot] is { } b) BattleItemRecord.Write(w, b);
            else w.WriteZeros(BattleItemRecord.Length);
        }
        for (int slot = 0; slot < Inventory.Size; slot++)
        {
            if (inventory.Main[slot] is { } item) ItemRecord.Write(w, item);
            else w.WriteZeros(ItemRecord.Length);
        }
    }
}

/// <summary>0x03: inventário na entrada do mapa (4416 bytes). ✅ sessões 52–56.</summary>
public sealed class InventoryDataPacket(Inventory inventory) : ServerPacket
{
    public const int EvolutionRecord = BattleItemRecord.Length;

    public override ushort Opcode => (ushort)MapOpcode.InventoryData;
    protected override void Write(PacketWriter w) => InventoryLayout.Write(w, inventory);
}

/// <summary>0xFC (4424 bytes): ✅ f64 bits + as bolsas como no 0x03. O oficial manda depois de cada 0xC9.</summary>
public sealed class InventoryRefreshPacket(Character character) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.InventoryRefresh;

    protected override void Write(PacketWriter w)
    {
        w.WriteF64(character.Bits);
        InventoryLayout.Write(w, character.Inventory);
    }
}

/// <summary>0x13 (112 bytes): item no chão (registro + x, y) ou remoção (108 zeros + x, y). ✅ sessões 53, 56 e 61.</summary>
public sealed class ItemDropPacket(ItemDrop drop, bool removed = false) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.ItemDrop;

    protected override void Write(PacketWriter w)
    {
        if (removed) w.WriteZeros(ItemRecord.Length);
        else ItemRecord.Write(w, drop.Uid, drop.ItemId, drop.Info, 1);
        w.WriteI16(drop.X);
        w.WriteI16(drop.Y);
    }
}

/// <summary>
/// Cabeçalho comum do 0x91 e do 0xBD (20 bytes): ✅ u8 origem, u8 destino (<see cref="ItemPlace"/>), u16 0,
/// i32 × 2 da origem (linha e coluna; x e y quando é o chão) e i32 × 2 do destino (o pedido de pegar do chão
/// manda 4, 6 = "qualquer lugar"; a resposta traz a posição usada).
/// </summary>
public readonly record struct ItemMoveHeader(ItemPlace From, ItemPlace To, int FromA, int FromB, int ToA, int ToB)
{
    public const int Length = 20;

    public static ItemMoveHeader Read(PacketReader r)
    {
        var from = (ItemPlace)r.ReadU8();
        var to = (ItemPlace)r.ReadU8();
        r.ReadU16();
        return new(from, to, r.ReadI32(), r.ReadI32(), r.ReadI32(), r.ReadI32());
    }

    public void Write(PacketWriter w)
    {
        w.WriteU8((byte)From);
        w.WriteU8((byte)To);
        w.WriteU16(0);
        w.WriteI32(FromA);
        w.WriteI32(FromB);
        w.WriteI32(ToA);
        w.WriteI32(ToB);
    }
}

/// <summary>
/// 0x91 (236 bytes): item movido. ✅ Cabeçalho, o registro do item (depois de empilhar, com o uid da pilha) e um
/// segundo registro (❓ o item trocado de lugar; zero nas capturas).
/// </summary>
public sealed class ItemMovePacket(ItemMoveHeader header, InventoryItem item, InventoryItem? displaced = null) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.ItemDropGet;

    protected override void Write(PacketWriter w)
    {
        header.Write(w);
        ItemRecord.Write(w, item);
        if (displaced is not null) ItemRecord.Write(w, displaced);
        else w.WriteZeros(ItemRecord.Length);
    }
}

/// <summary>0xBD (240 bytes): ✅ item jogado no chão e destruído. Eco do cabeçalho e do registro, 108 zeros e u32 1.</summary>
public sealed class ItemDestroyPacket(ItemMoveHeader header, InventoryItem item) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.ItemDestroy;

    protected override void Write(PacketWriter w)
    {
        header.Write(w);
        ItemRecord.Write(w, item);
        w.WriteZeros(ItemRecord.Length);
        w.WriteU32(1);
    }
}

/// <summary>
/// 0xC9 (604 bytes): itens recebidos (NPC). ✅ u32 quantidade de entradas (até 5) e, por entrada, u32 3, u32 linha,
/// u32 coluna e o registro de 108 com o estado novo da posição (o pedido do Tunomon traz as sementes que sobraram e
/// a carta).
/// </summary>
public sealed class ItemReceivePacket(IReadOnlyList<InventoryItem> items) : ServerPacket
{
    public const int MaxEntries = 5;

    public ItemReceivePacket(InventoryItem item) : this([item]) { }

    public override ushort Opcode => (ushort)MapOpcode.InventoryItemReceive;

    protected override void Write(PacketWriter w)
    {
        var list = items.Take(MaxEntries).ToList();
        w.WriteU32((uint)list.Count);
        foreach (var item in list)
        {
            w.WriteU32((uint)ItemPlace.Inventory);
            w.WriteU32((uint)item.Row);
            w.WriteU32((uint)item.Column);
            if (item.Amount > 0) ItemRecord.Write(w, item);
            else w.WriteZeros(ItemRecord.Length);
        }
        w.WriteZeros((MaxEntries - list.Count) * (12 + ItemRecord.Length));
    }
}

/// <summary>0xC8 (444 bytes): ✅ item recebido na bolsa de batalha (o Woodmon's Leaf do fim do tutorial). Como o 0xC9, com registros de 76.</summary>
public sealed class BattleItemReceivePacket(IReadOnlyList<InventoryItem> items) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleItemReceive;

    protected override void Write(PacketWriter w)
    {
        var list = items.Take(ItemReceivePacket.MaxEntries).ToList();
        w.WriteU32((uint)list.Count);
        foreach (var item in list)
        {
            w.WriteU32((uint)ItemPlace.Inventory);
            w.WriteU32((uint)item.Row);
            w.WriteU32((uint)item.Column);
            BattleItemRecord.Write(w, item);
        }
        w.WriteZeros((ItemReceivePacket.MaxEntries - list.Count) * (12 + BattleItemRecord.Length));
    }
}

/// <summary>
/// 0x41 (808 bytes): ✅ item usado. u32 3 (inventário), registro do item depois do uso (quantidade 0 quando acaba) e
/// o bloco do digimon: na Taming Net, o digimon capturado (sessões 6 e 8, v242 com bloco de 688).
/// </summary>
public sealed class ItemUsePacket(InventoryItem item, Digimon digimon) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.ItemUse;

    protected override void Write(PacketWriter w)
    {
        w.WriteU32((uint)ItemPlace.Inventory);
        ItemRecord.Write(w, item);
        DigimonStatusBlock.Write(w, digimon);
    }
}

/// <summary>0xC3 (vazio): ✅ o item não pôde ser usado (sessão 56: carta e item 21011 usados em digimon do jogador).</summary>
public sealed class ItemUseFailedPacket : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.ItemUseFailed;
    protected override void Write(PacketWriter w) { }
}
