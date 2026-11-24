using DRPGServer.Data.GameData;
using DRPGServer.Game.Entities;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>
/// Registro de item de 108 bytes (0x03, 0x13, 0x91, 0x97, 0xBD, 0xC9, 0xFC). ✅ Campos tirados do <c>item_info_us</c>
/// (21 itens das sessões 52–61, ver docs/protocolo/structs/inventario.md):
/// uid, item, u8 tipo (col. 2), u8 subtipo (3), u16 0, quantidade, pilha (26), preço (17), venda (18), ❓, nível (20),
/// 52 B de efeitos ❓, u16 9999 (32000 nos do tipo 7), u16 col. 15, u32 col. 16, col. 23, 24, 0, 32, checagem uid + item, 0.
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
/// Registro de card de 76 bytes (0x02 offsets 784..1239, 0x03/0xFC primeiros 24 slots, 0x90, 0xC8).
/// ✅ Layout confirmado contra capturas v322 (cards #74 Golden Drill X, #19 Recovery Floppy L, #3 Attack Plug-In, #5 Defense Plug-In C):
/// +0 uid, +4 cardId, +8 tipo (col 2), +9 subtipo (col 3), +10 flag19 (col 19), +11 trigger (col 23: 1 ataque / 2 defesa),
/// +12 restrição (col 24), +13 zero, +14 u16 quantidade, +16 pilha (col 21), +20 preço (col 12), +24 venda (col 13),
/// +28..39 (duração, código e valor de efeito), +40..63 zeros, +64 u16 9999, +66 col 26, +68 col 28, +72..75 flags (col 27/29/30).
/// </summary>
public static class CardRecord
{
    public const int Length = 76;

    private static (uint Duration, uint Code, uint Value) EffectFields(uint cardId) => cardId switch
    {
        3 => (3000, 7, 20),     // ✅ Attack Plug-In / +20% DMG (conn=442)
        5 => (5000, 5, 20),     // ✅ Defense Plug-In C / +20% DEF (conn=446)
        19 => (5000, 20, 50),   // ✅ Recovery Floppy L / +50 HP (conn=428/442)
        74 => (0, 105, 1),      // ✅ Golden Drill X / AoE (conn=428/437)
        _ => (0, 0, 0),
    };

    public static void Write(PacketWriter w, InventoryItem item)
    {
        var card = item.CardInfo ?? FallbackCard(item);
        var (duration, code, value) = EffectFields(item.ItemId);

        w.WriteU32(item.Uid);                           // 0
        w.WriteU32(item.ItemId);                        // 4
        w.WriteU8(card.Type);                           // 8
        w.WriteU8(card.SubType);                        // 9
        w.WriteU8(card.Flag19);                         // 10
        w.WriteU8((byte)card.Trigger);                  // 11
        w.WriteU8(card.Restriction);                    // 12
        w.WriteU8(0);                                   // 13
        w.WriteU16((ushort)item.Amount);                // 14
        w.WriteU32(card.MaxStack);                      // 16
        w.WriteU32(card.BuyPrice);                      // 20
        w.WriteU32(card.SellPrice);                     // 24
        w.WriteU32(duration);                           // 28
        w.WriteU32(code);                               // 32
        w.WriteU32(value);                              // 36
        w.WriteZeros(24);                               // 40..63
        w.WriteU16(9999);                               // 64
        w.WriteU16(card.Col26);                         // 66
        w.WriteU32(card.Col28);                         // 68
        w.WriteU8(card.Col30 != 0 ? card.Col27 : (byte)0); // 72
        w.WriteU8(card.Col30 != 0 ? card.Col29 : (byte)0); // 73
        w.WriteU8(0);                                   // 74
        w.WriteU8(card.Col30);                          // 75
    }

    private static CardInfo FallbackCard(InventoryItem item)
    {
        if (item.ItemId == 74)
        {
            // ✅ Golden Drill X (fallback para pacotes sintéticos sem card_info_us.dat)
            return new CardInfo(74, "Golden Drill X")
            {
                Type = 4,
                SubType = 1,
                Flag19 = 0,
                Trigger = CardTrigger.Attack,
                Restriction = 1,
                MaxStack = 999,
                BuyPrice = 1,
                SellPrice = 0,
                Col26 = 32000,
                Col28 = 1_000_000,
                Col30 = 0,
            };
        }
        var info = item.Info;
        return new CardInfo(item.ItemId, info?.Name ?? "")
        {
            Type = info?.Type ?? 0,
            SubType = info?.SubType ?? 0,
            MaxStack = info?.MaxStack ?? 999,
            BuyPrice = info?.BuyPrice ?? 0,
            SellPrice = info?.SellPrice ?? 0,
            Col26 = info?.Col16 ?? 32000,
            Col28 = info?.Col17 ?? 1_000_000,
        };
    }
}

/// <summary>Compatibilidade com código legado: delega para <see cref="CardRecord"/>.</summary>
public static class BattleItemRecord
{
    public const int Length = CardRecord.Length;
    public static void Write(PacketWriter w, InventoryItem item) => CardRecord.Write(w, item);
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
/// 0x90 (172 bytes): ✅ card movido, equipado, desequipado ou coletado (sessão 64+, conn=437, 442, 446).
/// Cabeçalho de 20 bytes + registro de 76 bytes do card movido + registro de 76 bytes do card trocado (ou zeros).
/// </summary>
public sealed class CardMovePacket(ItemMoveHeader header, InventoryItem card, InventoryItem? displaced = null) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.CardMove;

    protected override void Write(PacketWriter w)
    {
        header.Write(w);
        CardRecord.Write(w, card);
        if (displaced is not null) CardRecord.Write(w, displaced);
        else w.WriteZeros(CardRecord.Length);
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

/// <summary>
/// 0x85 (124 bytes): ✅ confirmação de teleporte no mesmo mapa via item 900/907 (Teleport / Teleport X).
/// Confirmado em captures.db (conn=16/125/137/158/234/469): i32 X, i32 Y, u32 3 (inventário), ItemRecord (108 B), u32 3.
/// </summary>
public sealed class ItemTeleportPacket(int targetX, int targetY, InventoryItem item) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.ItemTeleport;

    protected override void Write(PacketWriter w)
    {
        w.WriteI32(targetX);
        w.WriteI32(targetY);
        w.WriteU32((uint)ItemPlace.Inventory);
        ItemRecord.Write(w, item);
        w.WriteU32(3);                          // ✅ sempre 3 nas capturas (conn=16/125/137/158)
    }
}
