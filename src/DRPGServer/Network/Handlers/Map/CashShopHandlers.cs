using DRPGServer.Data.GameData;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Shop;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Handlers;

/// <summary>✅ Map 0x0159 (C2S 78 B): abrir Cash Shop / consultar saldo de Cash.</summary>
[PacketHandler(MapOpcode.CashShopOpen)]
public sealed class CashShopOpenHandler(CashShopService cashShop) : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        uint headerWord = packet.Remaining >= 4 ? packet.ReadU32() : 0u;
        _ = cashShop.OpenShopAsync(player, headerWord);
    }
}

/// <summary>✅ Map 0x0160 (C2S 70 B): comprar produto no Cash Shop.</summary>
[PacketHandler(MapOpcode.CashShopBuy)]
public sealed class CashShopBuyHandler(CashShopService cashShop) : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        if (player.InBattle || packet.Remaining < 32) return;
        uint headerWord = packet.ReadU32();  // +0x00
        packet.Skip(20);                     // +0x04..+0x17 (errorCode, action, pad, accountNo)
        uint productId = packet.ReadU32();   // +0x18
        uint quantity = packet.ReadU32();    // +0x1C
        // +0x20 unitPrice é ignorado (servidor sempre calcula pelo catálogo)

        _ = cashShop.BuyProductAsync(player, headerWord, productId, quantity);
    }
}

/// <summary>✅ Map 0x0161 (C2S 1006 B): consultar histórico de compras do Cash Shop.</summary>
[PacketHandler(MapOpcode.CashShopHistory)]
public sealed class CashShopHistoryHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        if (packet.Remaining < 20) return;
        uint headerWord = packet.ReadU32();  // +0x00
        packet.Skip(8);                      // +0x04..+0x0B
        ushort year = packet.ReadU16();      // +0x0C
        ushort month = packet.ReadU16();     // +0x0E
        ushort page = packet.ReadU16();      // +0x10

        player.Send(new CashShopHistoryPacket(
            headerWord,
            errorCode: 0,
            year,
            month,
            Math.Max((ushort)1, page),
            totalPages: 1,
            player.Account.Uid));
    }
}

/// <summary>✅ Map 0x00CA (C2S 117 B): autenticar e abrir a janela do Cash Depository (<c>0x46</c>).</summary>
[PacketHandler(MapOpcode.CashDepositoryAuth)]
public sealed class CashDepositoryAuthHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        if (player.InBattle || packet.Remaining < 103) return;
        string username = packet.ReadFixedString(21);      // +0x00..+0x14
        string passwordHash = packet.ReadFixedString(80);  // +0x15..+0x64
        packet.ReadU8();                                   // +0x65 (0 no C2S)
        byte action = packet.ReadU8();                     // +0x66 (1 = abrir)

        // Aceita somente para o próprio usuário autenticado
        bool validUser = string.IsNullOrEmpty(username)
            || string.Equals(username, player.Account.Username, StringComparison.OrdinalIgnoreCase);
        byte result = validUser ? (byte)1 : (byte)0;
        player.Send(new CashDepositoryAuthPacket(player.Account.Username, passwordHash, result, action));
    }
}

/// <summary>✅ Map 0x00DE (C2S 4434 B): listar os 24 cards e 24 itens no Cash Depository da conta.</summary>
[PacketHandler(MapOpcode.CashDepositoryList)]
public sealed class CashDepositoryListHandler(CashShopService cashShop) : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        if (player.InBattle) return;
        _ = cashShop.ListDepositoryAsync(player);
    }
}

/// <summary>✅ Map 0x00DD (C2S 206 B): retirar Card (1) ou Item (2) do Cash Depository para a mochila.</summary>
[PacketHandler(MapOpcode.CashDepositoryWithdraw)]
public sealed class CashDepositoryWithdrawHandler(CashShopService cashShop) : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        if (player.InBattle || packet.Remaining < 188) return;
        uint kindRaw = packet.ReadU32();     // +0x00 (1 = Card, 2 = Item)
        uint cardUid = packet.ReadU32();     // +0x04 (início do CardRecord de 76 B)
        packet.Skip(72);                     // +0x08..+0x4F (restante do CardRecord)
        uint itemUid = packet.ReadU32();     // +0x50 (início do ItemRecord de 108 B)

        if (kindRaw is not (uint)CashShopItemKind.Card and not (uint)CashShopItemKind.Item)
        {
            player.Send(new CashDepositoryWithdrawPacket(CashShopItemKind.Item, null, CashDepositoryWithdrawResult.Failed));
            return;
        }

        var kind = (CashShopItemKind)kindRaw;
        uint uid = kind == CashShopItemKind.Card ? cardUid : itemUid;
        _ = cashShop.WithdrawDepositoryAsync(player, kind, uid);
    }
}
