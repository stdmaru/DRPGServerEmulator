using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;
using Microsoft.Extensions.Logging;

namespace DRPGServer.Game.Shop;

/// <summary>
/// Regras de negócio do Cash Shop (<c>0x0159</c>/<c>0x0160</c>/<c>0x0161</c>), do Cash Depository
/// (<c>0x00CA</c>/<c>0x00DE</c>/<c>0x00DD</c>) e da entrega no Shop Depository da conta. Roda na thread do mundo; o
/// banco é acessado só por <see cref="WorldIo"/>.
/// </summary>
public sealed class CashShopService(
    GameData data,
    ICashStore store,
    WorldIo io,
    ILogger<CashShopService> log)
{
    public const uint MaxPurchaseQuantity = 99;

    /// <summary>✅ Abre a loja de Cash e retorna o saldo atual da conta (<c>0x0159</c>).</summary>
    public void OpenShop(Player player, uint headerWord)
    {
        uint accountUid = player.Account.Uid;
        string username = player.Account.Username;
        io.Run(player, "cash-shop.open",
            work: ct => store.GetBalanceAsync(accountUid, ct),
            then: balance =>
            {
                if (balance is null)
                {
                    player.Send(new CashShopOpenPacket(headerWord, errorCode: 1, accountUid, 0, 0, username));
                    return;
                }
                player.Send(new CashShopOpenPacket(headerWord, errorCode: 0, accountUid, balance.Value.Cash, balance.Value.BonusCash, username));
            },
            failed: _ => player.Send(new CashShopOpenPacket(headerWord, errorCode: 1, accountUid, 0, 0, username)));
    }

    /// <summary>
    /// ✅ Compra um produto do catálogo (<c>0x0160</c>): recalcula o preço server-side, debita o Cash da conta
    /// e deposita os itens/cards no Cash Depository da conta em uma transação atômica.
    /// </summary>
    public void BuyProduct(Player player, uint headerWord, uint productId, uint quantity)
    {
        log.LogInformation("CashShopBuy recebido de {Player}: itemDataId={ProductId} qty={Qty}", player, productId, quantity);

        if (quantity is 0 or > MaxPurchaseQuantity)
        {
            log.LogWarning("CashShopBuy rejeitado ({Player}): quantidade inválida {Qty}", player, quantity);
            player.SendNotice("[Cash Shop]", "Quantidade inválida.");
            return;
        }
        if (data.FindCashShopProduct(productId) is not { } product)
        {
            log.LogWarning("CashShopBuy rejeitado ({Player}): itemDataId={ProductId} não encontrado no catálogo ({Count} produtos)",
                player, productId, data.CashShopProducts.Count);
            player.SendNotice("[Cash Shop]", $"Produto #{productId} não encontrado.");
            return;
        }

        ulong totalPriceLong = (ulong)product.Price * quantity;
        if (totalPriceLong > uint.MaxValue)
        {
            player.SendNotice("[Cash Shop]", "Valor da compra excede o limite permitido.");
            return;
        }
        uint totalPrice = (uint)totalPriceLong;

        var expanded = ExpandProductEntries(product, quantity);
        if (expanded.Count == 0)
        {
            player.SendNotice("[Cash Shop]", $"Produto #{productId} sem itens válidos.");
            return;
        }

        uint accountUid = player.Account.Uid;
        io.Run(player, "cash-shop.buy",
            work: ct => store.PurchaseAsync(accountUid, product.ItemDataId, quantity, product.Price, totalPrice, expanded, ct),
            then: outcome =>
            {
                log.LogInformation("CashShopBuy resultado ({Player}): result={Result} itemDataId={ItemDataId} totalPrice={Price} saldo={Cash}+{Bonus}",
                    player, outcome.Result, product.ItemDataId, totalPrice, outcome.Balance.Cash, outcome.Balance.BonusCash);
                switch (outcome.Result)
                {
                    case CashPurchaseResult.Ok:
                        player.Send(new CashShopBuyPacket(
                            headerWord,
                            errorCode: 0,
                            accountUid,
                            product.ItemDataId,
                            quantity,
                            product.Price,
                            outcome.Balance.Cash,
                            outcome.Balance.BonusCash));
                        break;
                    case CashPurchaseResult.InsufficientCash:
                        player.SendNotice("[Cash Shop]", "Saldo de Cash insuficiente.");
                        break;
                    case CashPurchaseResult.DepositoryFull:
                        player.SendNotice("[Cash Shop]", "Cash Depository cheio. Retire itens antes de comprar.");
                        break;
                    default:
                        player.SendNotice("[Cash Shop]", "Não foi possível concluir a compra.");
                        break;
                }
            },
            failed: _ => player.SendNotice("[Cash Shop]", "Erro interno ao processar compra."));
    }

    /// <summary>✅ Consulta o histórico de compras por ano/mês/página (<c>0x0161</c>).</summary>
    public void GetHistory(Player player, uint headerWord, ushort year, ushort month, ushort page)
    {
        uint accountUid = player.Account.Uid;
        ushort clampedPage = Math.Max((ushort)1, page);
        io.Run(player, "cash-shop.history",
            work: ct => store.GetHistoryAsync(accountUid, year, month, clampedPage, ct),
            then: history => player.Send(new CashShopHistoryPacket(
                headerWord,
                errorCode: 0,
                year,
                month,
                Math.Min(clampedPage, history.TotalPages),
                history.TotalPages,
                accountUid,
                history.Entries)),
            failed: _ => player.Send(new CashShopHistoryPacket(headerWord, errorCode: 0, year, month, clampedPage, 1, accountUid, [])));
    }

    /// <summary>✅ Lista o Cash Depository da conta (<c>0x00DE</c>: 24 cards + 24 items).</summary>
    public void ListDepository(Player player)
    {
        uint accountUid = player.Account.Uid;
        io.Run(player, "cash-depository.list",
            work: ct => store.ListDepositoryAsync(accountUid, ct),
            then: rows =>
            {
                var cards = new InventoryItem?[CashRepository.DepositoryCapacityPerKind];
                var items = new InventoryItem?[CashRepository.DepositoryCapacityPerKind];
                foreach (var r in rows)
                {
                    if (r.Slot >= CashRepository.DepositoryCapacityPerKind) continue;
                    var item = ToInventoryItem(r);
                    if (r.Kind == CashShopItemKind.Card) cards[r.Slot] = item;
                    else items[r.Slot] = item;
                }
                player.Send(new CashDepositoryListPacket(cards, items, success: true));
            });
    }

    /// <summary>
    /// ✅ Retira um Card (<c>kind=1</c>) ou Item (<c>kind=2</c>) do Cash Depository para a mochila do personagem (<c>0x00DD</c>).
    /// Devolve ao depósito se a mochila não tiver espaço ou o jogador sair antes da entrega.
    /// ❓ A entrega na mochila ainda não é atômica com o snapshot do personagem (PR #41: S3).
    /// </summary>
    public void WithdrawDepository(Player player, CashShopItemKind kind, uint uid)
    {
        if (uid == 0 || kind is not CashShopItemKind.Card and not CashShopItemKind.Item)
        {
            player.Send(new CashDepositoryWithdrawPacket(kind, null, CashDepositoryWithdrawResult.Failed));
            return;
        }

        uint accountUid = player.Account.Uid;
        io.Run(player, "cash-depository.withdraw",
            work: ct => store.TakeDepositoryItemAsync(accountUid, kind, uid, ct),
            then: taken =>
            {
                if (taken is null)
                {
                    player.Send(new CashDepositoryWithdrawPacket(kind, null, CashDepositoryWithdrawResult.NotFound));
                    return;
                }
                if (!Deliver(player, taken))
                    Restore(accountUid, taken);
            },
            failed: _ => player.Send(new CashDepositoryWithdrawPacket(kind, null, CashDepositoryWithdrawResult.Failed)),
            discarded: taken =>
            {
                if (taken is not null) Restore(accountUid, taken);
            });
    }

    private bool Deliver(Player player, CashDepositoryRow taken)
    {
        if (player.InBattle) return false;

        var inv = player.Character.Inventory;
        var bag = taken.Kind == CashShopItemKind.Card ? inv.Battle : inv.Main;
        if (bag.Room(taken.ItemId) < taken.Amount)
        {
            player.Send(new CashDepositoryWithdrawPacket(taken.Kind, ToInventoryItem(taken), CashDepositoryWithdrawResult.InventoryFull));
            return false;
        }

        long? durationOverride = null;
        if (taken.Kind == CashShopItemKind.Item && taken.ExpiresAt > 0)
        {
            long remaining = taken.ExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            durationOverride = Math.Max(1, remaining);
        }

        var added = bag.Add(taken.ItemId, taken.Amount, durationOverride);
        if (added.Count == 0)
        {
            player.Send(new CashDepositoryWithdrawPacket(taken.Kind, ToInventoryItem(taken), CashDepositoryWithdrawResult.InventoryFull));
            return false;
        }

        player.RequestImmediateSave();
        player.Send(new CashDepositoryWithdrawPacket(taken.Kind, ToInventoryItem(taken), CashDepositoryWithdrawResult.Ok));
        if (taken.Kind == CashShopItemKind.Card)
            player.Send(new BattleItemReceivePacket(added));
        else
            player.Send(new ItemReceivePacket(added));
        return true;
    }

    /// <summary>Devolve ao Cash Depository o que foi retirado e não entregue. Não é cancelado na parada.</summary>
    private void Restore(uint accountUid, CashDepositoryRow taken) =>
        io.Run($"cash-depository.restore conta={accountUid} uid={taken.Uid} item={taken.ItemId}x{taken.Amount} (ITEM FORA DO DEPÓSITO)",
            _ => store.RestoreDepositoryItemAsync(accountUid, taken, CancellationToken.None));

    /// <summary>
    /// Deposita um item no <b>Shop Depository</b> (<c>cash_depository_item</c>) da conta do jogador. Reutilizável por
    /// lojas NPC (ex.: loja 5050 de Coins), eventos, missões e comandos GM. <paramref name="then"/> roda na thread do
    /// mundo com <c>true</c> se depositou ou <c>false</c> se o depósito está cheio ou indisponível, e só se o jogador
    /// continua online. ❓ Não é atômico com o snapshot do personagem (PR #41: S3).
    /// </summary>
    public void SendToShopDepository(Player player, uint itemId, uint quantity, uint expiresAt, Action<bool> then) =>
        SendBatchToShopDepository(player, Split(CashShopItemKind.Item, itemId, quantity, expiresAt,
            player.Character.Inventory.MaxStackOf(itemId, isCard: false)), then);

    /// <summary>Deposita um card no Shop Depository da conta (ver <see cref="SendToShopDepository"/>).</summary>
    public void SendCardToShopDepository(Player player, uint cardId, uint quantity, Action<bool> then) =>
        SendBatchToShopDepository(player, Split(CashShopItemKind.Card, cardId, quantity, 0,
            player.Character.Inventory.MaxStackOf(cardId, isCard: true)), then);

    /// <summary>Deposita um lote de itens e cards no Shop Depository da conta (ver <see cref="SendToShopDepository"/>).</summary>
    public void SendBatchToShopDepository(Player player,
        IReadOnlyList<(CashShopItemKind Kind, uint ItemId, uint Amount, uint ExpiresAt)> entries, Action<bool> then)
    {
        if (entries.Count == 0)
        {
            then(false);
            return;
        }
        uint accountUid = player.Account.Uid;
        io.Run(player, "shop-depository.deposit",
            work: ct => store.DepositAsync(accountUid, entries, ct),
            then: then,
            failed: _ => then(false));
    }

    private static List<(CashShopItemKind Kind, uint ItemId, uint Amount, uint ExpiresAt)> Split(
        CashShopItemKind kind, uint itemId, uint quantity, uint expiresAt, uint maxStack)
    {
        maxStack = Math.Max(1u, maxStack);
        var entries = new List<(CashShopItemKind, uint, uint, uint)>();
        for (uint remaining = quantity; remaining > 0;)
        {
            uint chunk = Math.Min(remaining, maxStack);
            entries.Add((kind, itemId, chunk, expiresAt));
            remaining -= chunk;
        }
        return entries;
    }

    /// <summary>Credita Cash (ou bônus) na conta — usado pelo comando GM <c>/cash</c>.</summary>
    public void GrantCash(Player requester, uint cashDelta, uint bonusCashDelta, Action<CashBalance?> then, Action failed) =>
        io.Run(requester, "gm.cash",
            work: ct => store.AddCashAsync(requester.Account.Uid, cashDelta, bonusCashDelta, ct),
            then: then,
            failed: _ => failed());

    private List<(CashShopItemKind Kind, uint ItemId, uint Amount, uint ExpiresAt)> ExpandProductEntries(CashShopProduct product, uint quantity)
    {
        var result = new List<(CashShopItemKind, uint, uint, uint)>();
        foreach (var entry in product.Entries)
        {
            if (entry.Kind == CashShopItemKind.Card)
            {
                if (data.Cards.Count > 0 && data.FindCard(entry.ItemId) is null) continue;
                uint maxStack = Math.Max(1u, data.FindCard(entry.ItemId)?.MaxStack ?? 999u);
                ulong total = (ulong)entry.Amount * quantity;
                while (total > 0)
                {
                    uint chunk = (uint)Math.Min(total, maxStack);
                    result.Add((CashShopItemKind.Card, entry.ItemId, chunk, 0u));
                    total -= chunk;
                }
            }
            else
            {
                if (data.FindItem(entry.ItemId) is not { } info) continue;
                uint maxStack = Math.Max(1u, info.MaxStack);
                ulong total = (ulong)entry.Amount * quantity;
                while (total > 0)
                {
                    uint chunk = (uint)Math.Min(total, maxStack);
                    // Período começa a contar ao retirar do Cash Depository (ExpiresAt = 0 no depósito)
                    result.Add((CashShopItemKind.Item, entry.ItemId, chunk, 0u));
                    total -= chunk;
                }
            }
        }
        return result;
    }

    private InventoryItem ToInventoryItem(CashDepositoryRow row) => new()
    {
        Uid = row.Uid,
        ItemId = row.ItemId,
        Info = row.Kind == CashShopItemKind.Item ? data.FindItem(row.ItemId) : null,
        CardInfo = row.Kind == CashShopItemKind.Card ? data.FindCard(row.ItemId) : null,
        Slot = row.Slot,
        Amount = row.Amount,
        ExpiresAt = row.ExpiresAt,
    };
}
