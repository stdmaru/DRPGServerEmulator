namespace DRPGServer.Game.Entities;

public sealed class InventoryItem
{
    public required uint Uid { get; init; }
    public required uint ItemId { get; init; }
    public required int Slot { get; init; }
    public uint Amount { get; set; }

    /// <summary>A grade do inventário tem 6 colunas.</summary>
    public int Row => Slot / Inventory.Columns;
    public int Column => Slot % Inventory.Columns;
}

/// <summary>Inventário de itens (24 posições). ❓ Ainda não é persistido: na entrada do mapa o 0x03 sai vazio.</summary>
public sealed class Inventory(uint maxStack)
{
    public const int Size = 24;
    public const int Columns = 6;

    private readonly InventoryItem?[] _slots = new InventoryItem?[Size];
    private uint _nextUid = 1;

    public IEnumerable<InventoryItem> Items => _slots.OfType<InventoryItem>();

    public InventoryItem? Find(uint itemId) => _slots.FirstOrDefault(i => i?.ItemId == itemId);

    public bool HasFreeSlot => Array.IndexOf(_slots, null) >= 0;

    /// <summary>Empilha no item existente ou ocupa a primeira posição livre. Null se não couber.</summary>
    public InventoryItem? Add(uint itemId, uint amount)
    {
        if (amount == 0) return null;
        if (Find(itemId) is { } existing)
        {
            existing.Amount = (uint)Math.Min((ulong)existing.Amount + amount, maxStack);
            return existing;
        }
        int slot = Array.IndexOf(_slots, null);
        if (slot < 0) return null;
        var item = new InventoryItem { Uid = _nextUid++, ItemId = itemId, Slot = slot, Amount = Math.Min(amount, maxStack) };
        _slots[slot] = item;
        return item;
    }

    public bool Remove(uint itemId, uint amount)
    {
        var item = Find(itemId);
        if (item is null || item.Amount < amount) return false;
        item.Amount -= amount;
        if (item.Amount == 0) _slots[item.Slot] = null;
        return true;
    }
}
