using DRPGServer.Data.GameData;

namespace DRPGServer.Data.Repositories;

/// <summary>
/// Transferências entre o depósito da conta e o personagem (docs/emulador/seguranca.md#transferências-entre-conta-e-personagem):
/// a mudança na conta e o snapshot do personagem (já com a mudança dele) gravam na mesma transação, ou nenhum dos dois.
/// </summary>
public interface IAccountTransferStore
{
    /// <summary>Guarda <paramref name="entries"/> no depósito da conta e grava o snapshot. Falso: depósito cheio, nada gravado.</summary>
    Task<bool> DepositAsync(uint accountUid, IReadOnlyList<AccountStorageEntry> entries, CharacterSnapshot snapshot,
        CancellationToken ct);

    /// <summary>Tira <paramref name="row"/> do depósito da conta e grava o snapshot. Falso: a linha não está mais lá, nada gravado.</summary>
    Task<bool> TakeAsync(uint accountUid, CashDepositoryRow row, CharacterSnapshot snapshot, CancellationToken ct);
}

public sealed class AccountTransferRepository(Database database, CashRepository cash, CharacterRepository characters)
    : IAccountTransferStore
{
    public async Task<bool> DepositAsync(uint accountUid, IReadOnlyList<AccountStorageEntry> entries,
        CharacterSnapshot snapshot, CancellationToken ct)
    {
        await using var uow = await database.BeginAsync(ct);
        if (!await cash.DepositAsync(uow, accountUid, entries, ct)) return false;
        await characters.SaveAsync(uow, snapshot, ct);
        await uow.CommitAsync(ct);
        return true;
    }

    public async Task<bool> TakeAsync(uint accountUid, CashDepositoryRow row, CharacterSnapshot snapshot, CancellationToken ct)
    {
        await using var uow = await database.BeginAsync(ct);
        if (!await cash.TakeAsync(uow, accountUid, row, ct)) return false;
        await characters.SaveAsync(uow, snapshot, ct);
        await uow.CommitAsync(ct);
        return true;
    }
}
