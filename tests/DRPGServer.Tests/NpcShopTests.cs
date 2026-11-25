using System.Buffers.Binary;
using System.Collections.Frozen;
using DRPGServer.Data.GameData;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Stats;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Tests;

public class NpcShopTests
{
    private static (GameData Data, Player Player, Session Session) MakeEnv(double bits = 5000)
    {
        var items = new Dictionary<uint, ItemInfo>(TestSupport.Data.Items)
        {
            [29] = new ItemInfo(29, "Double Burger") { Type = 4, SubType = 1, MaxStack = 999, BuyPrice = 600, SellPrice = 60 },
            [24580] = new ItemInfo(24580, "Beginner Crest (1 Day)") { Type = 1, SubType = 1, MaxStack = 1, DurationSeconds = 86400, BuyPrice = 10000, SellPrice = 0 },
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
        };

        var inv = new Inventory(data.FindItem, 999, data.FindCard);
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

        var p52 = new ItemShopListPacket(itemShop, data).EncodePayload();
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
        var itemTrade = new ItemShopTradeHandler(data);
        var cardTrade = new CardShopTradeHandler(data);

        // 1. Comprar 3x Double Burger (600 cada = 1800 Bits) -> resta 3200 Bits
        itemTrade.Handle(session, BuildItemTrade(NpcShopAction.Buy, 0, 29, 3, ItemPlace.Inventory, 0, 0, 40101));
        Assert.Equal(3200.0, player.Character.Bits);
        Assert.Equal(3u, player.Character.Inventory.Main.Count(29));
        Assert.Equal([(ushort)MapOpcode.ItemShopTrade, (ushort)MapOpcode.InventoryItemReceive], session.SentOpcodes());

        // 2. Vender 2x Double Burger (60 cada = 120 Bits) -> fica 3320 Bits e 1x na mochila
        var burger = player.Character.Inventory.Main.Find(29)!;
        itemTrade.Handle(session, BuildItemTrade(NpcShopAction.Sell, burger.Uid, 29, 2, ItemPlace.Inventory, burger.Column, burger.Row, 40101));
        Assert.Equal(3320.0, player.Character.Bits);
        Assert.Equal(1u, player.Character.Inventory.Main.Count(29));
        Assert.Equal([(ushort)MapOpcode.ItemShopTrade], session.SentOpcodes());

        // 3. Comprar 1x item temporário com PriceOverride (500 Bits) -> resta 2820 Bits e ExpiresAt > 0
        itemTrade.Handle(session, BuildItemTrade(NpcShopAction.Buy, 0, 24580, 1, ItemPlace.Inventory, 0, 0, 40101));
        Assert.Equal(2820.0, player.Character.Bits);
        Assert.True(player.Character.Inventory.Main.Find(24580)!.IsTimed);
        session.SentOpcodes();

        // 4. Comprar 5x Offense Plug-In A (200 cada = 1000 Bits) -> resta 1820 Bits
        cardTrade.Handle(session, BuildCardTrade(NpcShopAction.Buy, 0, 3, 5, ItemPlace.Inventory, 0, 0, 5201));
        Assert.Equal(1820.0, player.Character.Bits);
        Assert.Equal(5u, player.Character.Inventory.Battle.Count(3));
        Assert.Equal([(ushort)MapOpcode.CardShopTrade, (ushort)MapOpcode.BattleItemReceive], session.SentOpcodes());

        // 5. Vender todos os 5 cards (100 cada = 500 Bits) -> fica 2320 Bits e slot zerado
        var card = player.Character.Inventory.Battle.Find(3)!;
        cardTrade.Handle(session, BuildCardTrade(NpcShopAction.Sell, card.Uid, 3, 5, ItemPlace.Inventory, card.Column, card.Row, 5201));
        Assert.Equal(2320.0, player.Character.Bits);
        Assert.Equal(0u, player.Character.Inventory.Battle.Count(3));
        Assert.Equal([(ushort)MapOpcode.CardShopTrade], session.SentOpcodes());
    }

    [Fact]
    public void Security_guards_reject_insufficient_bits_unlisted_items_and_forged_sell()
    {
        var (data, player, session) = MakeEnv(bits: 500);
        var itemTrade = new ItemShopTradeHandler(data);

        // Bits insuficientes (1x 600 > 500)
        itemTrade.Handle(session, BuildItemTrade(NpcShopAction.Buy, 0, 29, 1, ItemPlace.Inventory, 0, 0, 40101));
        Assert.Equal(500.0, player.Character.Bits);
        Assert.Empty(session.SentOpcodes());

        // Item que não pertence à loja 40101
        itemTrade.Handle(session, BuildItemTrade(NpcShopAction.Buy, 0, 41, 1, ItemPlace.Inventory, 0, 0, 40101));
        Assert.Equal(500.0, player.Character.Bits);
        Assert.Empty(session.SentOpcodes());

        // Quantidade 0 ou > 1 em item não empilhável
        itemTrade.Handle(session, BuildItemTrade(NpcShopAction.Buy, 0, 29, 0, ItemPlace.Inventory, 0, 0, 40101));
        itemTrade.Handle(session, BuildItemTrade(NpcShopAction.Buy, 0, 24580, 2, ItemPlace.Inventory, 0, 0, 40101));
        Assert.Equal(500.0, player.Character.Bits);
        Assert.Empty(session.SentOpcodes());

        // Vender com UID errado ou quantidade maior que a disponível
        var added = player.Character.Inventory.Main.Add(29, 2)[0];
        itemTrade.Handle(session, BuildItemTrade(NpcShopAction.Sell, added.Uid + 999, 29, 1, ItemPlace.Inventory, added.Column, added.Row, 40101));
        itemTrade.Handle(session, BuildItemTrade(NpcShopAction.Sell, added.Uid, 29, 5, ItemPlace.Inventory, added.Column, added.Row, 40101));
        Assert.Equal(500.0, player.Character.Bits);
        Assert.Equal(2u, player.Character.Inventory.Main.Count(29));
        Assert.Empty(session.SentOpcodes());
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
