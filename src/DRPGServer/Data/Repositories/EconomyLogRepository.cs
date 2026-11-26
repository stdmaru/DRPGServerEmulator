using Dapper;
using DRPGServer.Game.Economy;

namespace DRPGServer.Data.Repositories;

/// <summary>Trilha de auditoria da economia (<c>economy_log</c>), sempre dentro da transação da operação.</summary>
public interface IAuditLog
{
    Task WriteAsync(UnitOfWork uow, IReadOnlyList<EconomyEntry> entries, CancellationToken ct);
}

public sealed class EconomyLogRepository : IAuditLog
{
    public async Task WriteAsync(UnitOfWork uow, IReadOnlyList<EconomyEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return;
        // o mesmo snapshot pode ser gravado de novo (confirmação perdida, dead-letter): o id repetido é ignorado
        await uow.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT IGNORE INTO economy_log
                (id, at, account_uid, character_uid, source, action, asset, asset_id, amount, balance_after, detail)
            VALUES (@Id, @At, @AccountUid, @CharacterUid, @Source, @Action, @Asset, @AssetId, @Amount, @BalanceAfter, @Detail)
            """, entries.Select(e => new
        {
            Id = e.Id.ToByteArray(bigEndian: true),
            At = e.At.UtcDateTime,
            e.AccountUid,
            e.CharacterUid,
            Source = e.Source.ToString(),
            e.Action,
            Asset = e.Asset.ToString(),
            e.AssetId,
            e.Amount,
            e.BalanceAfter,
            e.Detail,
        }), uow.Transaction, cancellationToken: ct));
    }
}
