using MySqlConnector;

namespace DRPGServer.Data;

/// <summary>
/// Uma transação do banco que vários repositórios compartilham (ex.: linha do depósito da conta + snapshot do
/// personagem). Sem <see cref="CommitAsync"/>, o descarte desfaz tudo.
/// </summary>
public sealed class UnitOfWork : IAsyncDisposable
{
    private bool _committed;

    private UnitOfWork(MySqlConnection connection, MySqlTransaction transaction)
    {
        Connection = connection;
        Transaction = transaction;
    }

    public MySqlConnection Connection { get; }
    public MySqlTransaction Transaction { get; }

    internal static async Task<UnitOfWork> BeginAsync(Database database, CancellationToken ct)
    {
        var connection = await database.OpenAsync(ct);
        try
        {
            return new UnitOfWork(connection, await connection.BeginTransactionAsync(ct));
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        await Transaction.CommitAsync(ct);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_committed && Connection.State == System.Data.ConnectionState.Open)
                await Transaction.RollbackAsync(CancellationToken.None);
        }
        finally
        {
            await Transaction.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
