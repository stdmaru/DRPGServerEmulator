using DRPG.Server.Data.GameData;
using DRPG.Server.Game.Entities;
using DRPG.Server.Game.Stats;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Server.Network.Packets.Map;

/// <summary>
/// Monta o registro de item de 108 bytes (<see cref="ItemData"/>, no <c>DRPG.Shared</c>) a partir do inventário.
/// ✅ Campos tirados do <c>item_info_us</c> (21 itens das sessões 52–61, ver docs/protocolo/structs/inventario.md).
/// </summary>
public static class ItemRecord
{
    public const int Length = ItemData.Length;

    /// <summary>❓ u32 em +28: 10000 nas sementes e lágrimas, 500 no Double Burger, 100 no ticket; o resto 0.</summary>
    private static uint Unknown28(uint itemId) => itemId switch
    {
        41 or 48 => 10_000,
        29 => 500,
        20 => 100,
        _ => 0,
    };

    /// <summary>O item de uma posição, ou o registro vazio (zeros).</summary>
    public static ItemData Of(InventoryItem? item) =>
        item is null ? ItemData.Empty : Of(item.Uid, item.ItemId, item.Info, item.Amount, item.ExpiresAt);

    public static ItemData Of(uint uid, uint itemId, ItemInfo? info, uint amount, uint expiresAt = 0)
    {
        info ??= new ItemInfo(itemId, "");
        bool isTimed = expiresAt > 0;
        return new ItemData(
            uid, itemId, info.Type, info.SubType,
            Amount: isTimed ? expiresAt : amount,       // ✅ quantidade ou Unix timestamp UTC de expiração (conn=238/428)
            Stack: isTimed                              // ✅ pilha (1..999) ou duração em segundos (> 999)
                ? Math.Max(Inventory.MinClientDurationSeconds, info.DurationSeconds)
                : info.MaxStack,
            info.BuyPrice, info.SellPrice, Unknown28(itemId), info.Level,
            Effects(info.Modifiers), Unknown88(info), info.CoinPrice, info.LadderPrice,
            info.UnknownCol23, info.UnknownCol24, info.UnknownCol32);
    }

    public static void Write(PacketWriter w, InventoryItem item) => Of(item).Write(w);

    public static void Write(PacketWriter w, uint uid, uint itemId, ItemInfo? info, uint amount, uint expiresAt = 0) =>
        Of(uid, itemId, info, amount, expiresAt).Write(w);

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
    private static ItemEffect[] Effects(StatModifiers bonus)
    {
        if (bonus.IsEmpty) return [];

        var effects = new List<ItemEffect>(ItemData.MaxEffects);
        var attributes = bonus.Attributes;
        int atk = bonus.Flat(StatModifierKind.Atk), def = bonus.Flat(StatModifierKind.Def), br = bonus.Flat(StatModifierKind.Br);
        int hp = bonus.Flat(StatModifierKind.Hp), vp = bonus.Flat(StatModifierKind.Vp);
        Add(effects, (uint)attributes.Str, 1, attributes.Str);
        Add(effects, (uint)attributes.Agi, 2, attributes.Agi);
        Add(effects, (uint)attributes.Con, 3, attributes.Con);
        Add(effects, (uint)attributes.Int, 4, attributes.Int);
        Add(effects, (uint)(10_000 + atk), 301, atk);
        Add(effects, (uint)(20_000 + def), 302, def);
        Add(effects, (uint)(30_000 + br), 303, br);
        Add(effects, (uint)(40_000 + hp), 304, hp);
        Add(effects, (uint)(50_000 + vp), 305, vp);
        return effects.ToArray();
    }

    private static void Add(List<ItemEffect> effects, uint id, uint code, int value)
    {
        if (value > 0 && effects.Count < ItemData.MaxEffects) effects.Add(new ItemEffect(id, code, value));
    }
}

/// <summary>
/// Monta o registro de card de 76 bytes (<see cref="CardData"/>, no <c>DRPG.Shared</c>). ✅ Layout confirmado contra
/// capturas v322 (cards #74 Golden Drill X, #19 Recovery Floppy L, #3 Attack Plug-In, #5 Defense Plug-In C).
/// </summary>
public static class CardRecord
{
    public const int Length = CardData.Length;

    private static (uint Duration, uint Code, uint Value) EffectFields(uint cardId) => cardId switch
    {
        3 => (3000, 7, 20),     // ✅ Attack Plug-In / +20% DMG (conn=442)
        5 => (5000, 5, 20),     // ✅ Defense Plug-In C / +20% DEF (conn=446)
        19 => (5000, 20, 50),   // ✅ Recovery Floppy L / +50 HP (conn=428/442)
        74 => (0, 105, 1),      // ✅ Golden Drill X / AoE (conn=428/437)
        _ => (0, 0, 0),
    };

    /// <summary>O card de uma posição, ou o registro vazio (zeros).</summary>
    public static CardData Of(InventoryItem? item)
    {
        if (item is null) return CardData.Empty;
        var card = item.CardInfo ?? FallbackCard(item);
        var (duration, code, value) = EffectFields(item.ItemId);
        return new CardData(
            item.Uid, item.ItemId, card.Type, card.SubType, card.UnknownCol19, (byte)card.Trigger, card.Restriction,
            (ushort)item.Amount, card.MaxStack, card.BuyPrice, card.SellPrice, duration, code, value,
            Unknown64: 9999, card.CoinPrice, card.LadderPrice,
            Flag72: card.UnknownCol30 != 0 ? card.UnknownCol27 : (byte)0,
            Flag73: card.UnknownCol30 != 0 ? card.UnknownCol29 : (byte)0,
            Flag75: card.UnknownCol30);
    }

    public static void Write(PacketWriter w, InventoryItem item) => Of(item).Write(w);

    /// <summary>Card fora do <c>card_info_us</c>: monta o registro com o que o item tiver.</summary>
    private static CardInfo FallbackCard(InventoryItem item)
    {
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

/// <summary>As duas bolsas como no 0x03: 24 × 76 (bolsa de batalha) e 24 × 108 (inventário), pela posição.</summary>
internal static class InventoryLayout
{
    public const int Length = InventoryBags.Length;

    public static InventoryBags Of(Inventory inventory)
    {
        var cards = new CardData[InventoryBags.Size];
        var items = new ItemData[InventoryBags.Size];
        for (int slot = 0; slot < InventoryBags.Size; slot++)
        {
            cards[slot] = CardRecord.Of(inventory.Battle[slot]);
            items[slot] = ItemRecord.Of(inventory.Main[slot]);
        }
        return new(cards, items);
    }

    public static void Write(PacketWriter w, Inventory inventory) => Of(inventory).Write(w);
}

/// <summary>0x03: inventário na entrada do mapa (4416 bytes). ✅ sessões 52–56. O pacote é o <see cref="InventoryDataPacket"/> do Shared.</summary>
public static class InventoryData
{
    public const int EvolutionRecord = CardRecord.Length;

    public static InventoryDataPacket Of(Inventory inventory) => new(InventoryLayout.Of(inventory));
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
            CardRecord.Write(w, item);
        }
        w.WriteZeros((ItemReceivePacket.MaxEntries - list.Count) * (12 + CardRecord.Length));
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
