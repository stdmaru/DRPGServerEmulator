using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Economy;

namespace DRPGServer.Tests;

/// <summary>
/// Cash Store em memória com as mesmas regras do <see cref="CashRepository"/> (saldo, depósito, histórico) e as
/// transferências do <see cref="AccountTransferRepository"/>: depósito + snapshot mudam juntos ou nada muda.
/// </summary>
internal sealed class InMemoryCashStore : ICashStore, IAccountTransferStore
{
    private readonly Dictionary<uint, CashBalance> _balances = [];
    private readonly Dictionary<uint, List<CashDepositoryRow>> _depo = [];
    private readonly Dictionary<uint, List<CashShopHistoryEntry>> _history = [];
    private readonly Lock _lock = new();
    private uint _nextUid = 1000;
    private long _nextOrderId = 1;

    /// <summary>Snapshots gravados pelas transferências que fizeram commit.</summary>
    public List<CharacterSnapshot> Snapshots { get; } = [];

    /// <summary>Se definido, toda transferência espera esta tarefa antes de gravar (banco lento).</summary>
    public Task? HoldTransfers { get; set; }

    /// <summary>Transferências lançam (erro de banco) em vez de gravar.</summary>
    public bool FailTransfers { get; set; }

    public void Seed(uint accountUid, uint cash, uint bonusCash = 0)
    {
        lock (_lock)
        {
            _balances[accountUid] = new CashBalance(cash, bonusCash);
            _depo.TryAdd(accountUid, []);
            _history.TryAdd(accountUid, []);
        }
    }

    /// <summary>Ocupa todas as posições de <paramref name="kind"/> no depósito da conta.</summary>
    public void Fill(uint accountUid, CashShopItemKind kind)
    {
        lock (_lock)
            while (TryInsert(accountUid, [new AccountStorageEntry(kind, 1, 1)])) { }
    }

    public Task<CashBalance?> GetBalanceAsync(uint accountUid, CancellationToken ct)
    {
        lock (_lock)
            return Task.FromResult(_balances.TryGetValue(accountUid, out var b) ? (CashBalance?)b : null);
    }

    /// <summary>Motivos recebidos nas operações só da conta (o SQL grava a auditoria na mesma transação).</summary>
    public List<EconomyReason> Reasons { get; } = [];

    public Task<CashBalance?> AddCashAsync(uint accountUid, uint cashDelta, uint bonusCashDelta, EconomyReason reason, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_balances.TryGetValue(accountUid, out var b)) return Task.FromResult<CashBalance?>(null);
            var next = new CashBalance(b.Cash + cashDelta, b.BonusCash + bonusCashDelta);
            _balances[accountUid] = next;
            Reasons.Add(reason);
            return Task.FromResult<CashBalance?>(next);
        }
    }

    public Task<IReadOnlyList<CashDepositoryRow>> ListDepositoryAsync(uint accountUid, CancellationToken ct)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<CashDepositoryRow>>(_depo.TryGetValue(accountUid, out var list) ? list.ToList() : []);
    }

    public Task<CashDepositoryRow?> FindDepositoryItemAsync(uint accountUid, CashShopItemKind kind, uint uid, CancellationToken ct)
    {
        lock (_lock)
            return Task.FromResult(_depo.TryGetValue(accountUid, out var list) ? list.Find(r => r.Kind == kind && r.Uid == uid) : null);
    }

    public Task<CashPurchaseOutcome> PurchaseAsync(uint accountUid, uint itemDataId, uint quantity, uint unitPrice,
        uint totalPrice, IReadOnlyList<AccountStorageEntry> entries, EconomyReason reason, CancellationToken ct)
    {
        lock (_lock)
        {
            Reasons.Add(reason);
            if (!_balances.TryGetValue(accountUid, out var bal))
                return Task.FromResult(new CashPurchaseOutcome(CashPurchaseResult.AccountNotFound, default));
            if (bal.Total < totalPrice)
                return Task.FromResult(new CashPurchaseOutcome(CashPurchaseResult.InsufficientCash, bal));
            if (!TryInsert(accountUid, entries))
                return Task.FromResult(new CashPurchaseOutcome(CashPurchaseResult.DepositoryFull, bal));

            uint fromBonus = Math.Min(bal.BonusCash, totalPrice);
            uint fromCash = totalPrice - fromBonus;
            var after = new CashBalance(bal.Cash - fromCash, bal.BonusCash - fromBonus);
            _balances[accountUid] = after;
            _history[accountUid].Add(new CashShopHistoryEntry(
                _nextOrderId++, itemDataId, quantity, unitPrice, after.Cash, after.BonusCash,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            return Task.FromResult(new CashPurchaseOutcome(CashPurchaseResult.Ok, after));
        }
    }

    public Task<CashShopHistoryPage> GetHistoryAsync(uint accountUid, ushort year, ushort month, ushort page, CancellationToken ct)
    {
        lock (_lock)
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
    }

    public async Task<bool> DepositAsync(uint accountUid, IReadOnlyList<AccountStorageEntry> entries, CharacterSnapshot snapshot,
        CancellationToken ct)
    {
        await BeforeTransferAsync();
        lock (_lock)
        {
            if (!TryInsert(accountUid, entries)) return false;
            Snapshots.Add(snapshot);
            return true;
        }
    }

    public async Task<bool> TakeAsync(uint accountUid, CashDepositoryRow row, CharacterSnapshot snapshot, CancellationToken ct)
    {
        await BeforeTransferAsync();
        lock (_lock)
        {
            if (!_depo.TryGetValue(accountUid, out var list) || !list.Remove(row)) return false;
            Snapshots.Add(snapshot);
            return true;
        }
    }

    private async Task BeforeTransferAsync()
    {
        if (HoldTransfers is { } hold) await hold;
        if (FailTransfers) throw new InvalidOperationException("teste: banco indisponível");
    }

    /// <summary>Mesma alocação de posição do <see cref="CashRepository"/>. Tudo ou nada.</summary>
    private bool TryInsert(uint accountUid, IReadOnlyList<AccountStorageEntry> entries)
    {
        _depo.TryAdd(accountUid, []);
        var list = _depo[accountUid];
        var used = new Dictionary<CashShopItemKind, HashSet<int>>
        {
            [CashShopItemKind.Card] = list.Where(r => r.Kind == CashShopItemKind.Card).Select(r => (int)r.Slot).ToHashSet(),
            [CashShopItemKind.Item] = list.Where(r => r.Kind == CashShopItemKind.Item).Select(r => (int)r.Slot).ToHashSet(),
        };
        var toAdd = new List<CashDepositoryRow>();
        foreach (var entry in entries)
        {
            var slots = used[entry.Kind];
            int slot = Enumerable.Range(0, CashRepository.DepositoryCapacityPerKind).FirstOrDefault(i => !slots.Contains(i), -1);
            if (slot < 0) return false;
            slots.Add(slot);
            toAdd.Add(new CashDepositoryRow(entry.Kind, (byte)slot, _nextUid++, entry.ItemId, entry.Amount, entry.ExpiresAt));
        }
        list.AddRange(toAdd);
        return true;
    }
}
