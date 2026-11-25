using Dapper;
using DRPGServer.Data.GameData;

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

public enum CashPurchaseResult : byte
{
    Ok = 0,
    InsufficientCash = 1,
    DepositoryFull = 2,
    AccountNotFound = 3,
}

public sealed record CashPurchaseOutcome(CashPurchaseResult Result, CashBalance Balance);

/// <summary>Persistência de saldo de Cash e Cash Depository por conta.</summary>
public interface ICashStore
{
    Task<CashBalance?> GetBalanceAsync(uint accountUid, CancellationToken ct);
    Task<CashBalance?> AddCashAsync(uint accountUid, uint cashDelta, uint bonusCashDelta, CancellationToken ct);
    Task<IReadOnlyList<CashDepositoryRow>> ListDepositoryAsync(uint accountUid, CancellationToken ct);
    Task<CashPurchaseOutcome> PurchaseAsync(
        uint accountUid,
        uint totalPrice,
        IReadOnlyList<(CashShopItemKind Kind, uint ItemId, uint Amount, uint ExpiresAt)> entries,
        CancellationToken ct);
    Task<CashDepositoryRow?> TakeDepositoryItemAsync(uint accountUid, CashShopItemKind kind, uint uid, CancellationToken ct);
    Task RestoreDepositoryItemAsync(uint accountUid, CashDepositoryRow row, CancellationToken ct);
}

public sealed class CashRepository(Database database) : ICashStore
{
    public const int DepositoryCapacityPerKind = 24;

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
    }

    public async Task<CashBalance?> GetBalanceAsync(uint accountUid, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var row = await db.QuerySingleOrDefaultAsync<BalanceRow>(new CommandDefinition(
            "SELECT cash, bonus_cash FROM account WHERE uid = @accountUid",
            new { accountUid }, cancellationToken: ct));
        return row is null ? null : new CashBalance(row.Cash, row.BonusCash);
    }

    public async Task<CashBalance?> AddCashAsync(uint accountUid, uint cashDelta, uint bonusCashDelta, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var row = await db.QuerySingleOrDefaultAsync<BalanceRow>(new CommandDefinition(
            "SELECT cash, bonus_cash FROM account WHERE uid = @accountUid FOR UPDATE",
            new { accountUid }, tx, cancellationToken: ct));
        if (row is null)
        {
            await tx.RollbackAsync(CancellationToken.None);
            return null;
        }

        uint newCash = (uint)Math.Min((ulong)row.Cash + cashDelta, uint.MaxValue);
        uint newBonus = (uint)Math.Min((ulong)row.BonusCash + bonusCashDelta, uint.MaxValue);
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE account SET cash = @newCash, bonus_cash = @newBonus WHERE uid = @accountUid",
            new { accountUid, newCash, newBonus }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return new CashBalance(newCash, newBonus);
    }

    public async Task<IReadOnlyList<CashDepositoryRow>> ListDepositoryAsync(uint accountUid, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var rows = await db.QueryAsync<DepositoryDbRow>(new CommandDefinition("""
            SELECT kind, slot, uid, item_id, amount, expires_at
            FROM cash_depository_item
            WHERE account_uid = @accountUid
            ORDER BY kind, slot
            """, new { accountUid }, cancellationToken: ct));
        return rows
            .Where(r => r.Kind is (byte)CashShopItemKind.Card or (byte)CashShopItemKind.Item && r.Slot < DepositoryCapacityPerKind)
            .Select(r => new CashDepositoryRow((CashShopItemKind)r.Kind, r.Slot, r.Uid, r.ItemId, r.Amount, r.ExpiresAt))
            .ToList();
    }

    public async Task<CashPurchaseOutcome> PurchaseAsync(
        uint accountUid,
        uint totalPrice,
        IReadOnlyList<(CashShopItemKind Kind, uint ItemId, uint Amount, uint ExpiresAt)> entries,
        CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        var bal = await db.QuerySingleOrDefaultAsync<BalanceRow>(new CommandDefinition(
            "SELECT cash, bonus_cash FROM account WHERE uid = @accountUid FOR UPDATE",
            new { accountUid }, tx, cancellationToken: ct));
        if (bal is null)
        {
            await tx.RollbackAsync(CancellationToken.None);
            return new CashPurchaseOutcome(CashPurchaseResult.AccountNotFound, default);
        }

        var current = new CashBalance(bal.Cash, bal.BonusCash);
        if (current.Total < totalPrice)
        {
            await tx.RollbackAsync(CancellationToken.None);
            return new CashPurchaseOutcome(CashPurchaseResult.InsufficientCash, current);
        }

        var existing = (await db.QueryAsync<DepositoryDbRow>(new CommandDefinition("""
            SELECT kind, slot, uid, item_id, amount, expires_at
            FROM cash_depository_item
            WHERE account_uid = @accountUid
            FOR UPDATE
            """, new { accountUid }, tx, cancellationToken: ct))).ToList();

        var usedCardSlots = existing.Where(r => r.Kind == (byte)CashShopItemKind.Card).Select(r => (int)r.Slot).ToHashSet();
        var usedItemSlots = existing.Where(r => r.Kind == (byte)CashShopItemKind.Item).Select(r => (int)r.Slot).ToHashSet();
        uint nextUid = existing.Count == 0 ? 1u : existing.Max(r => r.Uid) + 1u;

        var toInsert = new List<CashDepositoryRow>(entries.Count);
        foreach (var (kind, itemId, amount, expiresAt) in entries)
        {
            var used = kind == CashShopItemKind.Card ? usedCardSlots : usedItemSlots;
            int freeSlot = FindFreeSlot(used);
            if (freeSlot < 0)
            {
                await tx.RollbackAsync(CancellationToken.None);
                return new CashPurchaseOutcome(CashPurchaseResult.DepositoryFull, current);
            }
            used.Add(freeSlot);
            toInsert.Add(new CashDepositoryRow(kind, (byte)freeSlot, nextUid++, itemId, amount, expiresAt));
        }

        // Debita primeiro do Cash principal e o restante do Bonus Cash
        uint takeFromCash = Math.Min(bal.Cash, totalPrice);
        uint takeFromBonus = totalPrice - takeFromCash;
        uint newCash = bal.Cash - takeFromCash;
        uint newBonus = bal.BonusCash - takeFromBonus;

        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE account SET cash = @newCash, bonus_cash = @newBonus WHERE uid = @accountUid",
            new { accountUid, newCash, newBonus }, tx, cancellationToken: ct));

        await db.ExecuteAsync(new CommandDefinition("""
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
        }), tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new CashPurchaseOutcome(CashPurchaseResult.Ok, new CashBalance(newCash, newBonus));
    }

    public async Task<CashDepositoryRow?> TakeDepositoryItemAsync(uint accountUid, CashShopItemKind kind, uint uid, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        var row = await db.QuerySingleOrDefaultAsync<DepositoryDbRow>(new CommandDefinition("""
            SELECT kind, slot, uid, item_id, amount, expires_at
            FROM cash_depository_item
            WHERE account_uid = @accountUid AND kind = @kind AND uid = @uid
            LIMIT 1
            FOR UPDATE
            """, new { accountUid, kind = (byte)kind, uid }, tx, cancellationToken: ct));

        if (row is null)
        {
            await tx.RollbackAsync(CancellationToken.None);
            return null;
        }

        await db.ExecuteAsync(new CommandDefinition("""
            DELETE FROM cash_depository_item
            WHERE account_uid = @accountUid AND kind = @kind AND slot = @slot
            """, new { accountUid, kind = row.Kind, slot = row.Slot }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new CashDepositoryRow((CashShopItemKind)row.Kind, row.Slot, row.Uid, row.ItemId, row.Amount, row.ExpiresAt);
    }

    public async Task RestoreDepositoryItemAsync(uint accountUid, CashDepositoryRow row, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO cash_depository_item (account_uid, kind, slot, uid, item_id, amount, expires_at)
            VALUES (@AccountUid, @Kind, @Slot, @Uid, @ItemId, @Amount, @ExpiresAt)
            """, new
        {
            AccountUid = accountUid,
            Kind = (byte)row.Kind,
            row.Slot,
            row.Uid,
            row.ItemId,
            row.Amount,
            row.ExpiresAt,
        }, cancellationToken: ct));
    }

    private static int FindFreeSlot(HashSet<int> used)
    {
        for (int i = 0; i < DepositoryCapacityPerKind; i++)
            if (!used.Contains(i))
                return i;
        return -1;
    }
}
