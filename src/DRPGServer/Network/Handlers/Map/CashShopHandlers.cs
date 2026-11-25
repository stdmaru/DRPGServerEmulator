using DRPGServer.Data.GameData;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Shop;
using DRPGServer.Network.Packets.Map;

namespace DRPGServer.Network.Handlers;

/// <summary>✅ 0x159: abrir o Cash Shop / consultar o saldo de Cash.</summary>
[PacketHandler(MapOpcode.CashShopOpen)]
[RequiresState(PlayerStates.Any)]
public sealed class CashShopOpenHandler(CashShopService cashShop) : PlayerHandler<CashShopOpenRequest>
{
    protected override void Handle(Player player, CashShopOpenRequest request) =>
        _ = cashShop.OpenShopAsync(player, request.HeaderWord);
}

/// <summary>✅ 0x160: comprar produto no Cash Shop. O preço vem sempre do catálogo.</summary>
[PacketHandler(MapOpcode.CashShopBuy)]
[RequiresState(PlayerStates.Idle)]
public sealed class CashShopBuyHandler(CashShopService cashShop) : PlayerHandler<CashShopBuyRequest>
{
    protected override void Handle(Player player, CashShopBuyRequest request) =>
        _ = cashShop.BuyProductAsync(player, request.HeaderWord, request.ProductId, request.Quantity);
}

/// <summary>✅ 0x161: histórico de compras do Cash Shop.</summary>
[PacketHandler(MapOpcode.CashShopHistory)]
[RequiresState(PlayerStates.Any)]
public sealed class CashShopHistoryHandler(CashShopService cashShop) : PlayerHandler<CashShopHistoryRequest>
{
    protected override void Handle(Player player, CashShopHistoryRequest request) =>
        _ = cashShop.GetHistoryAsync(player, request.HeaderWord, request.Year, request.Month, request.Page);
}

/// <summary>✅ 0xCA: autenticar e abrir a janela do Cash Depository (<c>0x46</c>).</summary>
[PacketHandler(MapOpcode.CashDepositoryAuth)]
[RequiresState(PlayerStates.Idle)]
public sealed class CashDepositoryAuthHandler : PlayerHandler<CashDepositoryAuthRequest>
{
    protected override void Handle(Player player, CashDepositoryAuthRequest request)
    {
        // Aceita somente para o próprio usuário autenticado
        bool validUser = string.IsNullOrEmpty(request.Username)
            || string.Equals(request.Username, player.Account.Username, StringComparison.OrdinalIgnoreCase);
        byte result = validUser ? (byte)1 : (byte)0;
        player.Send(new CashDepositoryAuthPacket(player.Account.Username, request.PasswordHash, result, request.Action));
    }
}

/// <summary>✅ 0xDE: listar os 24 cards e 24 itens do Cash Depository da conta.</summary>
[PacketHandler(MapOpcode.CashDepositoryList)]
[RequiresState(PlayerStates.Idle)]
public sealed class CashDepositoryListHandler(CashShopService cashShop) : PlayerHandler<CashDepositoryListRequest>
{
    protected override void Handle(Player player, CashDepositoryListRequest request) =>
        _ = cashShop.ListDepositoryAsync(player);
}

/// <summary>✅ 0xDD: retirar card (1) ou item (2) do Cash Depository para a mochila.</summary>
[PacketHandler(MapOpcode.CashDepositoryWithdraw)]
[RequiresState(PlayerStates.Idle)]
public sealed class CashDepositoryWithdrawHandler(CashShopService cashShop) : PlayerHandler<CashDepositoryWithdrawRequest>
{
    protected override void Handle(Player player, CashDepositoryWithdrawRequest request)
    {
        var kind = (CashShopItemKind)request.Kind;
        if (request.UidOf(kind) is not { } uid)
        {
            player.Send(new CashDepositoryWithdrawPacket(CashShopItemKind.Item, null, CashDepositoryWithdrawResult.Failed));
            return;
        }
        _ = cashShop.WithdrawDepositoryAsync(player, kind, uid);
    }
}
