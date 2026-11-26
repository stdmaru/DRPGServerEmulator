using Dapper;
using DRPGServer.Game.Entities;

namespace DRPGServer.Data.Repositories;

/// <param name="AuthKey">Chave em texto puro, como o emissor grava; <c>null</c> depois de convertida.</param>
/// <param name="AuthKeyHash">Hash da chave (<see cref="Game.Accounts.AuthKeyHash"/>).</param>
public sealed record AccountRecord(AuthenticatedAccount Account, string? AuthKey, string? AuthKeyHash);

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

public sealed class AccountRepository(Database database) : IAccountStore
{
    private sealed class Row
    {
        public uint Uid { get; set; }
        public string Username { get; set; } = "";
        public string? AuthKey { get; set; }
        public string? AuthKeyHash { get; set; }
        public byte? Authority { get; set; }
    }

    public async Task<AccountRecord?> FindByUsernameAsync(string username, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var row = await db.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            "SELECT uid, username, auth_key, auth_key_hash, authority FROM account WHERE username = @username",
            new { username }, cancellationToken: ct));
        return row is null
            ? null
            : new AccountRecord(new AuthenticatedAccount(row.Uid, row.Username, (Authority)(row.Authority ?? 0)), row.AuthKey, row.AuthKeyHash);
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
}
