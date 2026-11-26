using DRPG.Server.Data.GameData;
using DRPG.Server.Data.Repositories;
using DRPG.Server.Game.Economy;
using DRPG.Server.Game.Entities;
using DRPG.Server.Game.Items;
using DRPG.Server.Network.Packets.Map;

namespace DRPG.Server.Game.Shop;

/// <summary>
/// Compra e venda nas lojas de NPC (<c>Resources/Item/NpcShop.xml</c>), iguais para cards (0x51) e itens (0x53). O
/// preço vem da loja e da moeda dela (<see cref="CurrencyKind"/>), o pagamento é pela <see cref="Wallet"/> e a entrega
/// pelo <see cref="ItemService"/>. Thread do mundo. Quem chama já conferiu a distância do dono
/// (<c>InteractionGuard.CanUseShop</c>). Pedido inválido é ignorado sem resposta, como o oficial.
/// </summary>
public sealed class NpcShopService(GameData data, ItemService items)
{
    /// <summary>Teto de quantidade por pedido.</summary>
    public const uint MaxTradeQuantity = 999;

    public void Buy(Player player, NpcShopKind kind, uint shopId, uint id, uint quantity)
    {
        if (quantity is 0 or > MaxTradeQuantity) return;
        if (data.FindNpcShop(shopId, kind) is not { } shop || shop.FindEntry(id) is not { } entry) return;
        if (Goods.Find(data, kind, id) is not { } goods) return;
        // não empilháveis: um por vez
        if (!goods.IsCard && goods.Item!.MaxStack <= 1 && quantity > 1) return;

        long unitPrice = PriceOf(shop, entry, goods);
        long total = unitPrice * quantity;
        if (unitPrice == 0 || total > Wallet.Max(shop.Currency)) return;

        if (shop.DeliverTo == NpcShopDelivery.ShopDepository && !goods.IsCard)
            BuyToDepository(player, shop, goods.Item!, quantity, total);
        else
            BuyToBag(player, shop, goods, quantity, total);
    }

    public void Sell(Player player, NpcShopKind kind, uint shopId, ItemPlace place, int col, int row, uint uid, uint id,
        uint quantity)
    {
        if (quantity is 0 or > MaxTradeQuantity || place != ItemPlace.Inventory) return;
        if (col < 0 || col >= Inventory.Columns || row < 0) return;
        if (Goods.Find(data, kind, id) is not { } goods) return;

        var reason = new EconomyReason(EconomySource.NpcShop, "sell", $"shop {shopId} uid {uid}");
        var bag = Bag(player, goods);
        if (items.TakeAt(player, bag, row * Inventory.Columns + col, uid, id, quantity, reason) is not { } left) return;

        player.Character.Wallet.Credit(CurrencyKind.Bits, (long)goods.SellPrice * quantity, reason);
        player.RequestImmediateSave();
        SendTrade(player, goods, NpcShopAction.Sell, left.Amount > 0 ? left : null, quantity, col, row, shopId, coins: 0);
    }

    private void BuyToBag(Player player, NpcShop shop, Goods goods, uint quantity, long total)
    {
        var wallet = player.Character.Wallet;
        var reason = new EconomyReason(EconomySource.NpcShop, "buy", $"shop {shop.ShopId}");
        var bag = Bag(player, goods);
        if (!bag.CanFit([(goods.Id, quantity)]) || !wallet.TrySpend(shop.Currency, total, reason)) return;

        var grant = new ItemGrant(goods.Id, quantity);
        bool ok = goods.IsCard
            ? items.GiveCards(player, [grant], reason, refresh: false, beforeNotify: Confirm)
            : items.GiveItems(player, [grant], reason, refresh: false, beforeNotify: Confirm);
        if (!ok)
        {
            wallet.Credit(shop.Currency, total, reason with { Action = "refund" });
            return;
        }
        player.RequestImmediateSave();
        SendCurrency(player, shop);

        // ✅ a resposta do 0x51/0x53 vem antes do recebimento (0xC8/0xC9)
        void Confirm(IReadOnlyList<InventoryItem> added) =>
            SendTrade(player, goods, NpcShopActions.BuyWith(shop.Currency), added[0], quantity,
                added[0].Column, added[0].Row, shop.ShopId, wallet.Coins);
    }

    private void BuyToDepository(Player player, NpcShop shop, ItemInfo info, uint quantity, long total)
    {
        var wallet = player.Character.Wallet;
        var reason = new EconomyReason(EconomySource.ShopDepository, "buy", $"shop {shop.ShopId}");
        if (!wallet.TrySpend(shop.Currency, total, reason)) return;

        // conta (depósito) e personagem (moeda gasta) gravam juntos; se não gravar, a moeda volta
        items.SendToAccountStorage(player, ItemService.StorageEntries(player, CashShopItemKind.Item, info.Id, quantity), reason,
            undo: () => wallet.Credit(shop.Currency, total, reason with { Action = "refund" }),
            done: result =>
            {
                if (result != StorageSendResult.Ok)
                {
                    player.SendNotice("[Shop]", result == StorageSendResult.Full
                        ? "Shop Depository cheio. Retire itens antes de comprar."
                        : "Não foi possível concluir a compra.");
                    return;
                }
                // ✅ sem 0xC9: o cliente mostra o item da resposta e avisa que foi para o Shop Depository
                var preview = new InventoryItem { Uid = 0, ItemId = info.Id, Info = info, Amount = quantity };
                player.Send(new ItemShopTradePacket(NpcShopActions.BuyWith(shop.Currency), preview, quantity, wallet.Bits,
                    ItemPlace.Inventory, 0, 0, shop.ShopId, wallet.Coins));
                SendCurrency(player, shop);
            });
    }

    /// <summary>
    /// Preço unitário na moeda da loja (0 = não vende). Card só tem preço em Bits. A tabela do cliente não tem preço em
    /// Medals: loja de Medals traz o <c>Price</c> de cada entrada (a carga recusa entrada sem ele).
    /// </summary>
    private static long PriceOf(NpcShop shop, NpcShopEntry entry, Goods goods)
    {
        if (entry.PriceOverride is { } price) return price;
        if (goods.IsCard) return shop.Currency == CurrencyKind.Bits ? goods.Card!.BuyPrice : 0;
        var info = goods.Item!;
        return shop.Currency switch
        {
            CurrencyKind.Bits => info.BuyPrice,
            CurrencyKind.Coins => info.CoinPrice,
            CurrencyKind.LadderPoints => info.LadderPrice,
            _ => 0,
        };
    }

    private static Bag Bag(Player player, Goods goods) =>
        goods.IsCard ? player.Character.Inventory.Battle : player.Character.Inventory.Main;

    private static void SendTrade(Player player, Goods goods, NpcShopAction action, InventoryItem? item, uint quantity,
        int col, int row, uint shopId, ushort coins)
    {
        double bits = player.Character.Wallet.Bits;
        if (goods.IsCard)
            player.Send(new CardShopTradePacket(action, item, quantity, bits, ItemPlace.Inventory, col, row, shopId));
        else
            player.Send(new ItemShopTradePacket(action, item, quantity, bits, ItemPlace.Inventory, col, row, shopId, coins));
    }

    /// <summary>✅ Medals e Coins não estão no 0x53: o saldo novo vai no 0xCB.</summary>
    private static void SendCurrency(Player player, NpcShop shop)
    {
        if (shop.Currency is CurrencyKind.Medals or CurrencyKind.Coins)
            player.Send(new CurrencyUpdatePacket(player.Character));
    }

    /// <summary>O que uma loja vende: um card ou um item.</summary>
    private sealed record Goods(uint Id, CardInfo? Card, ItemInfo? Item)
    {
        public bool IsCard => Card is not null;
        public uint SellPrice => Card?.SellPrice ?? Item!.SellPrice;

        public static Goods? Find(GameData data, NpcShopKind kind, uint id) => kind == NpcShopKind.Card
            ? data.FindCard(id) is { } card ? new(id, card, null) : null
            : data.FindItem(id) is { } item ? new(id, null, item) : null;
    }
}
