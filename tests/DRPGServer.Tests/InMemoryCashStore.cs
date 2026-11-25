using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;

namespace DRPGServer.Tests;

/// <summary>Cash Store em memória com as mesmas regras do <see cref="CashRepository"/> (saldo, depósito, histórico).</summary>
internal sealed class InMemoryCashStore : ICashStore
{
    private readonly Dictionary<uint, CashBalance> _balances = [];
    private readonly Dictionary<uint, List<CashDepositoryRow>> _depo = [];
    private readonly Dictionary<uint, List<CashShopHistoryEntry>> _history = [];
    private uint _nextUid = 1000;
    private long _nextOrderId = 1;

    public void Seed(uint accountUid, uint cash, uint bonusCash = 0)
    {
        _balances[accountUid] = new CashBalance(cash, bonusCash);
        _depo.TryAdd(accountUid, []);
        _history.TryAdd(accountUid, []);
    }

    public Task<CashBalance?> GetBalanceAsync(uint accountUid, CancellationToken ct) =>
        Task.FromResult(_balances.TryGetValue(accountUid, out var b) ? (CashBalance?)b : null);

    public Task<CashBalance?> AddCashAsync(uint accountUid, uint cashDelta, uint bonusCashDelta, CancellationToken ct)
    {
        if (!_balances.TryGetValue(accountUid, out var b)) return Task.FromResult<CashBalance?>(null);
        var next = new CashBalance(b.Cash + cashDelta, b.BonusCash + bonusCashDelta);
        _balances[accountUid] = next;
        return Task.FromResult<CashBalance?>(next);
    }

    public Task<IReadOnlyList<CashDepositoryRow>> ListDepositoryAsync(uint accountUid, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CashDepositoryRow>>(_depo.TryGetValue(accountUid, out var list) ? list.ToList() : []);

    public Task<CashPurchaseOutcome> PurchaseAsync(
        uint accountUid,
        uint itemDataId,
        uint quantity,
        uint unitPrice,
        uint totalPrice,
        IReadOnlyList<(CashShopItemKind Kind, uint ItemId, uint Amount, uint ExpiresAt)> entries,
        CancellationToken ct)
    {
        if (!_balances.TryGetValue(accountUid, out var bal))
            return Task.FromResult(new CashPurchaseOutcome(CashPurchaseResult.AccountNotFound, default));
        if (bal.Total < totalPrice)
            return Task.FromResult(new CashPurchaseOutcome(CashPurchaseResult.InsufficientCash, bal));

        var list = _depo[accountUid];
        var usedCards = list.Where(r => r.Kind == CashShopItemKind.Card).Select(r => (int)r.Slot).ToHashSet();
        var usedItems = list.Where(r => r.Kind == CashShopItemKind.Item).Select(r => (int)r.Slot).ToHashSet();
        var toAdd = new List<CashDepositoryRow>();

        foreach (var (kind, itemId, amount, expiresAt) in entries)
        {
            var used = kind == CashShopItemKind.Card ? usedCards : usedItems;
            int slot = Enumerable.Range(0, CashRepository.DepositoryCapacityPerKind).FirstOrDefault(i => !used.Contains(i), -1);
            if (slot < 0)
                return Task.FromResult(new CashPurchaseOutcome(CashPurchaseResult.DepositoryFull, bal));
            used.Add(slot);
            toAdd.Add(new CashDepositoryRow(kind, (byte)slot, _nextUid++, itemId, amount, expiresAt));
        }

        uint fromBonus = Math.Min(bal.BonusCash, totalPrice);
        uint fromCash = totalPrice - fromBonus;
        var after = new CashBalance(bal.Cash - fromCash, bal.BonusCash - fromBonus);
        _balances[accountUid] = after;
        list.AddRange(toAdd);
        _history[accountUid].Add(new CashShopHistoryEntry(
            _nextOrderId++, itemDataId, quantity, unitPrice, after.Cash, after.BonusCash,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        return Task.FromResult(new CashPurchaseOutcome(CashPurchaseResult.Ok, after));
    }

    public Task<bool> DepositAsync(
        uint accountUid,
        IReadOnlyList<(CashShopItemKind Kind, uint ItemId, uint Amount, uint ExpiresAt)> entries,
        CancellationToken ct)
    {
        _depo.TryAdd(accountUid, []);
        var list = _depo[accountUid];
        var usedCards = list.Where(r => r.Kind == CashShopItemKind.Card).Select(r => (int)r.Slot).ToHashSet();
        var usedItems = list.Where(r => r.Kind == CashShopItemKind.Item).Select(r => (int)r.Slot).ToHashSet();
        var toAdd = new List<CashDepositoryRow>();
        foreach (var (kind, itemId, amount, expiresAt) in entries)
        {
            var used = kind == CashShopItemKind.Card ? usedCards : usedItems;
            int slot = Enumerable.Range(0, CashRepository.DepositoryCapacityPerKind).FirstOrDefault(i => !used.Contains(i), -1);
            if (slot < 0) return Task.FromResult(false);
            used.Add(slot);
            toAdd.Add(new CashDepositoryRow(kind, (byte)slot, _nextUid++, itemId, amount, expiresAt));
        }
        list.AddRange(toAdd);
        return Task.FromResult(true);
    }

    public Task<CashShopHistoryPage> GetHistoryAsync(uint accountUid, ushort year, ushort month, ushort page, CancellationToken ct)
    {
        if (!_history.TryGetValue(accountUid, out var all))
            return Task.FromResult(new CashShopHistoryPage([], 1));
        long startTs = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        long endTs = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1).ToUnixTimeSeconds();
        var filtered = all.Where(e => e.PurchasedAt >= startTs && e.PurchasedAt < endTs).OrderByDescending(e => e.OrderId).ToList();
        ushort totalPages = (ushort)Math.Max(1, (filtered.Count + CashRepository.HistoryPageSize - 1) / CashRepository.HistoryPageSize);
        int p = Math.Clamp((int)page, 1, totalPages);
        var slice = filtered.Skip((p - 1) * CashRepository.HistoryPageSize).Take(CashRepository.HistoryPageSize).ToList();
        return Task.FromResult(new CashShopHistoryPage(slice, totalPages));
    }

    /// <summary>Se definido, a retirada tira a linha e espera esta tarefa antes de responder (I/O lento).</summary>
    public Task? HoldTake { get; set; }

    public async Task<CashDepositoryRow?> TakeDepositoryItemAsync(uint accountUid, CashShopItemKind kind, uint uid, CancellationToken ct)
    {
        if (!_depo.TryGetValue(accountUid, out var list)) return null;
        int idx = list.FindIndex(r => r.Kind == kind && r.Uid == uid);
        if (idx < 0) return null;
        var row = list[idx];
        list.RemoveAt(idx);
        if (HoldTake is { } hold) await hold;
        return row;
    }

    public Task RestoreDepositoryItemAsync(uint accountUid, CashDepositoryRow row, CancellationToken ct)
    {
        _depo[accountUid].Add(row);
        return Task.CompletedTask;
    }
}
