using DRPG.Server.Game.Economy;
using DRPG.Server.Game.Entities;
using DRPG.Server.Game.World;
using DRPG.Server.Network;
using DRPG.Server.Network.Packets.Map;

namespace DRPG.Server.Tests;

/// <summary>Sequências repetidas do mapa (<see cref="MapSequences"/>) e a busca do jogador pela conta.</summary>
public class MapSequencesTests
{
    private static readonly EconomyReason Gm = new(EconomySource.Gm, "grant", "teste");

    [Fact]
    public void ListPages_sends_7E_then_7F_seven_times_each()
    {
        var pages = MapSequences.ListPages().Select(p => p.GetType()).ToList();

        Assert.Equal(14, pages.Count);
        Assert.All(pages.Take(7), t => Assert.Equal(typeof(Unknown7EPacket), t));
        Assert.All(pages.Skip(7), t => Assert.Equal(typeof(Unknown7FPacket), t));
    }

    [Fact]
    public async Task WorldState_follows_the_captured_order_and_sends_currencies_only_when_there_are_any()
    {
        await using var harness = new WorldHarness();
        await harness.StartAsync();
        var players = TestSupport.NewPlayers(harness.World);
        var character = Entities.Character();
        var player = await harness.InvokeAsync(() => players.EnterNow(TestSupport.NewSession(ServerRole.Map),
            new AuthenticatedAccount(1, "u", Authority.None), character));

        var plain = await harness.InvokeAsync(() => MapSequences.WorldState(player, null).Select(p => p.GetType()).ToList());
        await harness.RunAsync(() => character.Wallet.Credit(CurrencyKind.Coins, 5, Gm));
        var withCoins = await harness.InvokeAsync(() => MapSequences.WorldState(player, null).Select(p => p.GetType()).ToList());

        Type[] head = [typeof(DRPG.Server.Network.Packets.TwoWayServerPacket<Shared.Packets.Map.CharacterDataPacket>), typeof(DRPG.Server.Network.Packets.TwoWayServerPacket<Shared.Packets.Map.InventoryDataPacket>)];
        Type[] tail = [typeof(Unknown05Packet), typeof(ExpEventPacket), typeof(Unknown74Packet), typeof(Unknown152Packet),
            typeof(SingleDigimonInfoPacket), typeof(Unknown73Packet)];
        var pages = MapSequences.ListPages().Select(p => p.GetType());
        Assert.Equal([.. head, .. pages, .. tail], plain);
        Assert.Equal([.. head, typeof(CurrencyUpdatePacket), .. pages, .. tail], withCoins);
    }

    [Fact]
    public async Task FindByAccount_finds_the_online_character_of_the_account_only()
    {
        await using var harness = new WorldHarness();
        await harness.StartAsync();
        var players = TestSupport.NewPlayers(harness.World);
        var player = await harness.InvokeAsync(() => players.EnterNow(TestSupport.NewSession(ServerRole.Map),
            new AuthenticatedAccount(7, "u", Authority.None), Entities.Character()));

        Assert.Same(player, await harness.InvokeAsync(() => players.FindByAccount(7)));
        Assert.Null(await harness.InvokeAsync(() => players.FindByAccount(8)));
    }
}
