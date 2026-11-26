using Dapper;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Accounts;
using DRPGServer.Game.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

/// <summary><c>auth_key</c> guardada como hash e conferida em tempo constante (S8).</summary>
public class AuthKeyTests
{
    /// <summary>Contas em memória com a mesma regra do SQL: o hash só entra se a chave em texto puro não mudou.</summary>
    private sealed class FakeAccounts : IAccountStore
    {
        public Dictionary<string, (uint Uid, string? Plain, string? Hash)> Rows { get; } = [];

        public Task<AccountRecord?> FindByUsernameAsync(string username, CancellationToken ct) =>
            Task.FromResult(Rows.TryGetValue(username, out var r)
                ? new AccountRecord(new AuthenticatedAccount(r.Uid, username, Authority.None), r.Plain, r.Hash)
                : null);

        public Task<bool> StoreAuthKeyHashAsync(uint accountUid, string plainKey, string hash, CancellationToken ct)
        {
            var (name, row) = Rows.Single(kv => kv.Value.Uid == accountUid);
            if (row.Plain != plainKey) return Task.FromResult(false);
            Rows[name] = (row.Uid, null, hash);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<(uint Uid, string AuthKey)>> PlainAuthKeysAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<(uint, string)>>(
                Rows.Values.Where(r => !string.IsNullOrEmpty(r.Plain)).Select(r => (r.Uid, r.Plain!)).ToList());
    }

    private static AccountAuthenticator Authenticator(FakeAccounts accounts) =>
        new(accounts, TestSupport.Clock, NullLogger<AccountAuthenticator>.Instance);

    [Fact]
    public void Hash_has_the_documented_format_and_a_fresh_salt()
    {
        var a = AuthKeyHash.Hash("KEY");
        var b = AuthKeyHash.Hash("KEY");
        var parts = a.Split('$');

        Assert.Equal(["pbkdf2-sha256", "100000"], parts[..2]);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);
        Assert.NotEqual(a, b);
        Assert.True(AuthKeyHash.Verify("KEY", a));
        Assert.False(AuthKeyHash.Verify("key", a));
        Assert.False(AuthKeyHash.Verify("KEY ", a));
    }

    [Theory]
    [InlineData("")]
    [InlineData("KEY")]
    [InlineData("pbkdf2-sha256$abc$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$0$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$1000$não-é-base64$AAAA")]
    [InlineData("md5$1000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void Malformed_hash_is_a_refusal_not_an_exception(string stored) =>
        Assert.False(AuthKeyHash.Verify("KEY", stored));

    [Fact]
    public void Hash_written_by_another_tool_with_other_iterations_verifies()
    {
        // o emissor pode gravar o hash direto (ex.: Python hashlib.pbkdf2_hmac('sha256', key, salt, 1000, 32))
        var salt = new byte[16];
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2("KEY"u8.ToArray(), salt, 1000,
            System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        var stored = $"pbkdf2-sha256$1000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        Assert.True(AuthKeyHash.Verify("KEY", stored));
    }

    [Fact]
    public async Task Plain_key_is_accepted_once_and_converted_to_hash()
    {
        var accounts = new FakeAccounts();
        accounts.Rows["tester"] = (1, "REPLAYKEY", null);
        var auth = Authenticator(accounts);

        Assert.NotNull(await auth.AuthenticateAsync("tester", "REPLAYKEY", default));
        var row = accounts.Rows["tester"];
        Assert.Null(row.Plain);
        Assert.True(AuthKeyHash.Verify("REPLAYKEY", row.Hash!));

        Assert.NotNull(await auth.AuthenticateAsync("tester", "REPLAYKEY", default));   // agora pelo hash
        Assert.Null(await auth.AuthenticateAsync("tester", "OUTRA", default));
    }

    [Fact]
    public async Task Wrong_unknown_or_empty_key_is_refused_without_converting()
    {
        var accounts = new FakeAccounts();
        accounts.Rows["tester"] = (1, "REPLAYKEY", null);
        accounts.Rows["nokey"] = (2, null, null);
        var auth = Authenticator(accounts);

        Assert.Null(await auth.AuthenticateAsync("tester", "ERRADA", default));
        Assert.Null(await auth.AuthenticateAsync("tester", "", default));
        Assert.Null(await auth.AuthenticateAsync("ninguem", "REPLAYKEY", default));
        Assert.Null(await auth.AuthenticateAsync("nokey", "", default));
        Assert.Equal("REPLAYKEY", accounts.Rows["tester"].Plain);
    }

    [Fact]
    public async Task New_plain_key_from_the_issuer_wins_over_the_old_hash()
    {
        // o emissor grava uma chave nova em texto puro a cada login; o hash antigo não vale mais
        var accounts = new FakeAccounts();
        accounts.Rows["tester"] = (1, "NOVA", AuthKeyHash.Hash("VELHA"));
        var auth = Authenticator(accounts);

        Assert.Null(await auth.AuthenticateAsync("tester", "VELHA", default));
        Assert.NotNull(await auth.AuthenticateAsync("tester", "NOVA", default));
        Assert.True(AuthKeyHash.Verify("NOVA", accounts.Rows["tester"].Hash!));
    }

    [Fact]
    public async Task Boot_converts_every_plain_key()
    {
        var accounts = new FakeAccounts();
        accounts.Rows["a"] = (1, "KA", null);
        accounts.Rows["b"] = (2, "KB", null);
        accounts.Rows["c"] = (3, null, AuthKeyHash.Hash("KC"));

        Assert.Equal(2, await Authenticator(accounts).ConvertPlainKeysAsync(default));
        Assert.All(accounts.Rows.Values, r => Assert.Null(r.Plain));
        Assert.True(AuthKeyHash.Verify("KB", accounts.Rows["b"].Hash!));
    }
}

/// <summary>SQL da conversão da <c>auth_key</c> (<see cref="AccountRepository"/>).</summary>
public class AccountRepositoryDatabaseTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    [DatabaseFact]
    public async Task Hash_is_stored_only_while_the_plain_key_is_unchanged()
    {
        await using (var conn = await db.Database.OpenAsync())
            await conn.ExecuteAsync("INSERT INTO account (username, auth_key) VALUES ('hashme', 'K1')");
        var repo = new AccountRepository(db.Database);
        var record = (await repo.FindByUsernameAsync("hashme", default))!;

        Assert.Contains(await repo.PlainAuthKeysAsync(default), r => r.Uid == record.Account.Uid);
        Assert.False(await repo.StoreAuthKeyHashAsync(record.Account.Uid, "K0", "h", default));   // o emissor trocou a chave
        Assert.True(await repo.StoreAuthKeyHashAsync(record.Account.Uid, "K1", AuthKeyHash.Hash("K1"), default));

        var after = (await repo.FindByUsernameAsync("hashme", default))!;
        Assert.Null(after.AuthKey);
        Assert.True(AuthKeyHash.Verify("K1", after.AuthKeyHash!));
        Assert.DoesNotContain(await repo.PlainAuthKeysAsync(default), r => r.Uid == record.Account.Uid);
    }
}
