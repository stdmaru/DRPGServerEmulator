using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;
using Microsoft.Extensions.Logging;

namespace DRPGServer.Game.Shop;

/// <summary>
/// Regras de negócio do Cash Shop (<c>0x0159</c>/<c>0x0160</c>/<c>0x0161</c>) e do Cash Depository
/// (<c>0x00CA</c>/<c>0x00DE</c>/<c>0x00DD</c>). Toda operação de banco roda fora da thread do mundo e
/// sincroniza o resultado de volta via <see cref="GameWorld.Post"/>.
/// </summary>
public sealed class CashShopService(
    GameData data,
    ICashStore store,
    GameWorld world,
    ILogger<CashShopService> log)
{
    public const uint MaxPurchaseQuantity = 99;

    /// <summary>✅ Abre a loja de Cash e retorna o saldo atual da conta (<c>0x0159</c>).</summary>
    public async Task OpenShopAsync(Player player, uint headerWord, CancellationToken ct = default)
    {
        uint accountUid = player.Account.Uid;
        string username = player.Account.Username;
        try
        {
            var balance = await store.GetBalanceAsync(accountUid, ct);
            world.Post(() =>
            {
                if (!player.IsOnline) return;
                if (balance is null)
                {
                    player.Send(new CashShopOpenPacket(headerWord, errorCode: 1, accountUid, 0, 0, username));
                    return;
                }
                player.Send(new CashShopOpenPacket(headerWord, errorCode: 0, accountUid, balance.Value.Cash, balance.Value.BonusCash, username));
            });
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Falha ao abrir Cash Shop para conta {AccountUid}", accountUid);
            world.Post(() =>
            {
                if (player.IsOnline)
                    player.Send(new CashShopOpenPacket(headerWord, errorCode: 1, accountUid, 0, 0, username));
            });
        }
    }

    /// <summary>
    /// ✅ Compra um produto do catálogo (<c>0x0160</c>): recalcula o preço server-side, debita o Cash da conta
    /// e deposita os itens/cards no Cash Depository da conta em uma transação atômica.
    /// </summary>
    public async Task BuyProductAsync(Player player, uint headerWord, uint productId, uint quantity, CancellationToken ct = default)
    {
        log.LogInformation("CashShopBuy recebido de {Player}: itemDataId={ProductId} qty={Qty} inBattle={InBattle}",
            player, productId, quantity, player.InBattle);

        // Validações autoritativas na thread do mundo antes de tocar no banco
        if (player.InBattle) return;
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
        try
        {
            var outcome = await store.PurchaseAsync(accountUid, product.ItemDataId, quantity, product.Price, totalPrice, expanded, ct);
            log.LogInformation("CashShopBuy resultado ({Player}): result={Result} itemDataId={ItemDataId} totalPrice={Price} saldo={Cash}+{Bonus}",
                player, outcome.Result, product.ItemDataId, totalPrice, outcome.Balance.Cash, outcome.Balance.BonusCash);
            world.Post(() =>
            {
                if (!player.IsOnline) return;
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
            });
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Falha na compra Cash Shop produto={ProductId} qtd={Qty} conta={AccountUid}", productId, quantity, accountUid);
            world.Post(() =>
            {
                if (player.IsOnline)
                    player.SendNotice("[Cash Shop]", "Erro interno ao processar compra.");
            });
        }
    }

    /// <summary>✅ Consulta o histórico de compras por ano/mês/página (<c>0x0161</c>).</summary>
    public async Task GetHistoryAsync(Player player, uint headerWord, ushort year, ushort month, ushort page, CancellationToken ct = default)
    {
        uint accountUid = player.Account.Uid;
        ushort clampedPage = Math.Max((ushort)1, page);
        try
        {
            var history = await store.GetHistoryAsync(accountUid, year, month, clampedPage, ct);
            world.Post(() =>
            {
                if (!player.IsOnline) return;
                player.Send(new CashShopHistoryPacket(
                    headerWord,
                    errorCode: 0,
                    year,
                    month,
                    Math.Min(clampedPage, history.TotalPages),
                    history.TotalPages,
                    accountUid,
                    history.Entries));
            });
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Falha ao consultar histórico Cash Shop da conta {AccountUid}", accountUid);
            world.Post(() =>
            {
                if (player.IsOnline)
                    player.Send(new CashShopHistoryPacket(headerWord, errorCode: 0, year, month, clampedPage, 1, accountUid, []));
            });
        }
    }

    /// <summary>✅ Lista o Cash Depository da conta (<c>0x00DE</c>: 24 cards + 24 items).</summary>
    public async Task ListDepositoryAsync(Player player, CancellationToken ct = default)
    {
        if (player.InBattle) return;
        uint accountUid = player.Account.Uid;
        try
        {
            var rows = await store.ListDepositoryAsync(accountUid, ct);
            world.Post(() =>
            {
                if (!player.IsOnline) return;
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
        catch (Exception e)
        {
            log.LogWarning(e, "Falha ao listar Cash Depository da conta {AccountUid}", accountUid);
        }
    }

    /// <summary>
    /// ✅ Retira um Card (<c>kind=1</c>) ou Item (<c>kind=2</c>) do Cash Depository para a mochila do personagem (<c>0x00DD</c>).
    /// Faz rollback no banco se a mochila não tiver espaço ou o jogador ficar indisponível.
    /// </summary>
    public async Task WithdrawDepositoryAsync(Player player, CashShopItemKind kind, uint uid, CancellationToken ct = default)
    {
        if (player.InBattle || uid == 0 || kind is not CashShopItemKind.Card and not CashShopItemKind.Item)
        {
            player.Send(new CashDepositoryWithdrawPacket(kind, null, CashDepositoryWithdrawResult.Failed));
            return;
        }

        uint accountUid = player.Account.Uid;
        CashDepositoryRow? taken;
        try
        {
            taken = await store.TakeDepositoryItemAsync(accountUid, kind, uid, ct);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Falha ao retirar uid={Uid} do Cash Depository da conta {AccountUid}", uid, accountUid);
            world.Post(() =>
            {
                if (player.IsOnline)
                    player.Send(new CashDepositoryWithdrawPacket(kind, null, CashDepositoryWithdrawResult.Failed));
            });
            return;
        }

        if (taken is null)
        {
            world.Post(() =>
            {
                if (player.IsOnline)
                    player.Send(new CashDepositoryWithdrawPacket(kind, null, CashDepositoryWithdrawResult.NotFound));
            });
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Post(() =>
        {
            if (!player.IsOnline || player.InBattle)
            {
                tcs.SetResult(false);
                return;
            }

            var inv = player.Character.Inventory;
            var bag = taken.Kind == CashShopItemKind.Card ? inv.Battle : inv.Main;
            if (bag.Room(taken.ItemId) < taken.Amount)
            {
                player.Send(new CashDepositoryWithdrawPacket(taken.Kind, ToInventoryItem(taken), CashDepositoryWithdrawResult.InventoryFull));
                tcs.SetResult(false);
                return;
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
                tcs.SetResult(false);
                return;
            }

            player.RequestImmediateSave();
            player.Send(new CashDepositoryWithdrawPacket(taken.Kind, ToInventoryItem(taken), CashDepositoryWithdrawResult.Ok));
            if (taken.Kind == CashShopItemKind.Card)
                player.Send(new BattleItemReceivePacket(added));
            else
                player.Send(new ItemReceivePacket(added));
            tcs.SetResult(true);
        });

        bool delivered = await tcs.Task;
        if (!delivered)
        {
            try
            {
                await store.RestoreDepositoryItemAsync(accountUid, taken, CancellationToken.None);
            }
            catch (Exception e)
            {
                log.LogError(e, "CRÍTICO: falha ao restaurar item uid={Uid} no Cash Depository da conta {AccountUid}", taken.Uid, accountUid);
            }
        }
    }

    /// <summary>Credita Cash (ou bônus) na conta — usado pelo comando GM <c>/cash</c>.</summary>
    public async Task<CashBalance?> GrantCashAsync(uint accountUid, uint cashDelta, uint bonusCashDelta = 0, CancellationToken ct = default) =>
        await store.AddCashAsync(accountUid, cashDelta, bonusCashDelta, ct);

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
