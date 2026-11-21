using Dapper;
using DRPGServer.Game.Entities;

namespace DRPGServer.Data.Repositories;

public sealed record AccountRecord(AuthenticatedAccount Account, string? AuthKey);

public sealed class AccountRepository(Database database)
{
    private sealed class Row
    {
        public uint Uid { get; set; }
        public string Username { get; set; } = "";
        public string? AuthKey { get; set; }
        public byte? Authority { get; set; }
    }

    public async Task<AccountRecord?> FindByUsernameAsync(string username, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var row = await db.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            "SELECT uid, username, auth_key, authority FROM account WHERE username = @username",
            new { username }, cancellationToken: ct));
        return row is null
            ? null
            : new AccountRecord(new AuthenticatedAccount(row.Uid, row.Username, (Authority)(row.Authority ?? 0)), row.AuthKey);
    }
}
