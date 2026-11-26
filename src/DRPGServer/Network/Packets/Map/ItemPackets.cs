using DRPGServer.Data.GameData;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Stats;
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

    public static void Write(PacketWriter w, InventoryItem item) =>
        Write(w, item.Uid, item.ItemId, item.Info, item.Amount, item.ExpiresAt);

    public static void Write(PacketWriter w, uint uid, uint itemId, ItemInfo? info, uint amount, uint expiresAt = 0)
    {
        info ??= new ItemInfo(itemId, "");
        bool isTimed = expiresAt > 0;
        uint wireAmount = isTimed ? expiresAt : amount;
        uint wireStack = isTimed
            ? Math.Max(Inventory.MinClientDurationSeconds, info.DurationSeconds)
            : info.MaxStack;

        w.WriteU32(uid);                         // 0
        w.WriteU32(itemId);                      // 4
        w.WriteU8(info.Type);                    // 8
        w.WriteU8(info.SubType);
        w.WriteU16(0);
        w.WriteU32(wireAmount);                  // 12 ✅ quantidade ou Unix timestamp UTC de expiração (conn=238/428)
        w.WriteU32(wireStack);                   // 16 ✅ pilha (1..999) ou duração em segundos (> 999)
        w.WriteU32(info.BuyPrice);               // 20
        w.WriteU32(info.SellPrice);              // 24
        w.WriteU32(Unknown28(itemId));           // 28 ❓
        w.WriteU32(info.Level);                  // 32
        WriteEffectTriplets(w, info.Modifiers);      // 36..87 (52 B): até 4 triplas (id, statCode, value) + 4 zeros
        w.WriteU16(Unknown88(info));                // 88 ❓
        w.WriteU16(info.CoinPrice);                  // 90
        w.WriteU32(info.LadderPrice);                  // 92
        w.WriteU8(info.UnknownCol23);                  // 96
        w.WriteU8(info.UnknownCol24);
        w.WriteU8(0);
        w.WriteU8(info.UnknownCol32);
        w.WriteU32(uid + itemId);                // 100 checagem
        w.WriteU32(0);                           // 104
    }

    /// <summary>
    /// ❓ <c>+88</c> (<c>u16</c>): 9999 ou 32000 nas capturas, sem coluna da tabela que explique. Não segue o tipo: há
    /// tipo 7 com 9999 (Taming Net #10022) e com 32000 (#23798); a coluna 13 = 6 bate com 32000 em quase todos.
    /// Mantido o valor antigo por tipo até achar a fonte. Não é preço: a loja de Medals usa o <c>Price</c> do XML.
    /// </summary>
    private static ushort Unknown88(ItemInfo info) => info.Type == 7 ? (ushort)32000 : (ushort)9999;

    /// <summary>
    /// ✅ Capturas v322 (<c>conn=234/238/428</c>): offsets +36..83 guardam até 4 triplas <c>(effectId, statCode, value)</c>:
    /// 1=STR, 2=AGI, 3=CON, 4=INT (Digimental #215), 301=ATK, 302=DEF, 303=BR (Crest #107), 304=HP, 305=VP.
    /// </summary>
    private static void WriteEffectTriplets(PacketWriter w, StatModifiers bonus)
    {
        if (bonus.IsEmpty)
        {
            w.WriteZeros(52);
            return;
        }

        Span<(uint Id, uint Code, int Value)> triplets = stackalloc (uint, uint, int)[4];
        int count = 0;

        var attributes = bonus.Attributes;
        int atk = bonus.Flat(StatModifierKind.Atk), def = bonus.Flat(StatModifierKind.Def), br = bonus.Flat(StatModifierKind.Br);
        int hp = bonus.Flat(StatModifierKind.Hp), vp = bonus.Flat(StatModifierKind.Vp);
        AddTriplet(triplets, ref count, (uint)attributes.Str, 1, attributes.Str);
        AddTriplet(triplets, ref count, (uint)attributes.Agi, 2, attributes.Agi);
        AddTriplet(triplets, ref count, (uint)attributes.Con, 3, attributes.Con);
        AddTriplet(triplets, ref count, (uint)attributes.Int, 4, attributes.Int);
        AddTriplet(triplets, ref count, (uint)(10_000 + atk), 301, atk);
        AddTriplet(triplets, ref count, (uint)(20_000 + def), 302, def);
        AddTriplet(triplets, ref count, (uint)(30_000 + br), 303, br);
        AddTriplet(triplets, ref count, (uint)(40_000 + hp), 304, hp);
        AddTriplet(triplets, ref count, (uint)(50_000 + vp), 305, vp);

        for (int i = 0; i < count; i++)
        {
            w.WriteU32(triplets[i].Id);
            w.WriteU32(triplets[i].Code);
            w.WriteI32(triplets[i].Value);
        }
        w.WriteZeros(52 - count * 12);
    }

    private static void AddTriplet(Span<(uint Id, uint Code, int Value)> triplets, ref int count, uint id, uint code, int value)
    {
        if (value > 0 && count < triplets.Length)
            triplets[count++] = (id, code, value);
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
        w.WriteU8(card.UnknownCol19);                         // 10
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
        w.WriteU16(card.CoinPrice);                         // 66
        w.WriteU32(card.LadderPrice);                         // 68
        w.WriteU8(card.UnknownCol30 != 0 ? card.UnknownCol27 : (byte)0); // 72
        w.WriteU8(card.UnknownCol30 != 0 ? card.UnknownCol29 : (byte)0); // 73
        w.WriteU8(0);                                   // 74
        w.WriteU8(card.UnknownCol30);                          // 75
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
                UnknownCol19 = 0,
                Trigger = CardTrigger.Attack,
                Restriction = 1,
                MaxStack = 999,
                BuyPrice = 1,
                SellPrice = 0,
                CoinPrice = 32000,
                LadderPrice = 1_000_000,
                UnknownCol30 = 0,
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
            CoinPrice = info?.CoinPrice ?? 32000,
            LadderPrice = info?.LadderPrice ?? 1_000_000,
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
        w.WriteF64(character.Wallet.Bits);
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
/// i32 × 2 da origem (linha e coluna; do chão, ✅ y e x do item, nas 27 coletas capturadas) e i32 × 2 do destino
/// (o pedido de pegar do chão manda 4, 6 = "qualquer lugar"; a resposta traz a posição usada).
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
