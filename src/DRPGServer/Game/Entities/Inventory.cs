using DRPGServer.Data.GameData;
using DRPGServer.Game.World;

namespace DRPGServer.Game.Entities;

/// <summary>
/// Onde fica um item ou card, pelo código que o 0x90/0x91/0xBD usam como origem e destino.
/// ✅ 1 chão, 2 os 6 Card Slots equipados (0x90), 3 inventário / mochila de cards,
/// 4..9 Crests/Digimentals, 11..12 Aura/D-ark, 14..28 Roupas/Acessórios/Spirits, 81..86 Asas/Efeitos/Gem/Watch
/// (confirmado em <c>DigimonRPG.bin @ 0x00AF030C / 0x00B0E740..0x00B0FEFD</c> e capturas v322 <c>conn=234/428</c>).
/// </summary>
public enum ItemPlace : byte
{
    Ground = 1,
    CardSlots = 2,
    Inventory = 3,

    // Equipamentos primários (0x02 offsets 100..747, 0xB1 offsets 24..671)
    Crest1 = 4,
    Crest2 = 5,
    Crest3 = 6,
    Digimental1 = 7,
    Digimental2 = 8,
    Digimental3 = 9,

    // Equipamentos do Tamer (0x02 cauda 4724..7599)
    Aura = 11,
    Digivice = 12,
    Socks = 14,
    Shoes = 15,
    Pants = 16,
    Gloves = 17,
    Shirt = 18,
    Jacket = 19,
    Hat = 20,
    CostumeSet = 21,
    Earring = 22,
    Necklace = 23,
    Ring = 24,
    DarkCard = 25,
    Spirit1 = 26,
    Spirit2 = 27,
    Spirit3 = 28,

    ExtraBag = 0x33,

    // Cosméticos e acessórios adicionais (0x02 offsets 6868..7515)
    Wings = 81,
    FloatingEffect = 82,
    Emblem = 83,
    Font = 84,
    Gem = 85,
    Watch = 86,
}

/// <summary>Bolsa: o inventário (108 B), a mochila de cards (76 B no 0x03), os 6 Card Slots (76 B no 0x02) e os equipamentos (108 B no 0x02/0xB1).</summary>
public enum BagKind : byte
{
    Battle = 2,
    Inventory = 3,
    EquippedCards = 4,
    Equipment = 5,
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
    /// <summary>✅ Unix timestamp UTC de expiração (0 = permanente; gravado em +12 do ItemRecord quando &gt; 0).</summary>
    public uint ExpiresAt { get; set; }

    public bool IsTimed => ExpiresAt > 0;
    public bool IsExpired(long unixNow) => ExpiresAt > 0 && unixNow >= ExpiresAt;

    /// <summary>A grade tem 6 colunas (✅ 0x91/0x90: linha 3, coluna 5 é a última posição).</summary>
    public int Row => Slot / Inventory.Columns;
    public int Column => Slot % Inventory.Columns;
}

/// <summary>Uma bolsa de itens ou cards (24 posições nas mochilas; 6 nos Card Slots equipados).</summary>
/// <summary>O que um <see cref="Bag.Grant"/> mudou, para <see cref="Bag.Revert"/>.</summary>
public sealed class BagGrant
{
    internal readonly record struct Part(InventoryItem Item, uint Put, bool Created);

    internal BagGrant(Bag bag, IReadOnlyList<Part> parts)
    {
        Bag = bag;
        Parts = parts;
    }

    internal Bag Bag { get; }
    internal IReadOnlyList<Part> Parts { get; }

    /// <summary>Posições alteradas (para o pacote de recebimento).</summary>
    public IReadOnlyList<InventoryItem> Changed => Parts.Select(p => p.Item).ToList();
}

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
    /// Para itens não empilháveis (<c>MaxStack == 1</c>), <paramref name="durationSeconds"/> pode ser:
    /// <c>null</c> (usa duração padrão do <see cref="ItemInfo"/>), <c>-1</c> (permanente) ou <c>&gt; 0</c> (duração arbitrária em segundos).
    /// </summary>
    public IReadOnlyList<InventoryItem> Add(uint itemId, uint amount, long? durationSeconds = null) =>
        Grant(itemId, amount, durationSeconds)?.Changed ?? [];

    /// <summary>
    /// Como <see cref="Add"/>, mas devolve o que mudou de forma que <see cref="Revert"/> desfaça exatamente (rollback de
    /// transferência). Null se não couber tudo.
    /// </summary>
    public BagGrant? Grant(uint itemId, uint amount, long? durationSeconds = null)
    {
        if (amount == 0 || Room(itemId) < amount) return null;
        uint max = _owner.MaxStackOf(itemId, IsCardBag);
        var parts = new List<BagGrant.Part>();
        if (max > 1)
        {
            foreach (var stack in _slots.Where(s => s?.ItemId == itemId && s.Amount < max).Cast<InventoryItem>())
            {
                uint put = Math.Min(amount, max - stack.Amount);
                stack.Amount += put;
                amount -= put;
                parts.Add(new(stack, put, Created: false));
                if (amount == 0) return new BagGrant(this, parts);
            }
        }
        uint expiresAt = ResolveExpiresAt(itemId, max, durationSeconds);
        while (amount > 0)
        {
            int slot = Array.IndexOf(_slots, null);
            uint put = Math.Min(amount, max);
            var item = _owner.Create(itemId, slot, put, isCard: IsCardBag, expiresAt: expiresAt);
            _slots[slot] = item;
            amount -= put;
            parts.Add(new(item, put, Created: true));
        }
        return new BagGrant(this, parts);
    }

    /// <summary>Desfaz um <see cref="Grant"/> desta bolsa: tira as quantidades postas e as posições criadas.</summary>
    public void Revert(BagGrant grant)
    {
        if (grant.Bag != this) throw new ArgumentException("o grant é de outra bolsa", nameof(grant));
        for (int i = grant.Parts.Count - 1; i >= 0; i--)
        {
            var (item, put, created) = grant.Parts[i];
            int slot = Array.IndexOf(_slots, item);
            if (slot < 0) continue;
            item.Amount -= Math.Min(put, item.Amount);
            if (created || item.Amount == 0) _slots[slot] = null;
        }
    }

    private uint ResolveExpiresAt(uint itemId, uint maxStack, long? durationSeconds)
    {
        if (IsCardBag || maxStack > 1) return 0;
        if (durationSeconds == Inventory.PermanentDuration) return 0;

        uint effectiveDuration;
        if (durationSeconds is > 0)
            effectiveDuration = (uint)Math.Min(durationSeconds.Value, uint.MaxValue);
        else
            effectiveDuration = _owner.FindInfo(itemId)?.DurationSeconds ?? 0u;

        if (effectiveDuration == 0) return 0;
        long expires = _owner.Clock.UnixNow + effectiveDuration;
        return (uint)Math.Clamp(expires, 1, uint.MaxValue);
    }

    /// <summary>Remove todos os itens cujo <see cref="InventoryItem.ExpiresAt"/> já venceu em <paramref name="unixNow"/>.</summary>
    public int RemoveExpired(long unixNow)
    {
        int removed = 0;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] is { } item && item.IsExpired(unixNow))
            {
                _slots[i] = null;
                removed++;
            }
        }
        return removed;
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
    /// <summary>Valor sentinela para entregar um item não empilhável como permanente (sem expiração).</summary>
    public const int PermanentDuration = -1;
    /// <summary>O cliente interpreta +12 como timestamp de expiração somente quando +16 &gt; 999.</summary>
    public const uint MinClientDurationSeconds = 1000;

    private readonly Func<uint, ItemInfo?> _lookup;
    private readonly Func<uint, CardInfo?> _cardLookup;
    private readonly uint _maxStackCap;
    private uint _nextUid = 1;

    /// <param name="lookup">Linha do item em <c>item_info_us</c>.</param>
    /// <param name="maxStackCap">Teto de pilha (configuração).</param>
    /// <param name="cardLookup">Linha do card em <c>card_info_us</c>.</param>
    /// <param name="clock">Calendário para a expiração dos itens de período.</param>
    public Inventory(Func<uint, ItemInfo?> lookup, uint maxStackCap, Func<uint, CardInfo?> cardLookup, IGameClock clock)
    {
        Clock = clock;
        _lookup = lookup;
        _cardLookup = cardLookup;
        _maxStackCap = Math.Max(1, maxStackCap);
        Main = new Bag(this, BagKind.Inventory, Size);
        Battle = new Bag(this, BagKind.Battle, Size);
        CardSlots = new Bag(this, BagKind.EquippedCards, CardSlotCount);
        Equipment = new Bag(this, BagKind.Equipment, EquipmentSlots.BagCapacity);
    }

    public IGameClock Clock { get; }

    /// <summary>True se o item é de período e já venceu agora.</summary>
    public bool IsExpired(InventoryItem item) => item.IsExpired(Clock.UnixNow);

    public Bag Main { get; }
    /// <summary>✅ Aba Cards (24 slots, registros de 76 bytes no 0x03/0xFC; recebe pelo 0xC8; move pelo 0x90).</summary>
    public Bag Battle { get; }
    /// <summary>✅ 6 Card Slots equipados no inventário (0x02 offsets 784..1239; 0x90 place = 2).</summary>
    public Bag CardSlots { get; }
    /// <summary>✅ 29 slots de equipamento do Tamer (indexados por <see cref="ItemPlace"/>; 0x02 / 0xB1 / 0x91).</summary>
    public Bag Equipment { get; }
    public IEnumerable<InventoryItem> Items => Main.Items;

    public Bag BagOf(BagKind kind) => kind switch
    {
        BagKind.Battle => Battle,
        BagKind.EquippedCards => CardSlots,
        BagKind.Equipment => Equipment,
        _ => Main,
    };

    /// <summary>Item equipado no slot <paramref name="place"/> (null se vazio ou inválido).</summary>
    public InventoryItem? EquippedAt(ItemPlace place) =>
        EquipmentSlots.IsEquipmentPlace(place) ? Equipment[(int)place] : null;

    /// <summary>Soma dos bônus de todos os equipamentos atualmente equipados pelo Tamer.</summary>
    public EquipmentBonus TotalEquipmentBonus()
    {
        var total = EquipmentBonus.Empty;
        foreach (var place in EquipmentSlots.All)
        {
            if (Equipment[(int)place]?.Info is { } info && !info.EquipmentBonus.IsEmpty)
                total += info.EquipmentBonus;
        }
        return total;
    }

    internal uint MaxStackOf(uint itemId, bool isCard = false)
    {
        uint tableMax = isCard
            ? _cardLookup(itemId)?.MaxStack ?? _lookup(itemId)?.MaxStack ?? _maxStackCap
            : _lookup(itemId)?.MaxStack ?? _maxStackCap;
        return Math.Min(_maxStackCap, tableMax);
    }

    public ItemInfo? FindInfo(uint itemId) => _lookup(itemId);

    /// <summary>Item ou card novo com uid novo (ou o salvo, na carga).</summary>
    public InventoryItem Create(uint itemId, int slot, uint amount, uint uid = 0, bool isCard = false, uint expiresAt = 0)
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
            ExpiresAt = expiresAt,
        };
    }

    internal void SeenUid(uint uid) => _nextUid = Math.Max(_nextUid, uid + 1);

    // atalhos do inventário principal
    public InventoryItem? Find(uint itemId) => Main.Find(itemId);
    public bool HasFreeSlot => Main.HasFreeSlot;
    public InventoryItem? Add(uint itemId, uint amount, long? durationSeconds = null) =>
        Main.Add(itemId, amount, durationSeconds) is [.., var last] ? last : null;
    public bool Remove(uint itemId, uint amount) => Main.Remove(itemId, amount).Count > 0;
}
