using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Economy;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;

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
/// API única para itens e cards que entram ou saem do personagem por uma fonte (NPC, loja, recompensa, GM, depósito da
/// conta). Cada operação confere espaço antes, muda tudo ou nada, registra a auditoria e manda os pacotes de
/// recebimento. Thread do mundo.
/// <para>
/// O depósito da conta (Cash/Shop Depository) é sempre transferência atômica (<see cref="CharacterTransfers"/>); os
/// callbacks rodam na thread do mundo também com o jogador offline (pacotes para sessão fechada são descartados).
/// </para>
/// </summary>
public sealed class ItemService(CharacterTransfers transfers, IAccountTransferStore store, IGameClock clock)
{
    /// <summary>
    /// Entrega itens na aba Items: um <c>0xC9</c> com as posições alteradas (e <c>0xFC</c> se <paramref name="refresh"/>).
    /// <paramref name="beforeNotify"/> recebe as posições alteradas depois da entrega e antes dos pacotes (resposta que
    /// o cliente espera antes do recebimento). Falso, sem mudar nada, se faltar espaço ou a entrega for inválida
    /// (quantidade zero, prazo em item empilhável).
    /// </summary>
    public bool GiveItems(Player player, IReadOnlyList<ItemGrant> items, EconomyReason reason, bool refresh = true,
        Action<IReadOnlyList<InventoryItem>>? beforeNotify = null)
    {
        var bag = player.Character.Inventory.Main;
        if (!IsValid(player, items, cards: false) || !bag.CanFit(Amounts(items))) return false;

        var added = Put(bag, items);
        player.Character.Economy.RecordItems(reason, Amounts(items), cards: false);
        beforeNotify?.Invoke(added);
        Notify(player, added, cards: false, refresh);
        return true;
    }

    /// <summary>Como <see cref="GiveItems"/>, na aba Cards (<c>0xC8</c>). Cards não têm prazo.</summary>
    public bool GiveCards(Player player, IReadOnlyList<ItemGrant> cards, EconomyReason reason, bool refresh = true,
        Action<IReadOnlyList<InventoryItem>>? beforeNotify = null)
    {
        var bag = player.Character.Inventory.Battle;
        if (!IsValid(player, cards, cards: true) || !bag.CanFit(Amounts(cards))) return false;

        var added = Put(bag, cards);
        player.Character.Economy.RecordItems(reason, Amounts(cards), cards: true);
        beforeNotify?.Invoke(added);
        Notify(player, added, cards: true, refresh);
        return true;
    }

    /// <summary>
    /// Troca na aba Items: tira <paramref name="takeAmount"/> de <paramref name="takeItemId"/> e entrega
    /// <paramref name="give"/>, contando com a posição que a retirada libera. Tudo ou nada; um <c>0xC9</c> com o estado
    /// final das posições. <paramref name="beforeNotify"/> como em <see cref="GiveItems"/>.
    /// </summary>
    public bool Exchange(Player player, uint takeItemId, uint takeAmount, IReadOnlyList<ItemGrant> give, EconomyReason reason,
        bool refresh = true, Action<IReadOnlyList<InventoryItem>>? beforeNotify = null)
    {
        var bag = player.Character.Inventory.Main;
        if (takeAmount == 0 || bag.Count(takeItemId) < takeAmount || !IsValid(player, give, cards: false)) return false;

        var removed = bag.Remove(takeItemId, takeAmount);
        if (!bag.CanFit(Amounts(give)))
        {
            bag.Add(takeItemId, takeAmount);
            return false;
        }
        var added = Put(bag, give);
        player.Character.Economy.RecordItems(reason, [(takeItemId, takeAmount)], cards: false, sign: -1);
        player.Character.Economy.RecordItems(reason, Amounts(give), cards: false);

        // posição esvaziada e ocupada de novo na mesma troca: vale o último estado
        var changed = removed.Concat(added).GroupBy(i => i.Slot).Select(g => g.Last()).ToList();
        beforeNotify?.Invoke(changed);
        Notify(player, changed, cards: false, refresh);
        return true;
    }

    /// <summary>
    /// Tira <paramref name="amount"/> da posição <paramref name="slot"/> de <paramref name="bag"/> (venda, entrega a
    /// NPC). A posição precisa ter o item <paramref name="uid"/>/<paramref name="itemId"/>, sem vencer, com a quantidade.
    /// Devolve a pilha que sobrou (quantidade zero se acabou) ou null sem mudar nada. Não manda pacote: a resposta é de
    /// quem chama.
    /// </summary>
    public InventoryItem? TakeAt(Player player, Bag bag, int slot, uint uid, uint itemId, uint amount, EconomyReason reason)
    {
        var inventory = player.Character.Inventory;
        if (amount == 0 || bag[slot] is not { } item || item.Uid != uid || item.ItemId != itemId) return null;
        if (inventory.IsExpired(item) || !bag.Consume(item, amount)) return null;

        player.Character.Economy.RecordItems(reason, [(itemId, amount)], cards: bag.IsCardBag, sign: -1);
        return item;
    }

    private static bool IsValid(Player player, IReadOnlyList<ItemGrant> grants, bool cards)
    {
        var inventory = player.Character.Inventory;
        return grants.All(g => g.Amount > 0
            && (g.Duration.Kind == ItemDurationKind.Default || !cards && inventory.MaxStackOf(g.ItemId, isCard: false) <= 1));
    }

    private static IEnumerable<(uint ItemId, uint Amount)> Amounts(IEnumerable<ItemGrant> grants) =>
        grants.Select(g => (g.ItemId, g.Amount));

    private static List<InventoryItem> Put(Bag bag, IEnumerable<ItemGrant> grants) =>
        grants.SelectMany(g => bag.Add(g.ItemId, g.Amount, g.Duration)).ToList();

    private static void Notify(Player player, IReadOnlyList<InventoryItem> changed, bool cards, bool refresh)
    {
        if (changed.Count > 0 && cards)
            player.Send(new BattleItemReceivePacket(changed));
        else if (changed.Count > 0)
            player.Send(new ItemReceivePacket(changed));
        if (refresh)
            player.RefreshInventory();
    }

    /// <summary>
    /// Guarda <paramref name="entries"/> no depósito da conta. Quem chama já aplicou o custo no personagem (moeda, item
    /// tirado da mochila) e passa em <paramref name="undo"/> como desfazê-lo: ele roda se a transferência não gravar.
    /// </summary>
    public void SendToAccountStorage(Player player, IReadOnlyList<AccountStorageEntry> entries, EconomyReason reason, Action undo,
        Action<StorageSendResult> done)
    {
        if (player.State != PlayerStates.Idle || entries.Count == 0)
        {
            undo();
            done(entries.Count == 0 ? StorageSendResult.Failed : StorageSendResult.Busy);
            return;
        }

        uint accountUid = player.Account.Uid;
        transfers.Run(player, "account-storage.send", AccountEntries(player, reason, entries.Select(e => (e.Kind, e.ItemId, e.Amount)), sign: 1),
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

    /// <summary>Movimentação no depósito da conta (sem personagem): positivo entrou nele, negativo saiu.</summary>
    private IReadOnlyList<EconomyEntry> AccountEntries(Player player, EconomyReason reason,
        IEnumerable<(CashShopItemKind Kind, uint ItemId, uint Amount)> items, int sign)
    {
        var at = clock.UtcNow;
        return items.GroupBy(i => (i.Kind, i.ItemId))
            .Select(g => EconomyEntry.Create(at, player.Account.Uid, null, reason,
                g.Key.Kind == CashShopItemKind.Card ? EconomyAsset.Card : EconomyAsset.Item, g.Key.ItemId,
                sign * g.Sum(i => (long)i.Amount), null))
            .ToList();
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
    public void TakeFromAccountStorage(Player player, CashDepositoryRow row, EconomyReason reason, Action<StorageTake> done)
    {
        if (player.State != PlayerStates.Idle)
        {
            done(new(StorageTakeResult.Busy, []));
            return;
        }

        var inventory = player.Character.Inventory;
        var bag = row.Kind == CashShopItemKind.Card ? inventory.Battle : inventory.Main;
        // o prazo de um item com validade no depósito continua de onde estava
        var duration = row.Kind == CashShopItemKind.Item && row.ExpiresAt > 0
            ? ItemDuration.FromSeconds(Math.Max(1, row.ExpiresAt - clock.UnixNow))
            : ItemDuration.Default;
        if (bag.Grant(row.ItemId, row.Amount, duration) is not { } grant)
        {
            done(new(StorageTakeResult.InventoryFull, []));
            return;
        }

        uint accountUid = player.Account.Uid;
        // saiu da conta e entrou no personagem: as duas pontas, gravadas só se a transferência gravar
        var asset = row.Kind == CashShopItemKind.Card ? EconomyAsset.Card : EconomyAsset.Item;
        var at = clock.UtcNow;
        var economy = AccountEntries(player, reason, [(row.Kind, row.ItemId, row.Amount)], sign: -1)
            .Append(EconomyEntry.Create(at, accountUid, player.Character.Uid, reason, asset, row.ItemId, row.Amount, null))
            .ToList();
        transfers.Run(player, "account-storage.take", economy,
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
