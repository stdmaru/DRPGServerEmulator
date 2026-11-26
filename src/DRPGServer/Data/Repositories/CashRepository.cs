using Dapper;
using DRPGServer.Data.GameData;
using DRPGServer.Game.Economy;
using DRPGServer.Game.Shop;

namespace DRPGServer.Data.Repositories;

/// <summary>Saldo de Cash de uma conta (o cliente soma <see cref="Cash"/> + <see cref="BonusCash"/>).</summary>
public readonly record struct CashBalance(uint Cash, uint BonusCash)
{
    public long Total => (long)Cash + BonusCash;
}

/// <summary>Uma entrada persistida na tabela <c>cash_depository_item</c> (24 slots de Cards + 24 slots de Items por conta).</summary>
public sealed record CashDepositoryRow(
    CashShopItemKind Kind,
    byte Slot,
    uint Uid,
    uint ItemId,
    uint Amount,
    uint ExpiresAt = 0);

/// <summary>Algo a guardar no depósito da conta (Cash/Shop Depository), antes de ganhar posição e uid.</summary>
public readonly record struct AccountStorageEntry(CashShopItemKind Kind, uint ItemId, uint Amount, uint ExpiresAt = 0);

public enum CashPurchaseResult : byte
{
    Ok = 0,
    InsufficientCash = 1,
    DepositoryFull = 2,
    AccountNotFound = 3,
}

public sealed record CashPurchaseOutcome(CashPurchaseResult Result, CashBalance Balance);

/// <summary>Registro de 48 bytes do histórico de compras (<c>S2C 0x0161</c>; <c>DigimonRPG.bin @ 0x0097AC93..0x0097B03F</c>).</summary>
public sealed record CashShopHistoryEntry(
    long OrderId,
    uint ItemDataId,
    uint Quantity,
    uint UnitPrice,
    uint CashAfter,
    uint BonusCashAfter,
    long PurchasedAt);

public sealed record CashShopHistoryPage(IReadOnlyList<CashShopHistoryEntry> Entries, ushort TotalPages);

/// <summary>Persistência de saldo de Cash, Cash Depository e histórico de compras por conta.</summary>
public interface ICashStore
{
    Task<CashBalance?> GetBalanceAsync(uint accountUid, CancellationToken ct);
    /// <summary>Credita Cash; a auditoria (<paramref name="reason"/>) entra na mesma transação.</summary>
    Task<CashBalance?> AddCashAsync(uint accountUid, uint cashDelta, uint bonusCashDelta, EconomyReason reason, CancellationToken ct);
    Task<IReadOnlyList<CashDepositoryRow>> ListDepositoryAsync(uint accountUid, CancellationToken ct);
    Task<CashDepositoryRow?> FindDepositoryItemAsync(uint accountUid, CashShopItemKind kind, uint uid, CancellationToken ct);
    /// <summary>Debita o Cash e guarda <paramref name="entries"/> no depósito; a auditoria entra na mesma transação.</summary>
    Task<CashPurchaseOutcome> PurchaseAsync(uint accountUid, uint itemDataId, uint quantity, uint unitPrice, uint totalPrice,
        IReadOnlyList<AccountStorageEntry> entries, EconomyReason reason, CancellationToken ct);
    Task<CashShopHistoryPage> GetHistoryAsync(uint accountUid, ushort year, ushort month, ushort page, CancellationToken ct);
}

/// <summary>
/// Depósito da conta (<c>cash_depository_item</c>). As operações que mudam o depósito rodam dentro de uma
/// <see cref="UnitOfWork"/> e travam a linha da conta primeiro, para serializar tudo o que mexe no depósito dela.
/// </summary>
public sealed class CashRepository(Database database, Game.World.IGameClock clock, IAuditLog audit) : ICashStore
{
    private const string SelectDepository = """
        SELECT kind, slot, uid, item_id, amount, expires_at
        FROM cash_depository_item
        """;

    private sealed class BalanceRow
    {
        public uint Cash { get; set; }
        public uint BonusCash { get; set; }
    }

    private sealed class DepositoryDbRow
    {
        public byte Kind { get; set; }
        public byte Slot { get; set; }
        public uint Uid { get; set; }
        public uint ItemId { get; set; }
        public uint Amount { get; set; }
        public uint ExpiresAt { get; set; }

        public CashDepositoryRow ToRow() => new((CashShopItemKind)Kind, Slot, Uid, ItemId, Amount, ExpiresAt);
    }

    private sealed class HistoryDbRow
    {
        public ulong Id { get; set; }
        public uint ItemDataId { get; set; }
        public uint Quantity { get; set; }
        public uint UnitPrice { get; set; }
        public uint CashAfter { get; set; }
        public uint BonusCashAfter { get; set; }
        public uint PurchasedAt { get; set; }
    }

    public async Task<CashBalance?> GetBalanceAsync(uint accountUid, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var row = await db.QuerySingleOrDefaultAsync<BalanceRow>(new CommandDefinition(
            "SELECT cash, bonus_cash FROM account WHERE uid = @accountUid",
            new { accountUid }, cancellationToken: ct));
        return row is null ? null : new CashBalance(row.Cash, row.BonusCash);
    }

    public async Task<CashBalance?> AddCashAsync(uint accountUid, uint cashDelta, uint bonusCashDelta, EconomyReason reason,
        CancellationToken ct)
    {
        await using var uow = await database.BeginAsync(ct);
        if (await LockBalanceAsync(uow, accountUid, ct) is not { } row) return null;

        uint newCash = (uint)Math.Min((ulong)row.Cash + cashDelta, uint.MaxValue);
        uint newBonus = (uint)Math.Min((ulong)row.BonusCash + bonusCashDelta, uint.MaxValue);
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE account SET cash = @newCash, bonus_cash = @newBonus WHERE uid = @accountUid",
            new { accountUid, newCash, newBonus }, uow.Transaction, cancellationToken: ct));
        var at = clock.UtcNow;
        await audit.WriteAsync(uow, [
            EconomyEntry.Create(at, accountUid, null, reason, EconomyAsset.Cash, 0, (long)newCash - row.Cash, newCash),
            EconomyEntry.Create(at, accountUid, null, reason, EconomyAsset.BonusCash, 0, (long)newBonus - row.BonusCash, newBonus),
        ], ct);
        await uow.CommitAsync(ct);
        return new CashBalance(newCash, newBonus);
    }

    public async Task<IReadOnlyList<CashDepositoryRow>> ListDepositoryAsync(uint accountUid, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var rows = await db.QueryAsync<DepositoryDbRow>(new CommandDefinition(
            SelectDepository + " WHERE account_uid = @accountUid ORDER BY kind, slot",
            new { accountUid }, cancellationToken: ct));
        return rows
            .Where(r => r.Kind is (byte)CashShopItemKind.Card or (byte)CashShopItemKind.Item && r.Slot < CashDepository.CapacityPerKind)
            .Select(r => r.ToRow())
            .ToList();
    }

    public async Task<CashDepositoryRow?> FindDepositoryItemAsync(uint accountUid, CashShopItemKind kind, uint uid, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var row = await db.QuerySingleOrDefaultAsync<DepositoryDbRow>(new CommandDefinition(
            SelectDepository + " WHERE account_uid = @accountUid AND kind = @kind AND uid = @uid LIMIT 1",
            new { accountUid, kind = (byte)kind, uid }, cancellationToken: ct));
        return row?.ToRow();
    }

    public async Task<CashPurchaseOutcome> PurchaseAsync(uint accountUid, uint itemDataId, uint quantity, uint unitPrice,
        uint totalPrice, IReadOnlyList<AccountStorageEntry> entries, EconomyReason reason, CancellationToken ct)
    {
        await using var uow = await database.BeginAsync(ct);
        if (await LockBalanceAsync(uow, accountUid, ct) is not { } bal)
            return new CashPurchaseOutcome(CashPurchaseResult.AccountNotFound, default);

        var current = new CashBalance(bal.Cash, bal.BonusCash);
        if (current.Total < totalPrice)
            return new CashPurchaseOutcome(CashPurchaseResult.InsufficientCash, current);
        if (!await InsertDepositoryAsync(uow, accountUid, entries, ct))
            return new CashPurchaseOutcome(CashPurchaseResult.DepositoryFull, current);

        // Debita primeiro do Bonus Cash e o restante do Cash principal
        uint takeFromBonus = Math.Min(bal.BonusCash, totalPrice);
        uint takeFromCash = totalPrice - takeFromBonus;
        uint newCash = bal.Cash - takeFromCash;
        uint newBonus = bal.BonusCash - takeFromBonus;

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE account SET cash = @newCash, bonus_cash = @newBonus WHERE uid = @accountUid",
            new { accountUid, newCash, newBonus }, uow.Transaction, cancellationToken: ct));

        await uow.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO cash_shop_history (account_uid, item_data_id, quantity, unit_price, cash_after, bonus_cash_after, purchased_at)
            VALUES (@AccountUid, @ItemDataId, @Quantity, @UnitPrice, @CashAfter, @BonusCashAfter, @PurchasedAt)
            """, new
        {
            AccountUid = accountUid,
            ItemDataId = itemDataId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            CashAfter = newCash,
            BonusCashAfter = newBonus,
            PurchasedAt = (uint)Math.Clamp(clock.UnixNow, 0, uint.MaxValue),
        }, uow.Transaction, cancellationToken: ct));

        var at = clock.UtcNow;
        var economy = new List<EconomyEntry>
        {
            EconomyEntry.Create(at, accountUid, null, reason, EconomyAsset.Cash, 0, -(long)takeFromCash, newCash),
            EconomyEntry.Create(at, accountUid, null, reason, EconomyAsset.BonusCash, 0, -(long)takeFromBonus, newBonus),
        };
        economy.AddRange(entries.GroupBy(e => (e.Kind, e.ItemId)).Select(g => EconomyEntry.Create(at, accountUid, null, reason,
            g.Key.Kind == CashShopItemKind.Card ? EconomyAsset.Card : EconomyAsset.Item, g.Key.ItemId, g.Sum(e => (long)e.Amount), null)));
        await audit.WriteAsync(uow, economy.Where(e => e.Amount != 0).ToList(), ct);

        await uow.CommitAsync(ct);
        return new CashPurchaseOutcome(CashPurchaseResult.Ok, new CashBalance(newCash, newBonus));
    }

    /// <summary>
    /// Guarda <paramref name="entries"/> no depósito da conta, dentro de <paramref name="uow"/>. Falso se não há
    /// posições livres para todas (nada é gravado; quem chamou descarta a transação).
    /// </summary>
    public async Task<bool> DepositAsync(UnitOfWork uow, uint accountUid, IReadOnlyList<AccountStorageEntry> entries,
        CancellationToken ct)
    {
        if (await LockBalanceAsync(uow, accountUid, ct) is null) return false;
        return await InsertDepositoryAsync(uow, accountUid, entries, ct);
    }

    /// <summary>
    /// Tira do depósito da conta exatamente a linha <paramref name="expected"/>, dentro de <paramref name="uow"/>. Falso
    /// se ela não está mais lá, ou mudou (já foi retirada por outra conexão).
    /// </summary>
    public async Task<bool> TakeAsync(UnitOfWork uow, uint accountUid, CashDepositoryRow expected, CancellationToken ct)
    {
        if (await LockBalanceAsync(uow, accountUid, ct) is null) return false;
        var row = await uow.Connection.QuerySingleOrDefaultAsync<DepositoryDbRow>(new CommandDefinition(
            SelectDepository + " WHERE account_uid = @accountUid AND kind = @kind AND slot = @slot FOR UPDATE",
            new { accountUid, kind = (byte)expected.Kind, slot = expected.Slot }, uow.Transaction, cancellationToken: ct));
        if (row is null || row.ToRow() != expected) return false;

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM cash_depository_item WHERE account_uid = @accountUid AND kind = @kind AND slot = @slot",
            new { accountUid, kind = row.Kind, slot = row.Slot }, uow.Transaction, cancellationToken: ct));
        return true;
    }

    /// <summary>Trava a linha da conta (serializa saldo e depósito da conta). Null se a conta não existe.</summary>
    private static Task<BalanceRow?> LockBalanceAsync(UnitOfWork uow, uint accountUid, CancellationToken ct) =>
        uow.Connection.QuerySingleOrDefaultAsync<BalanceRow>(new CommandDefinition(
            "SELECT cash, bonus_cash FROM account WHERE uid = @accountUid FOR UPDATE",
            new { accountUid }, uow.Transaction, cancellationToken: ct));

    /// <summary>Dá posição e uid a cada entrada e grava. Falso (sem gravar) se não couber tudo.</summary>
    private static async Task<bool> InsertDepositoryAsync(UnitOfWork uow, uint accountUid,
        IReadOnlyList<AccountStorageEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return true;
        var existing = (await uow.Connection.QueryAsync<DepositoryDbRow>(new CommandDefinition(
            SelectDepository + " WHERE account_uid = @accountUid FOR UPDATE",
            new { accountUid }, uow.Transaction, cancellationToken: ct))).ToList();

        var used = new Dictionary<CashShopItemKind, HashSet<int>>
        {
            [CashShopItemKind.Card] = existing.Where(r => r.Kind == (byte)CashShopItemKind.Card).Select(r => (int)r.Slot).ToHashSet(),
            [CashShopItemKind.Item] = existing.Where(r => r.Kind == (byte)CashShopItemKind.Item).Select(r => (int)r.Slot).ToHashSet(),
        };
        uint nextUid = existing.Count == 0 ? 1u : existing.Max(r => r.Uid) + 1u;

        var toInsert = new List<CashDepositoryRow>(entries.Count);
        foreach (var entry in entries)
        {
            if (!used.TryGetValue(entry.Kind, out var slots)) return false;
            int freeSlot = FindFreeSlot(slots);
            if (freeSlot < 0) return false;
            slots.Add(freeSlot);
            toInsert.Add(new CashDepositoryRow(entry.Kind, (byte)freeSlot, nextUid++, entry.ItemId, entry.Amount, entry.ExpiresAt));
        }

        await uow.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO cash_depository_item (account_uid, kind, slot, uid, item_id, amount, expires_at)
            VALUES (@AccountUid, @Kind, @Slot, @Uid, @ItemId, @Amount, @ExpiresAt)
            """, toInsert.Select(r => new
        {
            AccountUid = accountUid,
            Kind = (byte)r.Kind,
            r.Slot,
            r.Uid,
            r.ItemId,
            r.Amount,
            r.ExpiresAt,
        }), uow.Transaction, cancellationToken: ct));
        return true;
    }

    public async Task<CashShopHistoryPage> GetHistoryAsync(uint accountUid, ushort year, ushort month, ushort page, CancellationToken ct)
    {
        if (year < 2000 || month is < 1 or > 12)
            return new CashShopHistoryPage([], 1);

        long startTs = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        long endTs = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1).ToUnixTimeSeconds();

        await using var db = await database.OpenAsync(ct);
        int totalCount = await db.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*)
            FROM cash_shop_history
            WHERE account_uid = @accountUid AND purchased_at >= @startTs AND purchased_at < @endTs
            """, new { accountUid, startTs, endTs }, cancellationToken: ct));

        ushort totalPages = (ushort)Math.Max(1, (totalCount + CashDepository.HistoryPageSize - 1) / CashDepository.HistoryPageSize);
        int clampedPage = Math.Clamp((int)page, 1, totalPages);
        int offset = (clampedPage - 1) * CashDepository.HistoryPageSize;

        var rows = await db.QueryAsync<HistoryDbRow>(new CommandDefinition("""
            SELECT id, item_data_id, quantity, unit_price, cash_after, bonus_cash_after, purchased_at
            FROM cash_shop_history
            WHERE account_uid = @accountUid AND purchased_at >= @startTs AND purchased_at < @endTs
            ORDER BY id DESC
            LIMIT @limit OFFSET @offset
            """, new { accountUid, startTs, endTs, limit = CashDepository.HistoryPageSize, offset }, cancellationToken: ct));

        return new CashShopHistoryPage(
            rows.Select(r => new CashShopHistoryEntry(
                (long)r.Id, r.ItemDataId, r.Quantity, r.UnitPrice, r.CashAfter, r.BonusCashAfter, r.PurchasedAt)).ToList(),
            totalPages);
    }

    private static int FindFreeSlot(HashSet<int> used)
    {
        for (int i = 0; i < CashDepository.CapacityPerKind; i++)
            if (!used.Contains(i))
                return i;
        return -1;
    }
}
