using System.Buffers.Binary;
using System.Collections.Frozen;
using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Shop;
using DRPGServer.Game.Stats;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

public class CashShopTests
{
    private static (GameData Data, Player Player, Session Session, WorldHarness World, InMemoryCashStore Store, CashShopService Service) MakeEnv()
    {
        var items = new Dictionary<uint, ItemInfo>(TestSupport.Data.Items)
        {
            [718] = new ItemInfo(718, "Aura T of Deva") { Type = 27, SubType = 2, MaxStack = 1, DurationSeconds = 2_593_000 },
        };
        var cards = new Dictionary<uint, CardInfo>
        {
            [74] = new CardInfo(74, "Golden Drill X") { Type = 4, SubType = 1, MaxStack = 999 },
        };
        var products = new Dictionary<uint, CashShopProduct>
        {
            [1] = new CashShopProduct(100001, "Starter Pack", ItemDataId: 1, Price: 160, Category: 2, [
                new CashShopEntry(CashShopItemKind.Card, 74, 50),
                new CashShopEntry(CashShopItemKind.Item, 718, 1),
            ]),
        };

        var data = new GameData
        {
            Species = TestSupport.Data.Species,
            Items = items.ToFrozenDictionary(),
            Cards = cards.ToFrozenDictionary(),
            CashShopProducts = products.ToFrozenDictionary(),
            DigimonTitles = TestSupport.Data.DigimonTitles,
            Skills = TestSupport.Data.Skills,
            Evolutions = TestSupport.Data.Evolutions,
            Maps = TestSupport.Data.Maps,
            Grids = TestSupport.Data.Grids,
            DropGroups = TestSupport.Data.DropGroups,
            TamingNets = TestSupport.Data.TamingNets,
            Portals = TestSupport.Data.Portals,
            Spawns = TestSupport.Data.Spawns,
            ZoneIds = TestSupport.Data.ZoneIds,
        };

        var inv = new Inventory(data.FindItem, 999, data.FindCard, TestSupport.Clock);
        var species = data.Species.Values.First(s => s.Stage == StatFormulas.InTraining);
        var leader = new Digimon(data, species, 10);
        var character = new Character(leader, inv)
        {
            Uid = 10, AccountUid = 42, Slot = 0, Name = "Tamer", TamerId = 1, Level = 10, MapId = 79, X = 30, Y = 30,
        };
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(42, "debelzak26", Authority.None), character);
        session.Player = player;

        var world = new WorldHarness();
        var store = new InMemoryCashStore();
        store.Seed(42, cash: 100, bonusCash: 100);
        var service = new CashShopService(data, store, world.Io, TestSupport.Clock, NullLogger<CashShopService>.Instance);
        return (data, player, session, world, store, service);
    }

    [Fact]
    public void Cash_shop_and_depository_packets_match_official_sizes_and_offsets()
    {
        var open = new CashShopOpenPacket(0x010101B4, 0, 42, 100, 50, "debelzak26").EncodePayload();
        Assert.Equal(64, open.Length);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(open.AsSpan(4)));
        Assert.Equal(2, open[8]);
        Assert.Equal(42L, BinaryPrimitives.ReadInt64LittleEndian(open.AsSpan(0x10)));
        Assert.Equal(100L, BinaryPrimitives.ReadInt64LittleEndian(open.AsSpan(0x18)));
        Assert.Equal(50L, BinaryPrimitives.ReadInt64LittleEndian(open.AsSpan(0x20)));

        var buy = new CashShopBuyPacket(0x0100FDBC, 0, 42, 100001, 2, 160, 20, 10).EncodePayload();
        Assert.Equal(56, buy.Length);
        Assert.Equal(100001u, BinaryPrimitives.ReadUInt32LittleEndian(buy.AsSpan(0x18)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(buy.AsSpan(0x1C)));
        Assert.Equal(20L, BinaryPrimitives.ReadInt64LittleEndian(buy.AsSpan(0x28)));
        Assert.Equal(10L, BinaryPrimitives.ReadInt64LittleEndian(buy.AsSpan(0x30)));

        var hist = new CashShopHistoryPacket(0, 0, 2026, 11, 1, 1, 42).EncodePayload();
        Assert.Equal(992, hist.Length);

        var auth = new CashDepositoryAuthPacket("debelzak26", "d41d8cd98f00b204e9800998ecf8427e", 1, 1).EncodePayload();
        Assert.Equal(103, auth.Length);
        Assert.Equal(1, auth[101]);
        Assert.Equal(1, auth[102]);

        var list = new CashDepositoryListPacket([], [], success: true).EncodePayload();
        Assert.Equal(4420, list.Length);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(list.AsSpan(4416)));

        var withdraw = new CashDepositoryWithdrawPacket(CashShopItemKind.Item, null, CashDepositoryWithdrawResult.InventoryFull).EncodePayload();
        Assert.Equal(192, withdraw.Length);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(withdraw.AsSpan(0)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(withdraw.AsSpan(188)));
    }

    [Fact]
    public async Task Buy_list_and_withdraw_flow_updates_balance_depository_and_inventory()
    {
        var (_, player, session, world, store, service) = MakeEnv();
        await world.StartAsync();
        try
        {
            // 1. Abrir Cash Shop (0x0159): saldo 100 + 100
            await world.RunAsync(() => service.OpenShop(player, 0x010101B4));
            Assert.Equal([(ushort)MapOpcode.CashShopOpen], session.SentOpcodes());

            // 2. Comprar Starter Pack (ItemDataId=1, 160 cash): debita 100 de bonus + 60 de cash -> resta 40 + 0
            await world.RunAsync(() => service.BuyProduct(player, 0x0100FDBC, 1, 1));
            var buyFrames = session.DrainOutgoing();
            Assert.Single(buyFrames);
            Assert.Equal((ushort)MapOpcode.CashShopBuy, PacketHeader.Read(buyFrames[0]).Opcode);
            var buyPayload = buyFrames[0].AsSpan(PacketHeader.Length);
            Assert.Equal(40L, BinaryPrimitives.ReadInt64LittleEndian(buyPayload.Slice(0x28)));
            Assert.Equal(0L, BinaryPrimitives.ReadInt64LittleEndian(buyPayload.Slice(0x30)));

            // 2.5. Consultar histórico de compras (0x0161): deve listar a compra recém-feita
            var now = DateTimeOffset.UtcNow;
            await world.RunAsync(() => service.GetHistory(player, 0, (ushort)now.Year, (ushort)now.Month, 1));
            var histFrames = session.DrainOutgoing();
            Assert.Single(histFrames);
            Assert.Equal((ushort)MapOpcode.CashShopHistory, PacketHeader.Read(histFrames[0]).Opcode);
            var histPayload = histFrames[0].AsSpan(PacketHeader.Length);
            Assert.Equal(1L, BinaryPrimitives.ReadInt64LittleEndian(histPayload.Slice(0x20)));          // orderId
            Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(histPayload.Slice(0x28)));         // itemDataId
            Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(histPayload.Slice(0x2C)));         // quantity
            Assert.Equal(160L, BinaryPrimitives.ReadInt64LittleEndian(histPayload.Slice(0x30)));        // unitPrice
            Assert.Equal(40L, BinaryPrimitives.ReadInt64LittleEndian(histPayload.Slice(0x38)));         // cashAfter
            Assert.Equal(0L, BinaryPrimitives.ReadInt64LittleEndian(histPayload.Slice(0x40)));          // bonusCashAfter

            // 3. Listar Cash Depository (0x00DE): deve conter 1 card (#74 x50) e 1 item (#718 x1)
            await world.RunAsync(() => service.ListDepository(player));
            Assert.Equal([(ushort)MapOpcode.CashDepositoryList], session.SentOpcodes());
            var rows = await store.ListDepositoryAsync(42, CancellationToken.None);
            Assert.Equal(2, rows.Count);
            var cardRow = rows.Single(r => r.Kind == CashShopItemKind.Card);
            var itemRow = rows.Single(r => r.Kind == CashShopItemKind.Item);

            // 4. Retirar Card (0x00DD -> 0x00DD + 0x00C8) e Item (0x00DD -> 0x00DD + 0x00C9)
            await world.RunAsync(() => service.WithdrawDepository(player, CashShopItemKind.Card, cardRow.Uid));
            Assert.Equal([(ushort)MapOpcode.CashDepositoryWithdraw, (ushort)MapOpcode.BattleItemReceive], session.SentOpcodes());
            Assert.Equal(50u, player.Character.Inventory.Battle.Count(74));

            await world.RunAsync(() => service.WithdrawDepository(player, CashShopItemKind.Item, itemRow.Uid));
            Assert.Equal([(ushort)MapOpcode.CashDepositoryWithdraw, (ushort)MapOpcode.InventoryItemReceive], session.SentOpcodes());
            var aura = player.Character.Inventory.Main.Find(718)!;
            Assert.True(aura.IsTimed); // duração padrão de 30 dias inicia ao retirar
            Assert.Empty(await store.ListDepositoryAsync(42, CancellationToken.None));
        }
        finally
        {
            await world.StopAsync();
        }
    }

    [Fact]
    public async Task Security_guards_reject_insufficient_cash_full_inventory_and_invalid_auth()
    {
        var (_, player, session, world, store, service) = MakeEnv();
        await world.StartAsync();
        try
        {
            // 1. Saldo insuficiente (2 * 160 = 320 > 200): não debita nem deposita
            await world.RunAsync(() => service.BuyProduct(player, 0, 1, 2));
            Assert.Equal(200, (await store.GetBalanceAsync(42, CancellationToken.None))!.Value.Total);
            Assert.Empty(await store.ListDepositoryAsync(42, CancellationToken.None));
            Assert.Equal([(ushort)MapOpcode.ChatNotice], session.SentOpcodes());

            // 2. Compra válida (160) seguida de tentativa de retirada com mochila cheia: restaura no depósito!
            await world.RunAsync(() => service.BuyProduct(player, 0, 1, 1));
            session.SentOpcodes();
            var itemRow = (await store.ListDepositoryAsync(42, CancellationToken.None)).Single(r => r.Kind == CashShopItemKind.Item);

            // Enche todos os 24 slots da mochila Main
            await world.InvokeAsync(() =>
            {
                for (int s = 0; s < Inventory.Size; s++)
                    player.Character.Inventory.Main.Place(player.Character.Inventory.Create(718, s, 1));
                return 0;
            });

            await world.RunAsync(() => service.WithdrawDepository(player, CashShopItemKind.Item, itemRow.Uid));
            var frames = session.DrainOutgoing();
            Assert.Single(frames);
            Assert.Equal((ushort)MapOpcode.CashDepositoryWithdraw, PacketHeader.Read(frames[0]).Opcode);
            Assert.Equal((uint)CashDepositoryWithdrawResult.InventoryFull,
                BinaryPrimitives.ReadUInt32LittleEndian(frames[0].AsSpan(PacketHeader.Length + 188)));
            // Item foi restaurado no Cash Depository (sem perda!)
            Assert.Contains(await store.ListDepositoryAsync(42, CancellationToken.None), r => r.Uid == itemRow.Uid);

            // 3. Autenticação 0x00CA rejeita username de outra conta
            var w = new PacketWriter();
            w.WriteFixedString("outra_conta", 21);
            w.WriteFixedString("d41d8cd98f00b204e9800998ecf8427e", 80);
            w.WriteU8(0);
            w.WriteU8(1);
            var authHandler = new CashDepositoryAuthHandler();
            await world.DispatchAsync(authHandler, session, new PacketReader(w.ToFrame(0).AsSpan(PacketHeader.Length).ToArray()));
            var authFrames = session.DrainOutgoing();
            Assert.Single(authFrames);
            Assert.Equal(0, authFrames[0][PacketHeader.Length + 101]); // result = 0
        }
        finally
        {
            await world.StopAsync();
        }
    }

    [Fact]
    public async Task Withdraw_returns_item_to_depository_when_player_leaves_during_io()
    {
        var (_, player, session, world, store, service) = MakeEnv();
        await world.StartAsync();
        try
        {
            await world.RunAsync(() => service.BuyProduct(player, 0, 1, 1));
            session.DrainOutgoing();
            var itemRow = (await store.ListDepositoryAsync(42, CancellationToken.None)).Single(r => r.Kind == CashShopItemKind.Item);

            var release = new TaskCompletionSource();
            store.HoldTake = release.Task;
            await world.InvokeAsync(() =>
            {
                service.WithdrawDepository(player, CashShopItemKind.Item, itemRow.Uid);
                return true;
            });
            Assert.DoesNotContain(await store.ListDepositoryAsync(42, CancellationToken.None), r => r.Uid == itemRow.Uid);

            session.Close("teste: saiu durante a retirada");
            release.SetResult();
            await world.SettleAsync();

            Assert.Contains(await store.ListDepositoryAsync(42, CancellationToken.None), r => r.Uid == itemRow.Uid);
            Assert.Equal(0u, player.Character.Inventory.Main.Count(718));
        }
        finally
        {
            await world.StopAsync();
        }
    }

    [Fact]
    public void Real_client_pack_loads_cash_shop_catalog()
    {
        var packPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Client", "table.hf");
        if (!File.Exists(packPath)) return;

        var products = ClientTables.CashShopProducts(ClientPack.Open(packPath));
        Assert.True(products.Count > 500, $"Esperado > 500 produtos do Cash Shop, encontrado {products.Count}");
        Assert.True(products.TryGetValue(1, out var starter));
        Assert.Equal(100001u, starter!.ProductId);
        Assert.Equal(160u, starter.Price);
        Assert.NotEmpty(starter.Entries);
    }
}
