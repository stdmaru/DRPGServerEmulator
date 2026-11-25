using Dapper;
using DRPGServer.Configuration;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace DRPGServer.Data;

/// <summary>Abre conexões (com pool) para o banco do jogo.</summary>
public sealed class Database(IOptions<DatabaseOptions> options)
{
    // colunas snake_case (item_id) nas propriedades PascalCase (ItemId) de todo repositório
    static Database() => DefaultTypeMap.MatchNamesWithUnderscores = true;

    private readonly string _connectionString = Build(options.Value, withDatabase: true);

    public string ServerConnectionString { get; } = Build(options.Value, withDatabase: false);
    public string DatabaseName => options.Value.Name;

    public async Task<MySqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    /// <summary>Abre uma conexão com uma transação (<see cref="UnitOfWork"/>).</summary>
    public Task<UnitOfWork> BeginAsync(CancellationToken ct = default) => UnitOfWork.BeginAsync(this, ct);

    private static string Build(DatabaseOptions o, bool withDatabase)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = o.Host,
            Port = (uint)o.Port,
            UserID = o.User,
            Password = o.Password,
            CharacterSet = "utf8mb4",
            Pooling = true,
            ConnectionTimeout = 5,
        };
        if (withDatabase) builder.Database = o.Name;
        return builder.ConnectionString;
    }
}
