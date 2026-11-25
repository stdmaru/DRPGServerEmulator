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
    private sealed class InMemoryCashStore : ICashStore
    {
        private readonly Dictionary<uint, CashBalance> _balances = [];
        private readonly Dictionary<uint, List<CashDepositoryRow>> _depo = [];
        private readonly Dictionary<uint, List<CashShopHistoryEntry>> _history = [];
        private uint _nextUid = 1000;
        private long _nextOrderId = 1;

        public void Seed(uint accountUid, uint cash, uint bonusCash = 0)
        {
            _balances[accountUid] = new CashBalance(cash, bonusCash);
            _depo.TryAdd(accountUid, []);
            _history.TryAdd(accountUid, []);
        }

        public Task<CashBalance?> GetBalanceAsync(uint accountUid, CancellationToken ct) =>
            Task.FromResult(_balances.TryGetValue(accountUid, out var b) ? (CashBalance?)b : null);

        public Task<CashBalance?> AddCashAsync(uint accountUid, uint cashDelta, uint bonusCashDelta, CancellationToken ct)
        {
            if (!_balances.TryGetValue(accountUid, out var b)) return Task.FromResult<CashBalance?>(null);
            var next = new CashBalance(b.Cash + cashDelta, b.BonusCash + bonusCashDelta);
            _balances[accountUid] = next;
            return Task.FromResult<CashBalance?>(next);
        }

        public Task<IReadOnlyList<CashDepositoryRow>> ListDepositoryAsync(uint accountUid, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CashDepositoryRow>>(_depo.TryGetValue(accountUid, out var list) ? list.ToList() : []);

        public Task<CashPurchaseOutcome> PurchaseAsync(
            uint accountUid,
            uint itemDataId,
            uint quantity,
            uint unitPrice,
            uint totalPrice,
            IReadOnlyList<(CashShopItemKind Kind, uint ItemId, uint Amount, uint ExpiresAt)> entries,
            CancellationToken ct)
        {
            if (!_balances.TryGetValue(accountUid, out var bal))
                return Task.FromResult(new CashPurchaseOutcome(CashPurchaseResult.AccountNotFound, default));
            if (bal.Total < totalPrice)
                return Task.FromResult(new CashPurchaseOutcome(CashPurchaseResult.InsufficientCash, bal));

            var list = _depo[accountUid];
            var usedCards = list.Where(r => r.Kind == CashShopItemKind.Card).Select(r => (int)r.Slot).ToHashSet();
            var usedItems = list.Where(r => r.Kind == CashShopItemKind.Item).Select(r => (int)r.Slot).ToHashSet();
            var toAdd = new List<CashDepositoryRow>();

            foreach (var (kind, itemId, amount, expiresAt) in entries)
            {
                var used = kind == CashShopItemKind.Card ? usedCards : usedItems;
                int slot = Enumerable.Range(0, CashRepository.DepositoryCapacityPerKind).FirstOrDefault(i => !used.Contains(i), -1);
                if (slot < 0)
                    return Task.FromResult(new CashPurchaseOutcome(CashPurchaseResult.DepositoryFull, bal));
                used.Add(slot);
                toAdd.Add(new CashDepositoryRow(kind, (byte)slot, _nextUid++, itemId, amount, expiresAt));
            }

            uint fromBonus = Math.Min(bal.BonusCash, totalPrice);
            uint fromCash = totalPrice - fromBonus;
            var after = new CashBalance(bal.Cash - fromCash, bal.BonusCash - fromBonus);
            _balances[accountUid] = after;
            list.AddRange(toAdd);
            _history[accountUid].Add(new CashShopHistoryEntry(
                _nextOrderId++, itemDataId, quantity, unitPrice, after.Cash, after.BonusCash,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            return Task.FromResult(new CashPurchaseOutcome(CashPurchaseResult.Ok, after));
        }

        public Task<bool> DepositAsync(
            uint accountUid,
            IReadOnlyList<(CashShopItemKind Kind, uint ItemId, uint Amount, uint ExpiresAt)> entries,
            CancellationToken ct)
        {
            _depo.TryAdd(accountUid, []);
            var list = _depo[accountUid];
            var usedCards = list.Where(r => r.Kind == CashShopItemKind.Card).Select(r => (int)r.Slot).ToHashSet();
            var usedItems = list.Where(r => r.Kind == CashShopItemKind.Item).Select(r => (int)r.Slot).ToHashSet();
            var toAdd = new List<CashDepositoryRow>();
            foreach (var (kind, itemId, amount, expiresAt) in entries)
            {
                var used = kind == CashShopItemKind.Card ? usedCards : usedItems;
                int slot = Enumerable.Range(0, CashRepository.DepositoryCapacityPerKind).FirstOrDefault(i => !used.Contains(i), -1);
                if (slot < 0) return Task.FromResult(false);
                used.Add(slot);
                toAdd.Add(new CashDepositoryRow(kind, (byte)slot, _nextUid++, itemId, amount, expiresAt));
            }
            list.AddRange(toAdd);
            return Task.FromResult(true);
        }

        public Task<CashShopHistoryPage> GetHistoryAsync(uint accountUid, ushort year, ushort month, ushort page, CancellationToken ct)
        {
            if (!_history.TryGetValue(accountUid, out var all))
                return Task.FromResult(new CashShopHistoryPage([], 1));
            long startTs = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            long endTs = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1).ToUnixTimeSeconds();
            var filtered = all.Where(e => e.PurchasedAt >= startTs && e.PurchasedAt < endTs).OrderByDescending(e => e.OrderId).ToList();
            ushort totalPages = (ushort)Math.Max(1, (filtered.Count + CashRepository.HistoryPageSize - 1) / CashRepository.HistoryPageSize);
            int p = Math.Clamp((int)page, 1, totalPages);
            var slice = filtered.Skip((p - 1) * CashRepository.HistoryPageSize).Take(CashRepository.HistoryPageSize).ToList();
            return Task.FromResult(new CashShopHistoryPage(slice, totalPages));
        }

        public Task<CashDepositoryRow?> TakeDepositoryItemAsync(uint accountUid, CashShopItemKind kind, uint uid, CancellationToken ct)
        {
            if (!_depo.TryGetValue(accountUid, out var list)) return Task.FromResult<CashDepositoryRow?>(null);
            int idx = list.FindIndex(r => r.Kind == kind && r.Uid == uid);
            if (idx < 0) return Task.FromResult<CashDepositoryRow?>(null);
            var row = list[idx];
            list.RemoveAt(idx);
            return Task.FromResult<CashDepositoryRow?>(row);
        }

        public Task RestoreDepositoryItemAsync(uint accountUid, CashDepositoryRow row, CancellationToken ct)
        {
            _depo[accountUid].Add(row);
            return Task.CompletedTask;
        }
    }

    private static (GameData Data, Player Player, Session Session, GameWorld World, InMemoryCashStore Store, CashShopService Service) MakeEnv()
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

        var inv = new Inventory(data.FindItem, 999, data.FindCard);
        var species = data.Species.Values.First(s => s.Stage == StatFormulas.InTraining);
        var leader = new Digimon(data, species, 10);
        var character = new Character(leader, inv)
        {
            Uid = 10, AccountUid = 42, Slot = 0, Name = "Tamer", TamerId = 1, Level = 10, MapId = 79, X = 30, Y = 30,
        };
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(42, "debelzak26", Authority.None), character);
        session.Player = player;

        var world = TestSupport.NewWorld();
        var store = new InMemoryCashStore();
        store.Seed(42, cash: 100, bonusCash: 100);
        var service = new CashShopService(data, store, world, NullLogger<CashShopService>.Instance);
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
        await world.StartAsync(CancellationToken.None);
        try
        {
            // 1. Abrir Cash Shop (0x0159): saldo 100 + 100
            await service.OpenShopAsync(player, 0x010101B4);
            await world.InvokeAsync(() => 0);
            Assert.Equal([(ushort)MapOpcode.CashShopOpen], session.SentOpcodes());

            // 2. Comprar Starter Pack (ItemDataId=1, 160 cash): debita 100 de bonus + 60 de cash -> resta 40 + 0
            await service.BuyProductAsync(player, 0x0100FDBC, 1, 1);
            await world.InvokeAsync(() => 0);
            var buyFrames = session.DrainOutgoing();
            Assert.Single(buyFrames);
            Assert.Equal((ushort)MapOpcode.CashShopBuy, PacketHeader.Read(buyFrames[0]).Opcode);
            var buyPayload = buyFrames[0].AsSpan(PacketHeader.Length);
            Assert.Equal(40L, BinaryPrimitives.ReadInt64LittleEndian(buyPayload.Slice(0x28)));
            Assert.Equal(0L, BinaryPrimitives.ReadInt64LittleEndian(buyPayload.Slice(0x30)));

            // 2.5. Consultar histórico de compras (0x0161): deve listar a compra recém-feita
            var now = DateTimeOffset.UtcNow;
            await service.GetHistoryAsync(player, 0, (ushort)now.Year, (ushort)now.Month, 1);
            await world.InvokeAsync(() => 0);
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
            await service.ListDepositoryAsync(player);
            await world.InvokeAsync(() => 0);
            Assert.Equal([(ushort)MapOpcode.CashDepositoryList], session.SentOpcodes());
            var rows = await store.ListDepositoryAsync(42, CancellationToken.None);
            Assert.Equal(2, rows.Count);
            var cardRow = rows.Single(r => r.Kind == CashShopItemKind.Card);
            var itemRow = rows.Single(r => r.Kind == CashShopItemKind.Item);

            // 4. Retirar Card (0x00DD -> 0x00DD + 0x00C8) e Item (0x00DD -> 0x00DD + 0x00C9)
            await service.WithdrawDepositoryAsync(player, CashShopItemKind.Card, cardRow.Uid);
            await world.InvokeAsync(() => 0);
            Assert.Equal([(ushort)MapOpcode.CashDepositoryWithdraw, (ushort)MapOpcode.BattleItemReceive], session.SentOpcodes());
            Assert.Equal(50u, player.Character.Inventory.Battle.Count(74));

            await service.WithdrawDepositoryAsync(player, CashShopItemKind.Item, itemRow.Uid);
            await world.InvokeAsync(() => 0);
            Assert.Equal([(ushort)MapOpcode.CashDepositoryWithdraw, (ushort)MapOpcode.InventoryItemReceive], session.SentOpcodes());
            var aura = player.Character.Inventory.Main.Find(718)!;
            Assert.True(aura.IsTimed); // duração padrão de 30 dias inicia ao retirar
            Assert.Empty(await store.ListDepositoryAsync(42, CancellationToken.None));
        }
        finally
        {
            await world.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Security_guards_reject_insufficient_cash_full_inventory_and_invalid_auth()
    {
        var (_, player, session, world, store, service) = MakeEnv();
        await world.StartAsync(CancellationToken.None);
        try
        {
            // 1. Saldo insuficiente (2 * 160 = 320 > 200): não debita nem deposita
            await service.BuyProductAsync(player, 0, 1, 2);
            await world.InvokeAsync(() => 0);
            Assert.Equal(200, (await store.GetBalanceAsync(42, CancellationToken.None))!.Value.Total);
            Assert.Empty(await store.ListDepositoryAsync(42, CancellationToken.None));
            Assert.Equal([(ushort)MapOpcode.ChatNotice], session.SentOpcodes());

            // 2. Compra válida (160) seguida de tentativa de retirada com mochila cheia: restaura no depósito!
            await service.BuyProductAsync(player, 0, 1, 1);
            await world.InvokeAsync(() => 0);
            session.SentOpcodes();
            var itemRow = (await store.ListDepositoryAsync(42, CancellationToken.None)).Single(r => r.Kind == CashShopItemKind.Item);

            // Enche todos os 24 slots da mochila Main
            await world.InvokeAsync(() =>
            {
                for (int s = 0; s < Inventory.Size; s++)
                    player.Character.Inventory.Main.Place(player.Character.Inventory.Create(718, s, 1));
                return 0;
            });

            await service.WithdrawDepositoryAsync(player, CashShopItemKind.Item, itemRow.Uid);
            await world.InvokeAsync(() => 0);
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
            authHandler.Handle(session, new PacketReader(w.ToFrame(0).AsSpan(PacketHeader.Length).ToArray()));
            var authFrames = session.DrainOutgoing();
            Assert.Single(authFrames);
            Assert.Equal(0, authFrames[0][PacketHeader.Length + 101]); // result = 0
        }
        finally
        {
            await world.StopAsync(CancellationToken.None);
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
