using DRPGServer.Data.GameData;

namespace DRPGServer.Game.Entities;

/// <summary>
/// Onde fica um item, pelo código que o 0x91/0xBD usam como origem e destino. ✅ 1 chão, 3 inventário
/// (sessões 53 e 56); 0x33 é a bolsa extra equipada (❓ não implementada).
/// </summary>
public enum ItemPlace : byte
{
    Ground = 1,
    Inventory = 3,
    ExtraBag = 0x33,
}

/// <summary>Bolsa: o inventário (registros de 108 bytes) e a bolsa de batalha (❓ registros de 76 bytes no 0x03).</summary>
public enum BagKind : byte
{
    Inventory = 3,
    Battle = 2,
}

public sealed class InventoryItem
{
    public required uint Uid { get; init; }
    public required uint ItemId { get; init; }
    /// <summary>Linha do <c>item_info_us</c> (null se a tabela não tem o item).</summary>
    public ItemInfo? Info { get; init; }
    public int Slot { get; set; }
    public uint Amount { get; set; }

    /// <summary>A grade tem 6 colunas (✅ 0x91: linha 3, coluna 5 é a última posição).</summary>
    public int Row => Slot / Inventory.Columns;
    public int Column => Slot % Inventory.Columns;
}

/// <summary>Uma bolsa de 24 posições.</summary>
public sealed class Bag
{
    private readonly InventoryItem?[] _slots = new InventoryItem?[Inventory.Size];
    private readonly Inventory _owner;

    internal Bag(Inventory owner, BagKind kind)
    {
        _owner = owner;
        Kind = kind;
    }

    public BagKind Kind { get; }
    public IEnumerable<InventoryItem> Items => _slots.OfType<InventoryItem>();
    public InventoryItem? this[int slot] => slot is >= 0 and < Inventory.Size ? _slots[slot] : null;
    public bool HasFreeSlot => Array.IndexOf(_slots, null) >= 0;

    public InventoryItem? Find(uint itemId) => _slots.FirstOrDefault(i => i?.ItemId == itemId);
    public uint Count(uint itemId) => (uint)Items.Where(i => i.ItemId == itemId).Sum(i => (long)i.Amount);

    /// <summary>Quanto ainda cabe do item (pilhas existentes + posições livres).</summary>
    public uint Room(uint itemId)
    {
        uint max = _owner.MaxStackOf(itemId);
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
        uint max = _owner.MaxStackOf(itemId);
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
            var item = _owner.Create(itemId, slot, put);
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
        if (item.Slot is < 0 or >= Inventory.Size || _slots[item.Slot] is not null) return false;
        _slots[item.Slot] = item;
        _owner.SeenUid(item.Uid);
        return true;
    }

    /// <summary>✅ 0x91 dentro do inventário: move para a posição; se estiver ocupada, troca. Devolve o que estava lá.</summary>
    public bool Move(int from, int to, out InventoryItem? displaced)
    {
        displaced = null;
        if (from is < 0 or >= Inventory.Size || to is < 0 or >= Inventory.Size || from == to || _slots[from] is not { } item)
            return false;
        displaced = _slots[to];
        _slots[to] = item;
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
}

/// <summary>Inventário do personagem: as duas bolsas e o contador de uids (único por personagem).</summary>
public sealed class Inventory
{
    public const int Size = 24;
    public const int Columns = 6;

    private readonly Func<uint, ItemInfo?> _lookup;
    private readonly uint _maxStackCap;
    private uint _nextUid = 1;

    /// <param name="lookup">Linha do item na tabela.</param>
    /// <param name="maxStackCap">Teto de pilha (configuração); a pilha de cada item é a coluna 27 do <c>item_info_us</c>.</param>
    public Inventory(Func<uint, ItemInfo?> lookup, uint maxStackCap)
    {
        _lookup = lookup;
        _maxStackCap = Math.Max(1, maxStackCap);
        Main = new Bag(this, BagKind.Inventory);
        Battle = new Bag(this, BagKind.Battle);
    }

    /// <summary>Sem tabela de itens: toda pilha vai até <paramref name="maxStack"/>.</summary>
    public Inventory(uint maxStack) : this(_ => null, maxStack) { }

    public Bag Main { get; }
    /// <summary>❓ Bolsa dos registros de 76 bytes do 0x03 (Woodmon's Leaf, Meat, Digimentals); recebe pelo 0xC8.</summary>
    public Bag Battle { get; }
    public IEnumerable<InventoryItem> Items => Main.Items;

    public Bag BagOf(BagKind kind) => kind == BagKind.Battle ? Battle : Main;

    internal uint MaxStackOf(uint itemId) => Math.Min(_maxStackCap, _lookup(itemId)?.MaxStack ?? _maxStackCap);

    /// <summary>Item novo com uid novo (ou o salvo, na carga).</summary>
    public InventoryItem Create(uint itemId, int slot, uint amount, uint uid = 0)
    {
        if (uid == 0) uid = _nextUid++;
        else SeenUid(uid);
        return new InventoryItem { Uid = uid, ItemId = itemId, Info = _lookup(itemId), Slot = slot, Amount = amount };
    }

    internal void SeenUid(uint uid) => _nextUid = Math.Max(_nextUid, uid + 1);

    // atalhos do inventário principal
    public InventoryItem? Find(uint itemId) => Main.Find(itemId);
    public bool HasFreeSlot => Main.HasFreeSlot;
    public InventoryItem? Add(uint itemId, uint amount) => Main.Add(itemId, amount) is [.., var last] ? last : null;
    public bool Remove(uint itemId, uint amount) => Main.Remove(itemId, amount).Count > 0;
}
