using System.Buffers.Binary;
using System.Collections.Frozen;
using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Economy;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Items;
using DRPGServer.Game.Stats;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

public class NpcShopTests
{
    private static (GameData Data, Player Player, Session Session) MakeEnv(double bits = 5000)
    {
        var items = new Dictionary<uint, ItemInfo>(TestSupport.Data.Items)
        {
            [29] = new ItemInfo(29, "Double Burger") { Type = 4, SubType = 1, MaxStack = 999, BuyPrice = 600, SellPrice = 60 },
            [24580] = new ItemInfo(24580, "Beginner Crest (1 Day)") { Type = 1, SubType = 1, MaxStack = 1, DurationSeconds = 86400, BuyPrice = 10000, SellPrice = 0 },
            [21008] = new ItemInfo(21008, "Exchange Ticket") { Type = 12, SubType = 3, MaxStack = 1, BuyPrice = 1, Col16 = 150, Col17 = 1000 },
        };
        var cards = new Dictionary<uint, CardInfo>
        {
            [3] = new CardInfo(3, "Offense Plug-In A") { Type = 4, SubType = 1, MaxStack = 999, BuyPrice = 200, SellPrice = 100 },
        };
        var shops = new Dictionary<uint, NpcShop>
        {
            [40101] = new NpcShop(40101, NpcShopKind.Item, "Gekomon Village", [
                new NpcShopEntry(29),
                new NpcShopEntry(24580, PriceOverride: 500),
            ]),
            [5050] = new NpcShop(5050, NpcShopKind.Item, "Coins Shop", [
                new NpcShopEntry(21008),
            ], NpcShopCurrency.Coins, NpcShopDelivery.ShopDepository),
            [5051] = new NpcShop(5051, NpcShopKind.Item, "Ladder Shop", [
                new NpcShopEntry(21008),
            ], NpcShopCurrency.LadderPoints),
            [5201] = new NpcShop(5201, NpcShopKind.Card, "DAB Cards", [
                new NpcShopEntry(3),
            ]),
        };

        var data = new GameData
        {
            Species = TestSupport.Data.Species,
            Items = items.ToFrozenDictionary(),
            Cards = cards.ToFrozenDictionary(),
            NpcShops = shops.ToFrozenDictionary(),
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
            // donos das lojas de teste, todos ao lado do jogador (mapa 1, (30, 30))
            Npcs = new NpcDirectory([
                new NpcInfo(1, 1, "Item Shop", 31, 30, NpcWindow.ItemShop, 40101),
                new NpcInfo(1, 2, "Coins Shop", 29, 30, NpcWindow.ItemShop, 5050),
                new NpcInfo(1, 3, "Ladder Shop", 30, 31, NpcWindow.ItemShop, 5051),
                new NpcInfo(1, 4, "Card Shop", 30, 29, NpcWindow.CardShop, 5201),
            ]),
        };

        var inv = new Inventory(data.FindItem, 999, data.FindCard, TestSupport.Clock);
        var species = data.Species.Values.First(s => s.Stage == StatFormulas.InTraining);
        var leader = new Digimon(data, species, 10);
        var character = new Character(leader, inv)
        {
            Uid = 10, AccountUid = 1, Slot = 0, Name = "Tamer", TamerId = 1, Level = 10, MapId = 1, X = 30, Y = 30,
        };
        character.SetBits(bits);
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "user", Authority.None), character);
        session.Player = player;
        return (data, player, session);
    }

    /// <summary>Handler 0x53. Sem <paramref name="world"/>, um mundo parado basta (compras que não usam o depósito da conta).</summary>
    private static ItemShopTradeHandler ItemTrade(GameData data, InMemoryCashStore? store = null, WorldHarness? world = null) =>
        new(data, new ItemService((world ?? new WorldHarness()).Transfers, store ?? new InMemoryCashStore(), TestSupport.Clock), TestSupport.Guard(data));

    private static PacketReader BuildItemTrade(NpcShopAction action, uint uid, uint itemId, uint quantity, ItemPlace place, int col, int row, uint shopId)
    {
        var w = new PacketWriter();
        w.WriteU32((uint)action);
        w.WriteU32(uid);
        w.WriteU32(itemId);
        w.WriteZeros(100);
        w.WriteU32(quantity);
        w.WriteU32(0);
        w.WriteF64(0);
        w.WriteU16(0);
        w.WriteU16((ushort)place);
        w.WriteI32(col);
        w.WriteI32(row);
        w.WriteU32(shopId);
        return new PacketReader(w.ToFrame(0).AsSpan(PacketHeader.Length).ToArray());
    }

    private static PacketReader BuildCardTrade(NpcShopAction action, uint uid, uint cardId, uint quantity, ItemPlace place, int col, int row, uint shopId)
    {
        var w = new PacketWriter();
        w.WriteU32((uint)action);
        w.WriteU32(uid);
        w.WriteU32(cardId);
        w.WriteZeros(68);
        w.WriteU32(quantity);
        w.WriteU32(0);
        w.WriteF64(0);
        w.WriteU16((ushort)place);
        w.WriteU16(0);
        w.WriteI32(col);
        w.WriteI32(row);
        w.WriteU32(shopId);
        return new PacketReader(w.ToFrame(0).AsSpan(PacketHeader.Length).ToArray());
    }

    [Fact]
    public void Shop_list_and_trade_packets_match_official_sizes_and_offsets()
    {
        var (data, _, _) = MakeEnv();
        var itemShop = data.FindNpcShop(40101, NpcShopKind.Item)!;
        var cardShop = data.FindNpcShop(5201, NpcShopKind.Card)!;

        var p52 = new ItemShopListPacket(itemShop, data, TestSupport.Clock.UnixNow).EncodePayload();
        Assert.Equal(4320, p52.Length);
        Assert.Equal(29u, BinaryPrimitives.ReadUInt32LittleEndian(p52.AsSpan(4)));
        Assert.Equal(600u, BinaryPrimitives.ReadUInt32LittleEndian(p52.AsSpan(20)));
        // Segundo slot: PriceOverride = 500 e item temporário com ExpiresAt > 0
        Assert.Equal(24580u, BinaryPrimitives.ReadUInt32LittleEndian(p52.AsSpan(108 + 4)));
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(p52.AsSpan(108 + 12)) > 1000u);
        Assert.Equal(500u, BinaryPrimitives.ReadUInt32LittleEndian(p52.AsSpan(108 + 20)));

        var p50 = new CardShopListPacket(cardShop, data).EncodePayload();
        Assert.Equal(3040, p50.Length);
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(p50.AsSpan(4)));
        Assert.Equal(200u, BinaryPrimitives.ReadUInt32LittleEndian(p50.AsSpan(20)));

        var p53 = new ItemShopTradePacket(NpcShopAction.Buy, null, 2, 1234.0, ItemPlace.Inventory, 3, 1, 40101).EncodePayload();
        Assert.Equal(144, p53.Length);
        Assert.Equal(1234.0, BinaryPrimitives.ReadDoubleLittleEndian(p53.AsSpan(0x78)));
        Assert.Equal((ushort)ItemPlace.Inventory, BinaryPrimitives.ReadUInt16LittleEndian(p53.AsSpan(0x82)));
        Assert.Equal(40101u, BinaryPrimitives.ReadUInt32LittleEndian(p53.AsSpan(0x8C)));

        var p51 = new CardShopTradePacket(NpcShopAction.Buy, null, 5, 4321.0, ItemPlace.Inventory, 2, 0, 5201).EncodePayload();
        Assert.Equal(112, p51.Length);
        Assert.Equal(4321.0, BinaryPrimitives.ReadDoubleLittleEndian(p51.AsSpan(0x58)));
        Assert.Equal((ushort)ItemPlace.Inventory, BinaryPrimitives.ReadUInt16LittleEndian(p51.AsSpan(0x60)));
        Assert.Equal(5201u, BinaryPrimitives.ReadUInt32LittleEndian(p51.AsSpan(0x6C)));
    }

    [Fact]
    public void Item_and_card_shop_buy_and_sell_update_bits_and_inventory()
    {
        var (data, player, session) = MakeEnv(bits: 5000);
        var itemTrade = ItemTrade(data);
        var cardTrade = new CardShopTradeHandler(data, TestSupport.Guard(data));

        // 1. Comprar 3x Double Burger (600 cada = 1800 Bits) -> resta 3200 Bits
        itemTrade.Dispatch(session, BuildItemTrade(NpcShopAction.Buy, 0, 29, 3, ItemPlace.Inventory, 0, 0, 40101));
        Assert.Equal(3200.0, player.Character.Bits);
        Assert.Equal(3u, player.Character.Inventory.Main.Count(29));
        Assert.Equal([(ushort)MapOpcode.ItemShopTrade, (ushort)MapOpcode.InventoryItemReceive], session.SentOpcodes());

        // 2. Vender 2x Double Burger (60 cada = 120 Bits) -> fica 3320 Bits e 1x na mochila
        var burger = player.Character.Inventory.Main.Find(29)!;
        itemTrade.Dispatch(session, BuildItemTrade(NpcShopAction.Sell, burger.Uid, 29, 2, ItemPlace.Inventory, burger.Column, burger.Row, 40101));
        Assert.Equal(3320.0, player.Character.Bits);
        Assert.Equal(1u, player.Character.Inventory.Main.Count(29));
        Assert.Equal([(ushort)MapOpcode.ItemShopTrade], session.SentOpcodes());

        // 3. Comprar 1x item temporário com PriceOverride (500 Bits) -> resta 2820 Bits e ExpiresAt > 0
        itemTrade.Dispatch(session, BuildItemTrade(NpcShopAction.Buy, 0, 24580, 1, ItemPlace.Inventory, 0, 0, 40101));
        Assert.Equal(2820.0, player.Character.Bits);
        Assert.True(player.Character.Inventory.Main.Find(24580)!.IsTimed);
        session.SentOpcodes();

        // 4. Comprar 5x Offense Plug-In A (200 cada = 1000 Bits) -> resta 1820 Bits
        cardTrade.Dispatch(session, BuildCardTrade(NpcShopAction.Buy, 0, 3, 5, ItemPlace.Inventory, 0, 0, 5201));
        Assert.Equal(1820.0, player.Character.Bits);
        Assert.Equal(5u, player.Character.Inventory.Battle.Count(3));
        Assert.Equal([(ushort)MapOpcode.CardShopTrade, (ushort)MapOpcode.BattleItemReceive], session.SentOpcodes());

        // 5. Vender todos os 5 cards (100 cada = 500 Bits) -> fica 2320 Bits e slot zerado
        var card = player.Character.Inventory.Battle.Find(3)!;
        cardTrade.Dispatch(session, BuildCardTrade(NpcShopAction.Sell, card.Uid, 3, 5, ItemPlace.Inventory, card.Column, card.Row, 5201));
        Assert.Equal(2320.0, player.Character.Bits);
        Assert.Equal(0u, player.Character.Inventory.Battle.Count(3));
        Assert.Equal([(ushort)MapOpcode.CardShopTrade], session.SentOpcodes());
    }

    [Fact]
    public void Security_guards_reject_insufficient_bits_unlisted_items_and_forged_sell()
    {
        var (data, player, session) = MakeEnv(bits: 500);
        var itemTrade = ItemTrade(data);

        // Bits insuficientes (1x 600 > 500)
        itemTrade.Dispatch(session, BuildItemTrade(NpcShopAction.Buy, 0, 29, 1, ItemPlace.Inventory, 0, 0, 40101));
        Assert.Equal(500.0, player.Character.Bits);
        Assert.Empty(session.SentOpcodes());

        // Item que não pertence à loja 40101
        itemTrade.Dispatch(session, BuildItemTrade(NpcShopAction.Buy, 0, 41, 1, ItemPlace.Inventory, 0, 0, 40101));
        Assert.Equal(500.0, player.Character.Bits);
        Assert.Empty(session.SentOpcodes());

        // Quantidade 0 ou > 1 em item não empilhável
        itemTrade.Dispatch(session, BuildItemTrade(NpcShopAction.Buy, 0, 29, 0, ItemPlace.Inventory, 0, 0, 40101));
        itemTrade.Dispatch(session, BuildItemTrade(NpcShopAction.Buy, 0, 24580, 2, ItemPlace.Inventory, 0, 0, 40101));
        Assert.Equal(500.0, player.Character.Bits);
        Assert.Empty(session.SentOpcodes());

        // Vender com UID errado ou quantidade maior que a disponível
        var added = player.Character.Inventory.Main.Add(29, 2)[0];
        itemTrade.Dispatch(session, BuildItemTrade(NpcShopAction.Sell, added.Uid + 999, 29, 1, ItemPlace.Inventory, added.Column, added.Row, 40101));
        itemTrade.Dispatch(session, BuildItemTrade(NpcShopAction.Sell, added.Uid, 29, 5, ItemPlace.Inventory, added.Column, added.Row, 40101));
        Assert.Equal(500.0, player.Character.Bits);
        Assert.Equal(2u, player.Character.Inventory.Main.Count(29));
        Assert.Empty(session.SentOpcodes());
    }

    [Fact]
    public async Task Special_currency_shops_use_coins_and_ladder_points_from_c2s_buy_action()
    {
        var cashStore = new InMemoryCashStore();
        var (data, player, session) = MakeEnv(bits: 5000);
        await using var world = new WorldHarness();
        await world.StartAsync();
        player.Character.SetCoins(200);
        player.Character.SetLadderPoints(1500);
        var itemTrade = ItemTrade(data, cashStore, world);

        // 1. Cliente sempre envia action=1 (Buy) em C2S 0x0053; loja 5050 (Coins + ShopDepository):
        // debita 150 Coins (Col16), deposita no Shop Depository (não na mochila), responde S2C action=4 + 0xCB (sem 0xC9)
        await world.DispatchAsync(itemTrade, session, BuildItemTrade(NpcShopAction.Buy, 0, 21008, 1, ItemPlace.Inventory, 0, 0, 5050));
        Assert.Equal(5000.0, player.Character.Bits);
        Assert.Equal((ushort)50, player.Character.Coins);
        Assert.Equal(0u, player.Character.Inventory.Main.Count(21008));
        var deposited = Assert.Single(await cashStore.ListDepositoryAsync(1, CancellationToken.None));
        Assert.Equal((CashShopItemKind.Item, 21008u, 1u, 0u), (deposited.Kind, deposited.ItemId, deposited.Amount, deposited.ExpiresAt));
        // a moeda gasta foi no mesmo commit do depósito
        Assert.Equal((ushort)50, Assert.Single(cashStore.Snapshots).Coins);
        var frames = session.DrainOutgoing();
        Assert.Equal(2, frames.Count);
        Assert.Equal((ushort)MapOpcode.ItemShopTrade, PacketHeader.Read(frames[0]).Opcode);
        Assert.Equal((uint)NpcShopAction.BuyCoins, BinaryPrimitives.ReadUInt32LittleEndian(frames[0].AsSpan(PacketHeader.Length + 0x00)));
        Assert.Equal((ushort)50, BinaryPrimitives.ReadUInt16LittleEndian(frames[0].AsSpan(PacketHeader.Length + 0x80)));
        Assert.Equal((ushort)MapOpcode.CurrencyUpdate, PacketHeader.Read(frames[1]).Opcode);
        Assert.Equal(16, frames[1].Length - PacketHeader.Length);
        Assert.Equal(50u, BinaryPrimitives.ReadUInt32LittleEndian(frames[1].AsSpan(PacketHeader.Length + 0x0C)));

        // 2. Com Coins insuficientes (50 < 150), bloqueia a compra
        await world.DispatchAsync(itemTrade, session, BuildItemTrade(NpcShopAction.Buy, 0, 21008, 1, ItemPlace.Inventory, 0, 0, 5050));
        Assert.Equal((ushort)50, player.Character.Coins);
        Assert.Empty(session.SentOpcodes());

        // 2b. Se o Shop Depository estiver cheio, reembolsa os Coins
        await world.RunAsync(() => player.Character.SetCoins(200));
        cashStore.Fill(1, CashShopItemKind.Item);
        await world.DispatchAsync(itemTrade, session, BuildItemTrade(NpcShopAction.Buy, 0, 21008, 1, ItemPlace.Inventory, 0, 0, 5050));
        Assert.Equal((ushort)200, player.Character.Coins);
        Assert.Single(cashStore.Snapshots);
        Assert.Equal([(ushort)MapOpcode.ChatNotice], session.SentOpcodes());
        Assert.True(player.NeedsImmediateSave);                 // o reembolso vai para o banco
        Assert.Equal(PlayerStates.Idle, player.State);

        // 2c. Erro de banco: nada gravado, Coins de volta
        cashStore = new InMemoryCashStore();
        cashStore.FailTransfers = true;
        itemTrade = ItemTrade(data, cashStore, world);
        await world.DispatchAsync(itemTrade, session, BuildItemTrade(NpcShopAction.Buy, 0, 21008, 1, ItemPlace.Inventory, 0, 0, 5050));
        Assert.Equal((ushort)200, player.Character.Coins);
        Assert.Empty(cashStore.Snapshots);
        Assert.Equal([(ushort)MapOpcode.ChatNotice], session.SentOpcodes());
        cashStore.FailTransfers = false;

        // 3. Loja de LadderPoints (5051): C2S action=1 debita 1000 (Col17), resta 500, S2C responde action=5
        await world.DispatchAsync(itemTrade, session, BuildItemTrade(NpcShopAction.Buy, 0, 21008, 1, ItemPlace.Inventory, 0, 0, 5051));
        Assert.Equal(500u, player.Character.LadderPoints);
        var ladderFrames = session.DrainOutgoing();
        Assert.Equal(2, ladderFrames.Count);
        Assert.Equal((uint)NpcShopAction.BuyLadderPoints, BinaryPrimitives.ReadUInt32LittleEndian(ladderFrames[0].AsSpan(PacketHeader.Length + 0x00)));
        Assert.Equal((ushort)MapOpcode.InventoryItemReceive, PacketHeader.Read(ladderFrames[1]).Opcode);

        // 4. CharacterDataPacket (S2C 0x02) inclui LadderPoints (+0x5C), Medals (+0x19A8) e Coins (+0x19AA)
        player.Character.SetMedals(77);
        player.Character.SetCoins(50);
        var charPayload = new CharacterDataPacket(player.Character, TestSupport.Clock.UnixNow).Encode().AsSpan(PacketHeader.Length);
        Assert.Equal(7600, charPayload.Length);
        Assert.Equal(500u, BinaryPrimitives.ReadUInt32LittleEndian(charPayload.Slice(0x5C)));
        Assert.Equal((ushort)77, BinaryPrimitives.ReadUInt16LittleEndian(charPayload.Slice(0x19A8)));
        Assert.Equal((ushort)50, BinaryPrimitives.ReadUInt16LittleEndian(charPayload.Slice(0x19AA)));
    }

    private static List<(EconomyAsset Asset, uint AssetId, long Amount, long? Balance, string Action, uint? Character)> Audit(
        IEnumerable<EconomyEntry> entries) =>
        entries.Select(e => (e.Asset, e.AssetId, e.Amount, e.BalanceAfter, e.Action, e.CharacterUid)).ToList();

    [Fact]
    public void Shop_trades_are_audited_with_balance_after()
    {
        var (data, player, session) = MakeEnv(bits: 5000);
        var itemTrade = ItemTrade(data);
        var cardTrade = new CardShopTradeHandler(data, TestSupport.Guard(data));

        itemTrade.Dispatch(session, BuildItemTrade(NpcShopAction.Buy, 0, 29, 3, ItemPlace.Inventory, 0, 0, 40101));
        var burger = player.Character.Inventory.Main.Find(29)!;
        itemTrade.Dispatch(session, BuildItemTrade(NpcShopAction.Sell, burger.Uid, 29, 2, ItemPlace.Inventory, burger.Column, burger.Row, 40101));
        cardTrade.Dispatch(session, BuildCardTrade(NpcShopAction.Buy, 0, 3, 5, ItemPlace.Inventory, 0, 0, 5201));

        Assert.Equal([
            (EconomyAsset.Bits, 0u, -1800L, (long?)3200, "buy", (uint?)10),
            (EconomyAsset.Item, 29u, 3L, null, "buy", 10u),
            (EconomyAsset.Item, 29u, -2L, null, "sell", 10u),
            (EconomyAsset.Bits, 0u, 120L, 3320, "sell", 10u),
            (EconomyAsset.Bits, 0u, -1000L, 2320, "buy", 10u),
            (EconomyAsset.Card, 3u, 5L, null, "buy", 10u),
        ], Audit(player.Character.Economy.Pending));
        Assert.All(player.Character.Economy.Pending, e => Assert.Equal((EconomySource.NpcShop, 1u), (e.Source, e.AccountUid)));
    }

    [Fact]
    public void Refused_purchase_is_not_audited()
    {
        var (data, player, session) = MakeEnv(bits: 100);
        ItemTrade(data).Dispatch(session, BuildItemTrade(NpcShopAction.Buy, 0, 29, 3, ItemPlace.Inventory, 0, 0, 40101));
        Assert.Empty(player.Character.Economy.Pending);
    }

    [Fact]
    public async Task Shop_depository_purchase_audits_the_account_side_inside_the_transfer()
    {
        var cashStore = new InMemoryCashStore();
        var (data, player, session) = MakeEnv();
        await using var world = new WorldHarness();
        await world.StartAsync();
        player.Character.SetCoins(400);
        var itemTrade = ItemTrade(data, cashStore, world);

        await world.DispatchAsync(itemTrade, session, BuildItemTrade(NpcShopAction.Buy, 0, 21008, 1, ItemPlace.Inventory, 0, 0, 5050));

        // a moeda (personagem) e a entrada no depósito (conta, sem personagem) foram no snapshot da transferência
        Assert.Equal([
            (EconomyAsset.Coins, 0u, -150L, (long?)250, "buy", (uint?)10),
            (EconomyAsset.Item, 21008u, 1L, null, "buy", null),
        ], Audit(Assert.Single(cashStore.Snapshots).Economy));
        Assert.Empty(player.Character.Economy.Pending);         // confirmado: saiu do personagem

        // depósito cheio: a entrada da conta some, o gasto e o reembolso ficam para o próximo save
        cashStore.Fill(1, CashShopItemKind.Item);
        await world.DispatchAsync(itemTrade, session, BuildItemTrade(NpcShopAction.Buy, 0, 21008, 1, ItemPlace.Inventory, 0, 0, 5050));
        Assert.Equal([
            (EconomyAsset.Coins, 0u, -150L, (long?)100, "buy", (uint?)10),
            (EconomyAsset.Coins, 0u, 150L, 250, "refund", 10u),
        ], Audit(player.Character.Economy.Pending));
    }

    [Fact]
    public void Real_client_pack_validates_all_entries_in_NpcShop_xml()
    {
        var packPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Client", "table.hf");
        if (!File.Exists(packPath)) return;

        var data = GameDataLoader.Load(Path.Combine(AppContext.BaseDirectory, "Resources"), ClientPack.Open(packPath));
        Assert.Equal(7, data.NpcShops.Count);
        foreach (var shop in data.NpcShops.Values)
        {
            Assert.NotEmpty(shop.Entries);
            foreach (var e in shop.Entries)
            {
                if (shop.Kind == NpcShopKind.Card)
                    Assert.NotNull(data.FindCard(e.ItemId));
                else
                    Assert.NotNull(data.FindItem(e.ItemId));
            }
        }
    }
}
