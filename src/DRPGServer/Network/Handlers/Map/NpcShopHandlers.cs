using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging;

namespace DRPGServer.Network.Handlers;

/// <summary>✅ Map 0x0050 (C2S 18 B): abrir lista de cards de uma loja de NPC.</summary>
[PacketHandler(MapOpcode.CardShopList)]
public sealed class CardShopListHandler(GameData data) : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        if (player.InBattle || packet.Remaining < 4) return;
        uint shopId = packet.ReadU32();
        if (data.FindNpcShop(shopId, NpcShopKind.Card) is not { } shop) return;

        player.Send(new CardShopListPacket(shop, data));
    }
}

/// <summary>✅ Map 0x0052 (C2S 18 B): abrir lista de itens de uma loja de NPC.</summary>
[PacketHandler(MapOpcode.ItemShopList)]
public sealed class ItemShopListHandler(GameData data) : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        if (player.InBattle || packet.Remaining < 4) return;
        uint shopId = packet.ReadU32();
        if (data.FindNpcShop(shopId, NpcShopKind.Item) is not { } shop) return;

        player.Send(new ItemShopListPacket(shop, data));
    }
}

/// <summary>✅ Map 0x0051 (C2S 126 B): comprar (1) ou vender (2) card em loja de NPC.</summary>
[PacketHandler(MapOpcode.CardShopTrade)]
public sealed class CardShopTradeHandler(GameData data) : PlayerHandler
{
    private const uint MaxTradeQuantity = 999;

    protected override void Handle(Player player, PacketReader packet)
    {
        if (player.InBattle || packet.Remaining < 112) return;

        uint actionRaw = packet.ReadU32();   // +0x00
        uint uid = packet.ReadU32();         // +0x04 (CardRecord +0)
        uint cardId = packet.ReadU32();      // +0x08 (CardRecord +4)
        packet.Skip(68);                     // +0x0C..+0x4F (restante do CardRecord de 76 B)
        uint quantity = packet.ReadU32();    // +0x50
        packet.Skip(12);                     // +0x54..+0x5F (pad + double bits)
        var place = (ItemPlace)packet.ReadU16(); // +0x60
        packet.Skip(2);                      // +0x62
        int col = packet.ReadI32();          // +0x64
        int row = packet.ReadI32();          // +0x68
        uint shopId = packet.ReadU32();      // +0x6C

        if (quantity is 0 or > MaxTradeQuantity) return;

        switch ((NpcShopAction)actionRaw)
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

        if (!player.Character.TrySpendBits(totalCost)) return;
        var added = bag.Add(cardId, quantity);
        if (added.Count == 0)
        {
            player.Character.AddBits(totalCost);
            return;
        }

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

        item.Amount -= quantity;
        if (item.Amount == 0)
            bag.Destroy(slot);

        player.Character.AddBits(totalGain);
        player.RequestImmediateSave();

        player.Send(new CardShopTradePacket(
            NpcShopAction.Sell, item.Amount > 0 ? item : null, quantity, player.Character.Bits,
            ItemPlace.Inventory, col, row, shopId));
    }
}

/// <summary>✅ Map 0x0053 (C2S 158 B): comprar (1) ou vender (2) item em loja de NPC.</summary>
[PacketHandler(MapOpcode.ItemShopTrade)]
public sealed class ItemShopTradeHandler(
    GameData data,
    ICashStore? cashStore = null,
    GameWorld? world = null,
    ILogger<ItemShopTradeHandler>? log = null) : PlayerHandler
{
    private const uint MaxTradeQuantity = 999;

    protected override void Handle(Player player, PacketReader packet)
    {
        if (player.InBattle || packet.Remaining < 144) return;

        uint actionRaw = packet.ReadU32();   // +0x00
        uint uid = packet.ReadU32();         // +0x04 (ItemRecord +0)
        uint itemId = packet.ReadU32();      // +0x08 (ItemRecord +4)
        packet.Skip(100);                    // +0x0C..+0x6F (restante do ItemRecord de 108 B)
        uint quantity = packet.ReadU32();    // +0x70
        packet.Skip(14);                     // +0x74..+0x81 (pad + double bits + u16 0)
        var place = (ItemPlace)packet.ReadU16(); // +0x82
        int col = packet.ReadI32();          // +0x84
        int row = packet.ReadI32();          // +0x88
        uint shopId = packet.ReadU32();      // +0x8C

        if (quantity is 0 or > MaxTradeQuantity) return;

        // ✅ DigimonRPG.bin @ 0x00ADD1CB: em C2S 0x0053 o cliente sempre envia action=1 (Buy) para qualquer moeda
        // (Bits, Medals, Coins ou LadderPoints) e action=2 (Sell). O servidor determina a moeda pela loja (shopId)
        // e responde no S2C 0x0053 com action = (NpcShopAction)shop.Currency (1, 3, 4 ou 5).
        switch ((NpcShopAction)actionRaw)
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
            HandleBuyToShopDepository(player, shop, info, quantity, unitPrice, totalCostLong);
            return;
        }

        var bag = character.Inventory.Main;
        if (bag.Room(itemId) < quantity) return;

        if (!TrySpendCurrency(character, shop.Currency, unitPrice, quantity, totalCostLong))
            return;

        var added = bag.Add(itemId, quantity);
        if (added.Count == 0)
        {
            RefundCurrency(character, shop.Currency, unitPrice, quantity, totalCostLong);
            return;
        }

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

    /// <summary>
    /// ✅ Lojas com <c>DeliverTo="ShopDepository"</c> (ex.: loja 5050 de Coins, <c>DigimonRPG.bin @ 0x00AEEE49..0x00AEF199</c>):
    /// debita a moeda na thread do mundo, deposita em <c>cash_depository_item</c> e envia <c>S2C 0x0053</c> sem adicionar
    /// à mochila nem enviar <c>0x00C9</c> (o cliente exibe "The purchased item has been delivered to the Shop Depository").
    /// </summary>
    private void HandleBuyToShopDepository(
        Player player, NpcShop shop, ItemInfo info, uint quantity, uint unitPrice, ulong totalCostLong)
    {
        if (cashStore is null) return;

        var character = player.Character;
        if (!TrySpendCurrency(character, shop.Currency, unitPrice, quantity, totalCostLong))
            return;

        uint expiresAt = info.IsTimed
            ? (uint)Math.Clamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + info.DurationSeconds, 1, uint.MaxValue)
            : 0u;
        uint maxStack = info.IsTimed ? 1u : Math.Max(1u, info.MaxStack);
        var entries = new List<(CashShopItemKind Kind, uint ItemId, uint Amount, uint ExpiresAt)>();
        uint remaining = quantity;
        while (remaining > 0)
        {
            uint chunk = Math.Min(remaining, maxStack);
            entries.Add((CashShopItemKind.Item, info.Id, chunk, expiresAt));
            remaining -= chunk;
        }

        _ = DeliverToShopDepositoryAsync(player, shop, info, quantity, unitPrice, totalCostLong, expiresAt, entries);
    }

    private async Task DeliverToShopDepositoryAsync(
        Player player,
        NpcShop shop,
        ItemInfo info,
        uint quantity,
        uint unitPrice,
        ulong totalCostLong,
        uint expiresAt,
        IReadOnlyList<(CashShopItemKind Kind, uint ItemId, uint Amount, uint ExpiresAt)> entries)
    {
        bool deposited = false;
        try
        {
            deposited = await cashStore!.DepositAsync(player.Account.Uid, entries, CancellationToken.None);
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "Falha ao depositar item {ItemId} da loja {ShopId} no Shop Depository da conta {AccountUid}",
                info.Id, shop.ShopId, player.Account.Uid);
        }

        void ApplyResult()
        {
            var character = player.Character;
            if (!deposited)
            {
                RefundCurrency(character, shop.Currency, unitPrice, quantity, totalCostLong);
                player.RequestImmediateSave();
                if (player.IsOnline)
                    player.SendNotice("[Shop]", "Shop Depository cheio. Retire itens antes de comprar.");
                return;
            }

            player.RequestImmediateSave();
            if (!player.IsOnline) return;

            var preview = new InventoryItem
            {
                Uid = 0,
                ItemId = info.Id,
                Info = info,
                Amount = quantity,
                ExpiresAt = expiresAt,
            };
            var responseAction = (NpcShopAction)(uint)shop.Currency;
            player.Send(new ItemShopTradePacket(
                responseAction, preview, quantity, character.Bits,
                ItemPlace.Inventory, 0, 0, shop.ShopId, character.Coins));
            if (shop.Currency is NpcShopCurrency.Medals or NpcShopCurrency.Coins)
                player.Send(new CurrencyUpdatePacket(character));
        }

        if (world is not null)
            world.Post(ApplyResult);
        else
            ApplyResult();
    }

    private static bool TrySpendCurrency(Character character, NpcShopCurrency currency, uint unitPrice, uint quantity, ulong totalCostLong) =>
        currency switch
        {
            NpcShopCurrency.Bits => character.TrySpendBits((double)unitPrice * quantity),
            NpcShopCurrency.Medals => character.TrySpendMedals((uint)totalCostLong),
            NpcShopCurrency.Coins => character.TrySpendCoins((uint)totalCostLong),
            NpcShopCurrency.LadderPoints => character.TrySpendLadderPoints((uint)totalCostLong),
            _ => false,
        };

    private static void RefundCurrency(Character character, NpcShopCurrency currency, uint unitPrice, uint quantity, ulong totalCostLong)
    {
        switch (currency)
        {
            case NpcShopCurrency.Bits: character.AddBits((double)unitPrice * quantity); break;
            case NpcShopCurrency.Medals: character.AddMedals((uint)totalCostLong); break;
            case NpcShopCurrency.Coins: character.AddCoins((uint)totalCostLong); break;
            case NpcShopCurrency.LadderPoints: character.AddLadderPoints((uint)totalCostLong); break;
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
        if (item is null || item.Uid != uid || item.ItemId != itemId || item.IsExpired()) return;
        if (item.Amount < quantity) return;
        if (data.FindItem(itemId) is not { } info) return;

        uint sellPrice = info.SellPrice;
        double totalGain = (double)sellPrice * quantity;

        item.Amount -= quantity;
        if (item.Amount == 0)
            bag.Destroy(slot);

        player.Character.AddBits(totalGain);
        player.RequestImmediateSave();

        player.Send(new ItemShopTradePacket(
            NpcShopAction.Sell, item.Amount > 0 ? item : null, quantity, player.Character.Bits,
            ItemPlace.Inventory, col, row, shopId));
    }
}
