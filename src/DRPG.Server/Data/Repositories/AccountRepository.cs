using Dapper;
using DRPG.Server.Game.Entities;

namespace DRPG.Server.Data.Repositories;

/// <param name="AuthKey">Plain-text key, as older issuers write it; <c>null</c> once converted.</param>
/// <param name="AuthKeyHash">Hash of the key (<see cref="Game.Accounts.AuthKeyHash"/>).</param>
/// <param name="AuthKeyExpiresAt">Unix seconds UTC after which the key is refused; 0 = no expiry.</param>
public sealed record AccountRecord(AuthenticatedAccount Account, string? AuthKey, string? AuthKeyHash, long AuthKeyExpiresAt = 0);

/// <summary>Account found by the launcher login (username or e-mail).</summary>
/// <param name="PasswordHash">PBKDF2 hash, <c>plain$&lt;password&gt;</c> before conversion, or <c>null</c> (no password set).</param>
public sealed record LauncherAccountRecord(AuthenticatedAccount Account, string? PasswordHash);

public interface IAccountStore
{
    Task<AccountRecord?> FindByUsernameAsync(string username, CancellationToken ct);

    /// <summary>
    /// Grava o hash e apaga a chave em texto puro, só se ela ainda for <paramref name="plainKey"/> (o emissor pode ter
    /// gravado uma nova nesse meio-tempo). Falso: a chave mudou, nada gravado.
    /// </summary>
    Task<bool> StoreAuthKeyHashAsync(uint accountUid, string plainKey, string hash, CancellationToken ct);

    /// <summary>Contas com chave em texto puro esperando conversão.</summary>
    Task<IReadOnlyList<(uint Uid, string AuthKey)>> PlainAuthKeysAsync(CancellationToken ct);
}

/// <summary>Accounts as seen by the launcher login (docs/protocolo/launcher.md).</summary>
public interface ILauncherAccountStore
{
    /// <summary>Finds by username or, failing that, by e-mail (both case-insensitive, as the column collation).</summary>
    Task<LauncherAccountRecord?> FindByLoginAsync(string login, CancellationToken ct);

    /// <summary>Replaces the password hash only if it is still <paramref name="previous"/>. False: it changed, nothing written.</summary>
    Task<bool> StorePasswordHashAsync(uint accountUid, string previous, string hash, CancellationToken ct);

    /// <summary>Stores a newly issued game key (hash only) and its expiry, dropping any plain-text key.</summary>
    Task IssueAuthKeyAsync(uint accountUid, string keyHash, long expiresAt, CancellationToken ct);

    /// <summary>Accounts whose password is still <c>plain$...</c> (copied by migration 0015).</summary>
    Task<IReadOnlyList<(uint Uid, string PasswordHash)>> PlainPasswordsAsync(CancellationToken ct);
}

public sealed class AccountRepository(Database database) : IAccountStore, ILauncherAccountStore
{
    private sealed class Row
    {
        public uint Uid { get; set; }
        public string Username { get; set; } = "";
        public string? AuthKey { get; set; }
        public string? AuthKeyHash { get; set; }
        public long AuthKeyExpiresAt { get; set; }
        public string? PasswordHash { get; set; }
        public byte? Authority { get; set; }
    }

    private static AuthenticatedAccount ToAccount(Row row) => new(row.Uid, row.Username, (Authority)(row.Authority ?? 0));

    public async Task<AccountRecord?> FindByUsernameAsync(string username, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var row = await db.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            "SELECT uid, username, auth_key, auth_key_hash, auth_key_expires_at, authority FROM account WHERE username = @username",
            new { username }, cancellationToken: ct));
        return row is null ? null : new AccountRecord(ToAccount(row), row.AuthKey, row.AuthKeyHash, row.AuthKeyExpiresAt);
    }

    public async Task<bool> StoreAuthKeyHashAsync(uint accountUid, string plainKey, string hash, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var changed = await db.ExecuteAsync(new CommandDefinition(
            "UPDATE account SET auth_key_hash = @hash, auth_key = NULL WHERE uid = @accountUid AND auth_key = @plainKey",
            new { accountUid, plainKey, hash }, cancellationToken: ct));
        return changed == 1;
    }

    public async Task<IReadOnlyList<(uint Uid, string AuthKey)>> PlainAuthKeysAsync(CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var rows = await db.QueryAsync<Row>(new CommandDefinition(
            "SELECT uid, auth_key FROM account WHERE auth_key IS NOT NULL AND auth_key <> ''", cancellationToken: ct));
        return rows.Select(r => (r.Uid, r.AuthKey!)).ToList();
    }

    public async Task<LauncherAccountRecord?> FindByLoginAsync(string login, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var row = await db.QueryFirstOrDefaultAsync<Row>(new CommandDefinition(
            "SELECT uid, username, password_hash, authority FROM account WHERE username = @login " +
            "UNION ALL SELECT uid, username, password_hash, authority FROM account WHERE email = @login LIMIT 1",
            new { login }, cancellationToken: ct));
        return row is null ? null : new LauncherAccountRecord(ToAccount(row), row.PasswordHash);
    }

    public async Task<bool> StorePasswordHashAsync(uint accountUid, string previous, string hash, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var changed = await db.ExecuteAsync(new CommandDefinition(
            "UPDATE account SET password_hash = @hash WHERE uid = @accountUid AND password_hash = @previous",
            new { accountUid, previous, hash }, cancellationToken: ct));
        return changed == 1;
    }

    public async Task IssueAuthKeyAsync(uint accountUid, string keyHash, long expiresAt, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE account SET auth_key = NULL, auth_key_hash = @keyHash, auth_key_expires_at = @expiresAt WHERE uid = @accountUid",
            new { accountUid, keyHash, expiresAt }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<(uint Uid, string PasswordHash)>> PlainPasswordsAsync(CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var rows = await db.QueryAsync<Row>(new CommandDefinition(
            "SELECT uid, password_hash FROM account WHERE password_hash LIKE 'plain$%'", cancellationToken: ct));
        return rows.Select(r => (r.Uid, r.PasswordHash!)).ToList();
    }
}
