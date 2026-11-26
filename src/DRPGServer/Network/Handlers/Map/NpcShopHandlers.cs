using DRPGServer.Data.GameData;
using DRPGServer.Game.Economy;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Items;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;

namespace DRPGServer.Network.Handlers;

/// <summary>✅ Map 0x0050 (C2S 18 B): abrir lista de cards de uma loja de NPC.</summary>
[PacketHandler(MapOpcode.CardShopList)]
[RequiresState(PlayerStates.Idle)]
public sealed class CardShopListHandler(GameData data, InteractionGuard guard) : PlayerHandler<NpcShopListRequest>
{
    protected override void Handle(Player player, NpcShopListRequest request)
    {
        if (data.FindNpcShop(request.ShopId, NpcShopKind.Card) is not { } shop) return;
        if (!guard.CanUseShop(player, shop.ShopId)) return;

        player.Send(new CardShopListPacket(shop, data));
    }
}

/// <summary>✅ Map 0x0052 (C2S 18 B): abrir lista de itens de uma loja de NPC.</summary>
[PacketHandler(MapOpcode.ItemShopList)]
[RequiresState(PlayerStates.Idle)]
public sealed class ItemShopListHandler(GameData data, InteractionGuard guard) : PlayerHandler<NpcShopListRequest>
{
    protected override void Handle(Player player, NpcShopListRequest request)
    {
        if (data.FindNpcShop(request.ShopId, NpcShopKind.Item) is not { } shop) return;
        if (!guard.CanUseShop(player, shop.ShopId)) return;

        player.Send(new ItemShopListPacket(shop, data, player.Character.Inventory.Clock.UnixNow));
    }
}

/// <summary>✅ Map 0x0051 (C2S 126 B): comprar (1) ou vender (2) card em loja de NPC.</summary>
[PacketHandler(MapOpcode.CardShopTrade)]
[RequiresState(PlayerStates.Idle)]
public sealed class CardShopTradeHandler(GameData data, InteractionGuard guard) : PlayerHandler<CardShopTradeRequest>
{
    private const uint MaxTradeQuantity = 999;

    protected override void Handle(Player player, CardShopTradeRequest request)
    {
        var (action, uid, cardId, quantity, place, col, row, shopId) = request;
        if (!guard.CanUseShop(player, shopId)) return;
        if (quantity is 0 or > MaxTradeQuantity) return;

        switch (action)
        {
            case NpcShopAction.Buy:
                HandleBuy(player, shopId, cardId, quantity);
                break;
            case NpcShopAction.Sell:
                HandleSell(player, shopId, place, col, row, uid, cardId, quantity);
                break;
        }
    }

    private void HandleBuy(Player player, uint shopId, uint cardId, uint quantity)
    {
        if (data.FindNpcShop(shopId, NpcShopKind.Card) is not { } shop) return;
        if (shop.FindEntry(cardId) is not { } entry) return;
        if (data.FindCard(cardId) is not { } card) return;

        uint unitPrice = entry.PriceOverride ?? card.BuyPrice;
        double totalCost = (double)unitPrice * quantity;
        if (player.Character.Bits < totalCost) return;

        var bag = player.Character.Inventory.Battle;
        if (bag.Room(cardId) < quantity) return;

        var reason = new EconomyReason(EconomySource.NpcShop, "buy", $"shop {shopId}");
        if (!player.Character.TrySpendBits(totalCost, reason)) return;
        var added = bag.Add(cardId, quantity);
        if (added.Count == 0)
        {
            player.Character.AddBits(totalCost, reason with { Action = "refund" });
            return;
        }
        player.Character.Economy.RecordItems(reason, [(cardId, quantity)], cards: true);

        player.RequestImmediateSave();
        var primary = added[0];
        player.Send(new CardShopTradePacket(
            NpcShopAction.Buy, primary, quantity, player.Character.Bits,
            ItemPlace.Inventory, primary.Column, primary.Row, shopId));
        player.Send(new BattleItemReceivePacket(added));
    }

    private void HandleSell(Player player, uint shopId, ItemPlace place, int col, int row, uint uid, uint cardId, uint quantity)
    {
        if (place != ItemPlace.Inventory) return;
        if (col < 0 || col >= Inventory.Columns || row < 0) return;
        int slot = row * Inventory.Columns + col;
        var bag = player.Character.Inventory.Battle;
        if (slot < 0 || slot >= bag.Capacity) return;

        var item = bag[slot];
        if (item is null || item.Uid != uid || item.ItemId != cardId || item.Amount < quantity) return;
        if (data.FindCard(cardId) is not { } card) return;

        uint sellPrice = card.SellPrice;
        double totalGain = (double)sellPrice * quantity;
        var reason = new EconomyReason(EconomySource.NpcShop, "sell", $"shop {shopId} uid {uid}");

        item.Amount -= quantity;
        if (item.Amount == 0)
            bag.Destroy(slot);

        player.Character.Economy.RecordItems(reason, [(cardId, quantity)], cards: true, sign: -1);
        player.Character.AddBits(totalGain, reason);
        player.RequestImmediateSave();

        player.Send(new CardShopTradePacket(
            NpcShopAction.Sell, item.Amount > 0 ? item : null, quantity, player.Character.Bits,
            ItemPlace.Inventory, col, row, shopId));
    }
}

/// <summary>✅ Map 0x0053 (C2S 158 B): comprar (1) ou vender (2) item em loja de NPC.</summary>
[PacketHandler(MapOpcode.ItemShopTrade)]
[RequiresState(PlayerStates.Idle)]
public sealed class ItemShopTradeHandler(GameData data, ItemService items, InteractionGuard guard) : PlayerHandler<ItemShopTradeRequest>
{
    private const uint MaxTradeQuantity = 999;

    protected override void Handle(Player player, ItemShopTradeRequest request)
    {
        var (action, uid, itemId, quantity, place, col, row, shopId) = request;
        if (quantity is 0 or > MaxTradeQuantity) return;
        if (!guard.CanUseShop(player, shopId)) return;

        // ✅ DigimonRPG.bin @ 0x00ADD1CB: em C2S 0x0053 o cliente sempre envia action=1 (Buy) para qualquer moeda
        // (Bits, Medals, Coins ou LadderPoints) e action=2 (Sell). O servidor determina a moeda pela loja (shopId)
        // e responde no S2C 0x0053 com action = (NpcShopAction)shop.Currency (1, 3, 4 ou 5).
        switch (action)
        {
            case NpcShopAction.Buy:
                HandleBuy(player, shopId, itemId, quantity);
                break;
            case NpcShopAction.Sell:
                HandleSell(player, shopId, place, col, row, uid, itemId, quantity);
                break;
        }
    }

    private void HandleBuy(Player player, uint shopId, uint itemId, uint quantity)
    {
        if (data.FindNpcShop(shopId, NpcShopKind.Item) is not { } shop) return;
        if (shop.FindEntry(itemId) is not { } entry) return;
        if (data.FindItem(itemId) is not { } info) return;

        // Itens não empilháveis (MaxStack == 1) comprados 1 por vez
        if (info.MaxStack <= 1 && quantity > 1) return;

        var character = player.Character;
        uint unitPrice = shop.Currency switch
        {
            NpcShopCurrency.Bits => entry.PriceOverride ?? info.BuyPrice,
            NpcShopCurrency.Medals => entry.PriceOverride ?? (info.Type == 7 ? 32000u : 9999u),
            NpcShopCurrency.Coins => entry.PriceOverride ?? info.Col16,
            NpcShopCurrency.LadderPoints => entry.PriceOverride ?? info.Col17,
            _ => 0u,
        };
        if (unitPrice == 0) return;

        ulong totalCostLong = (ulong)unitPrice * quantity;
        if (totalCostLong > uint.MaxValue && shop.Currency != NpcShopCurrency.Bits) return;

        if (shop.DeliverTo == NpcShopDelivery.ShopDepository)
        {
            var depositReason = new EconomyReason(EconomySource.ShopDepository, "buy", $"shop {shopId}");
            if (!TrySpendCurrency(character, shop.Currency, unitPrice, quantity, totalCostLong, depositReason))
                return;

            // conta (depósito) e personagem (moeda gasta) gravam juntos; se não gravar, a moeda volta
            items.SendToAccountStorage(player, ItemService.StorageEntries(player, CashShopItemKind.Item, info.Id, quantity),
                depositReason,
                undo: () => RefundCurrency(character, shop.Currency, unitPrice, quantity, totalCostLong, depositReason with { Action = "refund" }),
                done: result =>
                {
                    if (result != StorageSendResult.Ok)
                    {
                        player.SendNotice("[Shop]", result == StorageSendResult.Full
                            ? "Shop Depository cheio. Retire itens antes de comprar."
                            : "Não foi possível concluir a compra.");
                        return;
                    }

                    var preview = new InventoryItem { Uid = 0, ItemId = info.Id, Info = info, Amount = quantity };
                    player.Send(new ItemShopTradePacket(
                        (NpcShopAction)(uint)shop.Currency, preview, quantity, character.Bits,
                        ItemPlace.Inventory, 0, 0, shop.ShopId, character.Coins));
                    if (shop.Currency is NpcShopCurrency.Medals or NpcShopCurrency.Coins)
                        player.Send(new CurrencyUpdatePacket(character));
                });
            return;
        }

        var bag = character.Inventory.Main;
        if (bag.Room(itemId) < quantity) return;

        var reason = new EconomyReason(EconomySource.NpcShop, "buy", $"shop {shopId}");
        if (!TrySpendCurrency(character, shop.Currency, unitPrice, quantity, totalCostLong, reason))
            return;

        var added = bag.Add(itemId, quantity);
        if (added.Count == 0)
        {
            RefundCurrency(character, shop.Currency, unitPrice, quantity, totalCostLong, reason with { Action = "refund" });
            return;
        }
        character.Economy.RecordItems(reason, [(itemId, quantity)], cards: false);

        player.RequestImmediateSave();
        var primary = added[0];
        var responseAction = (NpcShopAction)(uint)shop.Currency;
        player.Send(new ItemShopTradePacket(
            responseAction, primary, quantity, character.Bits,
            ItemPlace.Inventory, primary.Column, primary.Row, shopId, character.Coins));
        player.Send(new ItemReceivePacket(added));
        if (shop.Currency is NpcShopCurrency.Medals or NpcShopCurrency.Coins)
            player.Send(new CurrencyUpdatePacket(character));
    }

    private static bool TrySpendCurrency(Character character, NpcShopCurrency currency, uint unitPrice, uint quantity,
        ulong totalCostLong, EconomyReason reason) =>
        currency switch
        {
            NpcShopCurrency.Bits => character.TrySpendBits((double)unitPrice * quantity, reason),
            NpcShopCurrency.Medals => character.TrySpendMedals((uint)totalCostLong, reason),
            NpcShopCurrency.Coins => character.TrySpendCoins((uint)totalCostLong, reason),
            NpcShopCurrency.LadderPoints => character.TrySpendLadderPoints((uint)totalCostLong, reason),
            _ => false,
        };

    private static void RefundCurrency(Character character, NpcShopCurrency currency, uint unitPrice, uint quantity,
        ulong totalCostLong, EconomyReason reason)
    {
        switch (currency)
        {
            case NpcShopCurrency.Bits: character.AddBits((double)unitPrice * quantity, reason); break;
            case NpcShopCurrency.Medals: character.AddMedals((uint)totalCostLong, reason); break;
            case NpcShopCurrency.Coins: character.AddCoins((uint)totalCostLong, reason); break;
            case NpcShopCurrency.LadderPoints: character.AddLadderPoints((uint)totalCostLong, reason); break;
        }
    }

    private void HandleSell(Player player, uint shopId, ItemPlace place, int col, int row, uint uid, uint itemId, uint quantity)
    {
        if (place != ItemPlace.Inventory) return;
        if (col < 0 || col >= Inventory.Columns || row < 0) return;
        int slot = row * Inventory.Columns + col;
        var bag = player.Character.Inventory.Main;
        if (slot < 0 || slot >= bag.Capacity) return;

        var item = bag[slot];
        if (item is null || item.Uid != uid || item.ItemId != itemId || player.Character.Inventory.IsExpired(item)) return;
        if (item.Amount < quantity) return;
        if (data.FindItem(itemId) is not { } info) return;

        uint sellPrice = info.SellPrice;
        double totalGain = (double)sellPrice * quantity;
        var reason = new EconomyReason(EconomySource.NpcShop, "sell", $"shop {shopId} uid {uid}");

        item.Amount -= quantity;
        if (item.Amount == 0)
            bag.Destroy(slot);

        player.Character.Economy.RecordItems(reason, [(itemId, quantity)], cards: false, sign: -1);
        player.Character.AddBits(totalGain, reason);
        player.RequestImmediateSave();

        player.Send(new ItemShopTradePacket(
            NpcShopAction.Sell, item.Amount > 0 ? item : null, quantity, player.Character.Bits,
            ItemPlace.Inventory, col, row, shopId));
    }
}
