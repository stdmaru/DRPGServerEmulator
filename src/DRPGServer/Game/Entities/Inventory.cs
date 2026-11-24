using DRPGServer.Data.GameData;

namespace DRPGServer.Game.Entities;

/// <summary>
/// Onde fica um item ou card, pelo código que o 0x90/0x91/0xBD usam como origem e destino.
/// ✅ 1 chão, 2 os 6 Card Slots equipados (0x90), 3 inventário / mochila de cards (sessões 53, 56, 64);
/// 0x33 é a bolsa extra equipada (❓ não implementada).
/// </summary>
public enum ItemPlace : byte
{
    Ground = 1,
    CardSlots = 2,
    Inventory = 3,
    ExtraBag = 0x33,
}

/// <summary>Bolsa: o inventário (108 B), a mochila de cards (76 B no 0x03) e os 6 Card Slots equipados (76 B no 0x02).</summary>
public enum BagKind : byte
{
    Battle = 2,
    Inventory = 3,
    EquippedCards = 4,
}

public sealed class InventoryItem
{
    public required uint Uid { get; init; }
    public required uint ItemId { get; init; }
    /// <summary>Linha do <c>item_info_us</c> (null se for card ou se a tabela não tem o item).</summary>
    public ItemInfo? Info { get; init; }
    /// <summary>Linha do <c>card_info_us</c> (null se for item normal ou se a tabela não tem o card).</summary>
    public CardInfo? CardInfo { get; init; }
    public int Slot { get; set; }
    public uint Amount { get; set; }

    /// <summary>A grade tem 6 colunas (✅ 0x91/0x90: linha 3, coluna 5 é a última posição).</summary>
    public int Row => Slot / Inventory.Columns;
    public int Column => Slot % Inventory.Columns;
}

/// <summary>Uma bolsa de itens ou cards (24 posições nas mochilas; 6 nos Card Slots equipados).</summary>
public sealed class Bag
{
    private readonly InventoryItem?[] _slots;
    private readonly Inventory _owner;

    internal Bag(Inventory owner, BagKind kind, int size = Inventory.Size)
    {
        _owner = owner;
        Kind = kind;
        _slots = new InventoryItem?[size];
    }

    public BagKind Kind { get; }
    public int Capacity => _slots.Length;
    public bool IsCardBag => Kind is BagKind.Battle or BagKind.EquippedCards;
    public IEnumerable<InventoryItem> Items => _slots.OfType<InventoryItem>();
    public InventoryItem? this[int slot] => slot >= 0 && slot < _slots.Length ? _slots[slot] : null;
    public bool HasFreeSlot => Array.IndexOf(_slots, null) >= 0;

    public InventoryItem? Find(uint itemId) => _slots.FirstOrDefault(i => i?.ItemId == itemId);
    public uint Count(uint itemId) => (uint)Items.Where(i => i.ItemId == itemId).Sum(i => (long)i.Amount);

    /// <summary>Quanto ainda cabe do item (pilhas existentes + posições livres).</summary>
    public uint Room(uint itemId)
    {
        uint max = _owner.MaxStackOf(itemId, IsCardBag);
        long room = Items.Where(i => i.ItemId == itemId).Sum(i => (long)(max - Math.Min(max, i.Amount)));
        room += (long)_slots.Count(s => s is null) * max;
        return (uint)Math.Min(room, uint.MaxValue);
    }

    /// <summary>
    /// Empilha nas pilhas existentes e depois ocupa posições livres. Devolve as posições alteradas (vazio se não
    /// coube nada; nada é adicionado se não couber tudo).
    /// </summary>
    public IReadOnlyList<InventoryItem> Add(uint itemId, uint amount)
    {
        if (amount == 0 || Room(itemId) < amount) return [];
        uint max = _owner.MaxStackOf(itemId, IsCardBag);
        var changed = new List<InventoryItem>();
        foreach (var stack in _slots.Where(s => s?.ItemId == itemId && s.Amount < max).Cast<InventoryItem>())
        {
            uint put = Math.Min(amount, max - stack.Amount);
            stack.Amount += put;
            amount -= put;
            changed.Add(stack);
            if (amount == 0) return changed;
        }
        while (amount > 0)
        {
            int slot = Array.IndexOf(_slots, null);
            uint put = Math.Min(amount, max);
            var item = _owner.Create(itemId, slot, put, isCard: IsCardBag);
            _slots[slot] = item;
            amount -= put;
            changed.Add(item);
        }
        return changed;
    }

    /// <summary>Tira a quantidade das pilhas do item (as primeiras posições antes). Devolve as posições alteradas.</summary>
    public IReadOnlyList<InventoryItem> Remove(uint itemId, uint amount)
    {
        if (amount == 0 || Count(itemId) < amount) return [];
        var changed = new List<InventoryItem>();
        foreach (var stack in _slots.Where(s => s?.ItemId == itemId).Cast<InventoryItem>().ToList())
        {
            uint take = Math.Min(amount, stack.Amount);
            stack.Amount -= take;
            amount -= take;
            changed.Add(stack);
            if (stack.Amount == 0) _slots[stack.Slot] = null;
            if (amount == 0) break;
        }
        return changed;
    }

    /// <summary>Gasta <paramref name="amount"/> de uma pilha (uso de item). A posição esvazia quando acaba.</summary>
    public bool Consume(InventoryItem item, uint amount)
    {
        if (this[item.Slot] != item || item.Amount < amount) return false;
        item.Amount -= amount;
        if (item.Amount == 0) _slots[item.Slot] = null;
        return true;
    }

    public InventoryItem? FindByUid(uint uid) => _slots.FirstOrDefault(i => i?.Uid == uid);

    /// <summary>Coloca um item salvo (carga do banco).</summary>
    public bool Place(InventoryItem item)
    {
        if (item.Slot < 0 || item.Slot >= _slots.Length || _slots[item.Slot] is not null) return false;
        _slots[item.Slot] = item;
        _owner.SeenUid(item.Uid);
        return true;
    }

    /// <summary>✅ 0x91/0x90 dentro da mesma bolsa: move para a posição; se estiver ocupada, troca. Devolve o que estava lá.</summary>
    public bool Move(int from, int to, out InventoryItem? displaced)
    {
        displaced = null;
        if (from < 0 || from >= _slots.Length || to < 0 || to >= _slots.Length || from == to || _slots[from] is not { } item)
            return false;
        displaced = _slots[to];
        _slots[to] = item;
        item.Slot = to;
        _slots[from] = displaced;
        if (displaced is not null) displaced.Slot = from;
        return true;
    }

    /// <summary>
    /// ✅ 0x90 entre bolsas compatíveis (mochila de cards ↔ 6 Card Slots): move para o destino; se ocupado, troca.
    /// </summary>
    public bool MoveTo(int from, Bag destination, int to, out InventoryItem? displaced)
    {
        if (ReferenceEquals(this, destination))
            return Move(from, to, out displaced);

        displaced = null;
        if (from < 0 || from >= _slots.Length || to < 0 || to >= destination._slots.Length || _slots[from] is not { } item)
            return false;

        displaced = destination._slots[to];
        destination._slots[to] = item;
        item.Slot = to;
        _slots[from] = displaced;
        if (displaced is not null) displaced.Slot = from;
        return true;
    }

    /// <summary>✅ 0xBD: jogar no chão destrói o item inteiro da posição.</summary>
    public InventoryItem? Destroy(int slot)
    {
        if (this[slot] is not { } item) return null;
        _slots[slot] = null;
        return item;
    }

    /// <summary>
    /// 0x148: auto-organiza a bolsa — junta pilhas parciais do mesmo item até <see cref="Inventory.MaxStackOf"/>,
    /// ordena por (tipo, subtipo, itemId, quantidade desc, uid) e compacta a partir do slot 0.
    /// </summary>
    public void Sort()
    {
        var items = Items.ToList();
        if (items.Count == 0) return;

        // 1. Junta pilhas parciais do mesmo item (preservando o menor uid de cada grupo)
        var merged = new List<InventoryItem>(items.Count);
        foreach (var group in items.GroupBy(i => i.ItemId))
        {
            uint max = _owner.MaxStackOf(group.Key, IsCardBag);
            var ordered = group.OrderBy(i => i.Uid).ToList();
            long total = ordered.Sum(i => (long)i.Amount);
            int idx = 0;
            while (total > 0)
            {
                uint put = (uint)Math.Min(total, max);
                var entry = idx < ordered.Count ? ordered[idx++] : _owner.Create(group.Key, 0, put, isCard: IsCardBag);
                entry.Amount = put;
                merged.Add(entry);
                total -= put;
            }
        }

        // 2. Ordena por categoria (Type, SubType), ItemId, pilhas cheias primeiro e UID
        merged.Sort((a, b) =>
        {
            byte typeA = IsCardBag ? (a.CardInfo?.Type ?? a.Info?.Type ?? 0) : (a.Info?.Type ?? 0);
            byte typeB = IsCardBag ? (b.CardInfo?.Type ?? b.Info?.Type ?? 0) : (b.Info?.Type ?? 0);
            int c = typeA.CompareTo(typeB);
            if (c != 0) return c;
            byte subA = IsCardBag ? (a.CardInfo?.SubType ?? a.Info?.SubType ?? 0) : (a.Info?.SubType ?? 0);
            byte subB = IsCardBag ? (b.CardInfo?.SubType ?? b.Info?.SubType ?? 0) : (b.Info?.SubType ?? 0);
            c = subA.CompareTo(subB);
            if (c != 0) return c;
            c = a.ItemId.CompareTo(b.ItemId);
            if (c != 0) return c;
            c = b.Amount.CompareTo(a.Amount);
            return c != 0 ? c : a.Uid.CompareTo(b.Uid);
        });

        // 3. Reatribui slots contíguos 0..N-1
        Array.Clear(_slots);
        for (int i = 0; i < merged.Count && i < _slots.Length; i++)
        {
            merged[i].Slot = i;
            _slots[i] = merged[i];
        }
    }
}

/// <summary>Inventário do personagem: aba Items, aba Cards, 6 Card Slots equipados e contador de uids.</summary>
public sealed class Inventory
{
    public const int Size = 24;
    public const int Columns = 6;
    public const int CardSlotCount = 6;

    private readonly Func<uint, ItemInfo?> _lookup;
    private readonly Func<uint, CardInfo?> _cardLookup;
    private readonly uint _maxStackCap;
    private uint _nextUid = 1;

    /// <param name="lookup">Linha do item em <c>item_info_us</c>.</param>
    /// <param name="maxStackCap">Teto de pilha (configuração).</param>
    /// <param name="cardLookup">Linha do card em <c>card_info_us</c> (opcional em testes sintéticos).</param>
    public Inventory(Func<uint, ItemInfo?> lookup, uint maxStackCap, Func<uint, CardInfo?>? cardLookup = null)
    {
        _lookup = lookup;
        _cardLookup = cardLookup ?? (_ => null);
        _maxStackCap = Math.Max(1, maxStackCap);
        Main = new Bag(this, BagKind.Inventory, Size);
        Battle = new Bag(this, BagKind.Battle, Size);
        CardSlots = new Bag(this, BagKind.EquippedCards, CardSlotCount);
    }

    /// <summary>Sem tabela de itens: toda pilha vai até <paramref name="maxStack"/>.</summary>
    public Inventory(uint maxStack) : this(_ => null, maxStack, null) { }

    public Bag Main { get; }
    /// <summary>✅ Aba Cards (24 slots, registros de 76 bytes no 0x03/0xFC; recebe pelo 0xC8; move pelo 0x90).</summary>
    public Bag Battle { get; }
    /// <summary>✅ 6 Card Slots equipados no inventário (0x02 offsets 784..1239; 0x90 place = 2).</summary>
    public Bag CardSlots { get; }
    public IEnumerable<InventoryItem> Items => Main.Items;

    public Bag BagOf(BagKind kind) => kind switch
    {
        BagKind.Battle => Battle,
        BagKind.EquippedCards => CardSlots,
        _ => Main,
    };

    internal uint MaxStackOf(uint itemId, bool isCard = false)
    {
        uint tableMax = isCard
            ? _cardLookup(itemId)?.MaxStack ?? _lookup(itemId)?.MaxStack ?? _maxStackCap
            : _lookup(itemId)?.MaxStack ?? _maxStackCap;
        return Math.Min(_maxStackCap, tableMax);
    }

    /// <summary>Item ou card novo com uid novo (ou o salvo, na carga).</summary>
    public InventoryItem Create(uint itemId, int slot, uint amount, uint uid = 0, bool isCard = false)
    {
        if (uid == 0) uid = _nextUid++;
        else SeenUid(uid);
        var cardInfo = isCard ? _cardLookup(itemId) : null;
        var itemInfo = isCard && cardInfo is not null ? null : _lookup(itemId);
        return new InventoryItem
        {
            Uid = uid,
            ItemId = itemId,
            Info = itemInfo,
            CardInfo = cardInfo,
            Slot = slot,
            Amount = amount,
        };
    }

    internal void SeenUid(uint uid) => _nextUid = Math.Max(_nextUid, uid + 1);

    // atalhos do inventário principal
    public InventoryItem? Find(uint itemId) => Main.Find(itemId);
    public bool HasFreeSlot => Main.HasFreeSlot;
    public InventoryItem? Add(uint itemId, uint amount) => Main.Add(itemId, amount) is [.., var last] ? last : null;
    public bool Remove(uint itemId, uint amount) => Main.Remove(itemId, amount).Count > 0;
}
