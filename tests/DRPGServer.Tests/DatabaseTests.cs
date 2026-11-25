using Dapper;
using DRPGServer.Configuration;
using DRPGServer.Data;
using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace DRPGServer.Tests;

/// <summary>
/// Teste contra um MariaDB de verdade. Só roda com <c>DRPG_TEST_DB_HOST</c> definido (no devcontainer:
/// <c>DRPG_TEST_DB_HOST=database</c>); cada classe cria um banco descartável e o apaga no fim.
/// </summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DatabaseFixture.HostVariable)))
            Skip = $"defina {DatabaseFixture.HostVariable} para rodar contra o banco";
    }
}

public sealed class DatabaseFixture : IAsyncLifetime
{
    public const string HostVariable = "DRPG_TEST_DB_HOST";

    public Database Database { get; private set; } = null!;
    public IOptions<DatabaseOptions> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var host = Environment.GetEnvironmentVariable(HostVariable);
        if (string.IsNullOrEmpty(host)) return;

        Options = Microsoft.Extensions.Options.Options.Create(new DatabaseOptions
        {
            Host = host,
            User = Environment.GetEnvironmentVariable("DRPG_TEST_DB_USER") ?? "root",
            Password = Environment.GetEnvironmentVariable("DRPG_TEST_DB_PASSWORD") ?? "root",
            Name = $"drpg_test_{Guid.NewGuid():N}"[..30],
            MigrationsPath = FindMigrations(),
            ConnectTimeoutSeconds = 30,
        });
        Database = new Database(Options);
        await new DatabaseMigrator(Database, Options, TimeProvider.System, NullLogger<DatabaseMigrator>.Instance)
            .MigrateAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (Database is null) return;
        await using var server = new MySqlConnection(Database.ServerConnectionString);
        await server.OpenAsync();
        await server.ExecuteAsync($"DROP DATABASE IF EXISTS `{Database.DatabaseName}`");
    }

    /// <summary>Cria uma conta e um personagem; devolve os uids.</summary>
    public async Task<(uint Account, uint Character)> NewCharacterAsync(DigimonUids uids, string name)
    {
        await using var db = await Database.OpenAsync();
        uint account = await db.ExecuteScalarAsync<uint>(
            "INSERT INTO account (username, password, auth_key) VALUES (@name, '', ''); SELECT LAST_INSERT_ID();", new { name });
        var characters = new CharacterRepository(Database, uids);
        uint character = (await characters.CreateAsync(new NewCharacter(account, 0, name, 1, 10, "Koromon", 79, 56, 56), default))!.Value;
        return (account, character);
    }

    private static string FindMigrations()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "sql", "upgrade");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("sql/upgrade não encontrado acima da pasta dos testes");
    }
}

/// <summary>SQL das transferências conta ↔ personagem (<see cref="AccountTransferRepository"/>).</summary>
public class AccountTransferDatabaseTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private readonly DigimonUids _uids = new();

    private (CashRepository Cash, CharacterRepository Characters, AccountTransferRepository Transfers) Repositories()
    {
        var cash = new CashRepository(db.Database, TestSupport.Clock);
        var characters = new CharacterRepository(db.Database, _uids);
        return (cash, characters, new AccountTransferRepository(db.Database, cash, characters));
    }

    private static CharacterSnapshot SnapshotWithBits(uint characterUid, double bits)
    {
        var character = Entities.Character();
        character.SetBits(bits);
        return CharacterSnapshot.Of(character) with { Uid = characterUid, Digimons = [], Forms = [], Titles = [] };
    }

    private async Task<double> BitsAsync(uint characterUid)
    {
        await using var connection = await db.Database.OpenAsync();
        return await connection.ExecuteScalarAsync<double>("SELECT bits FROM `character` WHERE uid = @characterUid", new { characterUid });
    }

    [DatabaseFact]
    public async Task Deposit_writes_the_depository_and_the_snapshot_together()
    {
        _uids.Seed(10_000);
        var (account, character) = await db.NewCharacterAsync(_uids, "Deposit1");
        var (cash, _, transfers) = Repositories();

        Assert.True(await transfers.DepositAsync(account, [new AccountStorageEntry(CashShopItemKind.Item, 21008, 3)],
            SnapshotWithBits(character, 1234), default));

        var row = Assert.Single(await cash.ListDepositoryAsync(account, default));
        Assert.Equal((21008u, 3u), (row.ItemId, row.Amount));
        Assert.Equal(1234, await BitsAsync(character));
    }

    [DatabaseFact]
    public async Task Full_depository_writes_neither_side()
    {
        _uids.Seed(20_000);
        var (account, character) = await db.NewCharacterAsync(_uids, "Deposit2");
        var (cash, _, transfers) = Repositories();
        var full = Enumerable.Range(0, CashRepository.DepositoryCapacityPerKind)
            .Select(_ => new AccountStorageEntry(CashShopItemKind.Item, 1, 1)).ToList();
        Assert.True(await transfers.DepositAsync(account, full, SnapshotWithBits(character, 10), default));

        Assert.False(await transfers.DepositAsync(account, [new AccountStorageEntry(CashShopItemKind.Item, 2, 1)],
            SnapshotWithBits(character, 999), default));

        Assert.Equal(CashRepository.DepositoryCapacityPerKind, (await cash.ListDepositoryAsync(account, default)).Count);
        Assert.Equal(10, await BitsAsync(character));
    }

    [DatabaseFact]
    public async Task Take_removes_the_row_once_and_a_stale_row_changes_nothing()
    {
        _uids.Seed(30_000);
        var (account, character) = await db.NewCharacterAsync(_uids, "Take1");
        var (cash, _, transfers) = Repositories();
        await transfers.DepositAsync(account, [new AccountStorageEntry(CashShopItemKind.Card, 74, 50)], SnapshotWithBits(character, 1), default);
        var row = (await cash.FindDepositoryItemAsync(account, CashShopItemKind.Card,
            (await cash.ListDepositoryAsync(account, default))[0].Uid, default))!;

        Assert.True(await transfers.TakeAsync(account, row, SnapshotWithBits(character, 2), default));
        Assert.False(await transfers.TakeAsync(account, row, SnapshotWithBits(character, 3), default));   // já retirada

        Assert.Empty(await cash.ListDepositoryAsync(account, default));
        Assert.Equal(2, await BitsAsync(character));
    }

    [DatabaseFact]
    public async Task Concurrent_takes_of_the_same_row_commit_only_once()
    {
        _uids.Seed(40_000);
        var (account, character) = await db.NewCharacterAsync(_uids, "Take2");
        var (cash, _, transfers) = Repositories();
        await transfers.DepositAsync(account, [new AccountStorageEntry(CashShopItemKind.Item, 718, 1)], SnapshotWithBits(character, 1), default);
        var row = (await cash.ListDepositoryAsync(account, default))[0];

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => transfers.TakeAsync(account, row, SnapshotWithBits(character, 100 + i), default)));

        Assert.Equal(1, results.Count(r => r));
        Assert.Empty(await cash.ListDepositoryAsync(account, default));
    }
}
