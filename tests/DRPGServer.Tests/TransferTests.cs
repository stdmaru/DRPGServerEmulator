using DRPGServer.Data.Repositories;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

/// <summary>Transferências conta ↔ personagem (docs/emulador/seguranca.md#transferências-entre-conta-e-personagem).</summary>
public class TransferTests
{
    private static readonly AuthenticatedAccount Account = new(1, "tester", Authority.None);

    [Fact]
    public void Revert_undoes_a_grant_exactly_including_merged_stacks()
    {
        var inventory = TestSupport.EmptyInventory(maxStack: 10);
        var bag = inventory.Main;
        bag.Add(29, 7);                                   // pilha parcial
        var before = bag.Items.Select(i => (i.Slot, i.ItemId, i.Amount)).ToList();

        var grant = bag.Grant(29, 15)!;                   // completa a pilha (3) e cria outra (10) e mais uma (2)
        Assert.Equal(22u, bag.Count(29));
        Assert.Equal(3, grant.Changed.Count);

        bag.Revert(grant);
        Assert.Equal(before, bag.Items.Select(i => (i.Slot, i.ItemId, i.Amount)).ToList());
    }

    [Fact]
    public void Grant_that_does_not_fit_changes_nothing()
    {
        var bag = TestSupport.EmptyInventory(maxStack: 1).Main;
        Assert.Null(bag.Grant(29, Inventory.Size + 1));
        Assert.Empty(bag.Items);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public WorldHarness Harness { get; } = new();
        public RecordingSnapshotStore Saves { get; } = new();
        public PlayerSystem Players { get; }

        public Fixture()
        {
            var world = Harness.World;
            var zones = new ZoneSystem(TestSupport.Data, world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
            Players = new PlayerSystem(world, zones, TestSupport.NewBattles(world), Harness.Persistence, Saves,
                TestSupport.GameOptions(), NullLogger<PlayerSystem>.Instance);
        }

        public ValueTask DisposeAsync() => Harness.DisposeAsync();
    }

    [Fact]
    public async Task Player_is_not_saved_while_a_transfer_is_pending_and_leaving_waits_for_it()
    {
        await using var f = new Fixture();
        await f.Harness.StartAsync();
        var session = TestSupport.NewSession(ServerRole.Map);
        session.Account = Account;
        var player = await f.Harness.InvokeAsync(() => f.Players.EnterNow(session, Account, Entities.Character()));

        var commit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = new TaskCompletionSource<TransferOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        CharacterSnapshot? committed = null;
        await f.Harness.InvokeAsync(() =>
        {
            player.Character.SetBits(500);
            f.Harness.Transfers.Run(player, "teste",
                commit: async (snapshot, _) => { committed = snapshot; return await commit.Task; },
                settled: outcome => settled.SetResult(outcome));
            player.RequestImmediateSave();                // pedido de save no meio da transferência
            return true;
        });

        await Task.Delay(200);                            // vários ticks do mundo
        Assert.Empty(f.Saves.Saved);

        session.Close("saiu no meio da transferência");
        await f.Harness.InvokeAsync(() => { f.Players.Leave(session); return true; });
        await Task.Delay(100);
        Assert.Empty(f.Saves.Saved);                      // o snapshot final espera a transferência

        commit.SetResult(true);
        Assert.Equal(TransferOutcome.Committed, await settled.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await f.Harness.SettleAsync();

        Assert.Equal(500, committed!.Bits);
        Assert.Equal(500, Assert.Single(f.Saves.Saved).Bits);   // o snapshot final veio depois
        Assert.Null(player.PendingTransfer);
    }

    [Fact]
    public async Task Transfer_job_replaces_an_older_pending_save_of_the_character()
    {
        await using var f = new Fixture();
        var executed = new List<string>();
        var queue = f.Harness.Persistence;

        // nada consome a fila ainda: o save fica pendente
        queue.EnqueueCharacterSave(TestSupport.Job(7, _ => { executed.Add("save"); return Task.CompletedTask; }));
        queue.EnqueueCharacterTransfer(TestSupport.Job(7, _ => { executed.Add("transfer"); return Task.CompletedTask; }));
        await f.Harness.StartAsync();
        await queue.FlushAsync();

        Assert.Equal(["transfer"], executed);
    }
}
