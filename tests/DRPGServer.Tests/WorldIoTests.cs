using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

/// <summary>Contrato do <see cref="WorldIo"/> (docs/emulador/handlers.md#io-a-partir-de-um-handler-de-mundo).</summary>
public class WorldIoTests
{
    private static Player NewPlayer(out Session session)
    {
        session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), Entities.Character());
        session.Player = player;
        return player;
    }

    [Fact]
    public async Task Work_runs_off_the_world_thread_and_continuation_on_it()
    {
        await using var h = new WorldHarness();
        await h.StartAsync();
        var player = NewPlayer(out _);
        bool workOnWorld = true, thenOnWorld = false;
        int result = 0;

        await h.RunAsync(() => h.Io.Run(player, "teste",
            work: _ =>
            {
                workOnWorld = h.World.IsWorldThread;
                return Task.FromResult(42);
            },
            then: r =>
            {
                thenOnWorld = h.World.IsWorldThread;
                result = r;
            }));

        Assert.False(workOnWorld);
        Assert.True(thenOnWorld);
        Assert.Equal(42, result);
        Assert.Equal(0, h.Io.Pending);
    }

    [Fact]
    public async Task Offline_player_gets_discarded_instead_of_then()
    {
        await using var h = new WorldHarness();
        await h.StartAsync();
        var player = NewPlayer(out var session);
        var release = new TaskCompletionSource();
        bool then = false;
        int discarded = 0;

        await h.InvokeAsync(() =>
        {
            h.Io.Run(player, "teste", work: async _ =>
            {
                await release.Task;
                return 7;
            }, then: _ => then = true, discarded: r => discarded = r);
            return true;
        });
        session.Close("teste");
        release.SetResult();
        await h.SettleAsync();

        Assert.False(then);
        Assert.Equal(7, discarded);
    }

    [Fact]
    public async Task Failure_reaches_failed_only_while_online()
    {
        await using var h = new WorldHarness();
        await h.StartAsync();
        var online = NewPlayer(out _);
        var offline = NewPlayer(out var offlineSession);
        offlineSession.Close("teste");
        Exception? onlineError = null;
        bool offlineFailed = false, anyThen = false;

        await h.RunAsync(() =>
        {
            h.Io.Run<int>(online, "teste", work: _ => throw new InvalidOperationException("banco fora"),
                then: _ => anyThen = true, failed: e => onlineError = e);
            h.Io.Run<int>(offline, "teste", work: _ => throw new InvalidOperationException("banco fora"),
                then: _ => anyThen = true, failed: _ => offlineFailed = true);
        });

        Assert.IsType<InvalidOperationException>(onlineError);
        Assert.False(offlineFailed);
        Assert.False(anyThen);
    }

    [Fact]
    public async Task Exception_in_continuation_is_contained()
    {
        await using var h = new WorldHarness();
        await h.StartAsync();
        var player = NewPlayer(out _);
        bool next = false;

        await h.RunAsync(() => h.Io.Run(player, "teste", work: _ => Task.FromResult(1), then: _ => throw new InvalidOperationException()));
        await h.RunAsync(() => h.Io.Run(player, "teste", work: _ => Task.FromResult(1), then: _ => next = true));

        Assert.True(next);
        Assert.Equal(0, h.Io.Pending);
    }

    [Fact]
    public async Task Drain_waits_for_io_started_by_a_continuation()
    {
        await using var h = new WorldHarness();
        await h.StartAsync();
        var player = NewPlayer(out _);
        var release = new TaskCompletionSource();
        bool compensated = false;

        await h.InvokeAsync(() =>
        {
            h.Io.Run(player, "teste", work: _ => Task.FromResult(1), then: _ =>
                h.Io.Run("compensação", async _ =>
                {
                    await release.Task;
                    compensated = true;
                }));
            return true;
        });

        var drain = h.SettleAsync();
        await Task.Delay(50);
        Assert.False(drain.IsCompleted);
        release.SetResult();
        await drain;
        Assert.True(compensated);
    }

    [Fact]
    public async Task Stop_cancels_work_that_outlives_the_shutdown_timeout()
    {
        var world = TestSupport.NewWorld();
        await world.StartAsync(CancellationToken.None);
        var io = new WorldIo(world, NullLogger<WorldIo>.Instance);
        bool cancelled = false;
        try
        {
            await world.InvokeAsync(() =>
            {
                io.Run("lenta", async ct =>
                {
                    try { await Task.Delay(Timeout.Infinite, ct); }
                    catch (OperationCanceledException) { cancelled = true; throw; }
                });
                return true;
            });

            await io.StopAsync(new CancellationTokenSource(TimeSpan.FromMilliseconds(50)).Token);
            await io.DrainAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
            Assert.True(cancelled);
        }
        finally
        {
            await world.StopAsync(CancellationToken.None);
        }
    }
}
