using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;

namespace DRPGServer.Game.Items;

/// <summary>Resultado de <see cref="ItemService.SendToAccountStorage"/>.</summary>
public enum StorageSendResult
{
    Ok,
    /// <summary>O depósito da conta não tem posições para tudo.</summary>
    Full,
    /// <summary>O jogador não está livre (batalha, outra transferência).</summary>
    Busy,
    Failed,
}

/// <summary>Resultado de <see cref="ItemService.TakeFromAccountStorage"/>.</summary>
public enum StorageTakeResult
{
    Ok,
    /// <summary>A linha não está mais no depósito (retirada por outra conexão).</summary>
    NotFound,
    InventoryFull,
    /// <summary>O jogador não está livre (batalha, outra transferência).</summary>
    Busy,
    Failed,
}

/// <param name="Items">Posições da mochila alteradas (só em <see cref="StorageTakeResult.Ok"/>).</param>
public readonly record struct StorageTake(StorageTakeResult Result, IReadOnlyList<InventoryItem> Items);

/// <summary>
/// Movimentação de itens entre o personagem e o depósito da conta (Cash/Shop Depository), sempre como transferência
/// atômica (<see cref="CharacterTransfers"/>). Loja NPC, Cash Depository, correio e troca usam esta API. Thread do
/// mundo; os callbacks rodam nela também com o jogador offline (pacotes para sessão fechada são descartados).
/// </summary>
public sealed class ItemService(CharacterTransfers transfers, IAccountTransferStore store, IGameClock clock)
{
    /// <summary>
    /// Guarda <paramref name="entries"/> no depósito da conta. Quem chama já aplicou o custo no personagem (moeda, item
    /// tirado da mochila) e passa em <paramref name="undo"/> como desfazê-lo: ele roda se a transferência não gravar.
    /// </summary>
    public void SendToAccountStorage(Player player, IReadOnlyList<AccountStorageEntry> entries, Action undo,
        Action<StorageSendResult> done)
    {
        if (player.State != PlayerStates.Idle || entries.Count == 0)
        {
            undo();
            done(entries.Count == 0 ? StorageSendResult.Failed : StorageSendResult.Busy);
            return;
        }

        uint accountUid = player.Account.Uid;
        transfers.Run(player, "account-storage.send",
            commit: (snapshot, ct) => store.DepositAsync(accountUid, entries, snapshot, ct),
            settled: outcome =>
            {
                if (outcome == TransferOutcome.Committed)
                {
                    done(StorageSendResult.Ok);
                    return;
                }
                undo();
                done(outcome == TransferOutcome.Refused ? StorageSendResult.Full : StorageSendResult.Failed);
            });
    }

    /// <summary>Divide <paramref name="quantity"/> em pilhas do tamanho máximo do item, como o depósito guarda.</summary>
    public static IReadOnlyList<AccountStorageEntry> StorageEntries(Player player, CashShopItemKind kind, uint itemId,
        uint quantity, uint expiresAt = 0)
    {
        uint maxStack = Math.Max(1u, player.Character.Inventory.MaxStackOf(itemId, isCard: kind == CashShopItemKind.Card));
        var entries = new List<AccountStorageEntry>();
        for (uint remaining = quantity; remaining > 0;)
        {
            uint chunk = Math.Min(remaining, maxStack);
            entries.Add(new AccountStorageEntry(kind, itemId, chunk, expiresAt));
            remaining -= chunk;
        }
        return entries;
    }

    /// <summary>
    /// Tira <paramref name="row"/> do depósito da conta para a mochila (cards na de batalha). A linha é conferida de
    /// novo dentro da transação: se outra conexão já a retirou, nada muda.
    /// </summary>
    public void TakeFromAccountStorage(Player player, CashDepositoryRow row, Action<StorageTake> done)
    {
        if (player.State != PlayerStates.Idle)
        {
            done(new(StorageTakeResult.Busy, []));
            return;
        }

        var inventory = player.Character.Inventory;
        var bag = row.Kind == CashShopItemKind.Card ? inventory.Battle : inventory.Main;
        // o prazo de um item com validade no depósito continua de onde estava
        long? duration = row.Kind == CashShopItemKind.Item && row.ExpiresAt > 0
            ? Math.Max(1, row.ExpiresAt - clock.UnixNow)
            : null;
        if (bag.Grant(row.ItemId, row.Amount, duration) is not { } grant)
        {
            done(new(StorageTakeResult.InventoryFull, []));
            return;
        }

        uint accountUid = player.Account.Uid;
        transfers.Run(player, "account-storage.take",
            commit: (snapshot, ct) => store.TakeAsync(accountUid, row, snapshot, ct),
            settled: outcome =>
            {
                if (outcome == TransferOutcome.Committed)
                {
                    done(new(StorageTakeResult.Ok, grant.Changed));
                    return;
                }
                bag.Revert(grant);
                done(new(outcome == TransferOutcome.Refused ? StorageTakeResult.NotFound : StorageTakeResult.Failed, []));
            });
    }
}
