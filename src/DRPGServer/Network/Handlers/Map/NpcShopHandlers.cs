using DRPGServer.Data.GameData;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Shop;
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
public sealed class CardShopTradeHandler(NpcShopService shops, InteractionGuard guard) : PlayerHandler<CardShopTradeRequest>
{
    protected override void Handle(Player player, CardShopTradeRequest request)
    {
        var (action, uid, cardId, quantity, place, col, row, shopId) = request;
        if (!guard.CanUseShop(player, shopId)) return;

        if (action == NpcShopAction.Buy)
            shops.Buy(player, NpcShopKind.Card, shopId, cardId, quantity);
        else if (action == NpcShopAction.Sell)
            shops.Sell(player, NpcShopKind.Card, shopId, place, col, row, uid, cardId, quantity);
    }
}

/// <summary>
/// ✅ Map 0x0053 (C2S 158 B): comprar (1) ou vender (2) item em loja de NPC. O cliente manda <c>action = 1</c> para
/// qualquer moeda (<c>DigimonRPG.bin @ 0x00ADD1CB</c>); a moeda é a da loja.
/// </summary>
[PacketHandler(MapOpcode.ItemShopTrade)]
[RequiresState(PlayerStates.Idle)]
public sealed class ItemShopTradeHandler(NpcShopService shops, InteractionGuard guard) : PlayerHandler<ItemShopTradeRequest>
{
    protected override void Handle(Player player, ItemShopTradeRequest request)
    {
        var (action, uid, itemId, quantity, place, col, row, shopId) = request;
        if (!guard.CanUseShop(player, shopId)) return;

        if (action == NpcShopAction.Buy)
            shops.Buy(player, NpcShopKind.Item, shopId, itemId, quantity);
        else if (action == NpcShopAction.Sell)
            shops.Sell(player, NpcShopKind.Item, shopId, place, col, row, uid, itemId, quantity);
    }
}
