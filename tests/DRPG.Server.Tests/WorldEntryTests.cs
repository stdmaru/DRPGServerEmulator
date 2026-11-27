using DRPG.Server.Data.Persistence;
using DRPG.Server.Data.Repositories;
using DRPG.Server.Game.Entities;
using DRPG.Server.Game.World;
using DRPG.Server.Network;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPG.Server.Tests;

/// <summary>Estado da conta na entrada e saída do mundo (docs/emulador/seguranca.md#entrada-e-saída-do-mundo).</summary>
public class WorldEntryTests
{
    private static readonly AuthenticatedAccount Account = new(1, "tester", Authority.None);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>Gravação que pode ser segurada, para ver quem espera por ela.</summary>
    private sealed class GatedSnapshotStore : ICharacterSnapshotStore
    {
        private volatile TaskCompletionSource _gate = Open();
        public List<CharacterSnapshot> Saved { get; } = [];

        public void Hold() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => _gate.TrySetResult();

        public async Task SaveAsync(CharacterSnapshot snapshot, CancellationToken ct)
        {
            await _gate.Task;
            lock (Saved) Saved.Add(snapshot);
        }

        public double? LastBits()
        {
            lock (Saved) return Saved.Count == 0 ? null : Saved[^1].Bits;
        }

        private static TaskCompletionSource Open()
        {
            var tcs = new TaskCompletionSource();
            tcs.SetResult();
            return tcs;
        }
    }

    /// <summary>"Banco" que devolve o personagem com os Bits do último snapshot gravado.</summary>
    private sealed class FakeLoader(GatedSnapshotStore store) : ICharacterLoader
    {
        private volatile TaskCompletionSource? _gate;
        public int Calls;
        public bool ReturnNothing { get; set; }

        public void Hold() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => _gate?.TrySetResult();

        public async Task<Character?> LoadAsync(uint accountUid, string name, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            if (_gate is { } gate) await gate.Task.WaitAsync(ct);
            if (ReturnNothing) return null;
            var character = Entities.Character();
            character.SetBits(store.LastBits() ?? 0);
            return character;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public WorldHarness Harness { get; } = new();
        public GatedSnapshotStore Store { get; } = new();
        public FakeLoader Loader { get; }
        public PlayerSystem Players { get; }
        public WorldEntry Entry { get; }
        private readonly PersistenceQueue _queue = new();
        private readonly PersistenceService _persistence;

        public Fixture()
        {
            var world = Harness.World;
            Loader = new FakeLoader(Store);
            var zones = new ZoneSystem(TestSupport.Data, world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
            Players = new PlayerSystem(world, zones, TestSupport.NewBattles(world), _queue, Store, TestSupport.GameOptions(),
                NullLogger<PlayerSystem>.Instance);
            Entry = new WorldEntry(world, Players, Loader, Harness.DeadLetters, NullLogger<WorldEntry>.Instance);
            _persistence = TestSupport.NewPersistenceService(_queue, Harness.DeadLetters);
        }

        public async Task StartAsync()
        {
            await Harness.StartAsync();
            await _persistence.StartAsync(CancellationToken.None);
        }

        /// <summary>Entra e espera o jogador estar no mundo.</summary>
        public async Task<(bool Ok, Task<Player> Entered)> EnterAsync(Session session)
        {
            var entered = new TaskCompletionSource<Player>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool ok = await Entry.EnterAsync(session, Account, "Tester", p => entered.TrySetResult(p), CancellationToken.None);
            return (ok, entered.Task);
        }

        public Task<int> OnlineAsync() => Harness.InvokeAsync(() => Players.Count);

        public async ValueTask DisposeAsync()
        {
            Store.Release();
            Loader.Release();
            await Harness.StopAsync();
            await _persistence.StopAsync(CancellationToken.None);
        }
    }

    private static Session NewMapSession()
    {
        var session = TestSupport.NewSession(ServerRole.Map);
        session.Account = Account;
        return session;
    }

    [Fact]
    public async Task Relogin_waits_for_the_previous_save_before_loading()
    {
        await using var f = new Fixture();
        await f.StartAsync();

        var first = NewMapSession();
        var (ok, entered) = await f.EnterAsync(first);
        Assert.True(ok);
        var player = await entered.WaitAsync(Wait);
        await f.Harness.InvokeAsync(() => { player.Character.SetBits(1234); return true; });

        f.Store.Hold();                                          // o save do logout fica preso no banco
        var second = NewMapSession();
        var relogin = f.EnterAsync(second);

        await WaitUntil(() => first.IsClosed);
        await Task.Delay(100);
        Assert.Equal(1, f.Loader.Calls);                         // não carregou com o save pendente
        Assert.False(relogin.IsCompleted);

        f.Store.Release();
        var (reloginOk, reentered) = await relogin.WaitAsync(Wait);
        Assert.True(reloginOk);
        Assert.Equal(1234, (await reentered.WaitAsync(Wait)).Character.Wallet.Bits);
        Assert.Equal(1, await f.OnlineAsync());
    }

    [Fact]
    public async Task Concurrent_entry_of_the_same_account_is_refused()
    {
        await using var f = new Fixture();
        await f.StartAsync();

        f.Loader.Hold();
        var first = NewMapSession();
        var loading = f.EnterAsync(first);
        await WaitUntil(() => f.Loader.Calls == 1);

        var second = NewMapSession();
        var (ok, _) = await f.EnterAsync(second);
        Assert.False(ok);
        Assert.False(first.IsClosed);

        f.Loader.Release();
        var (firstOk, entered) = await loading.WaitAsync(Wait);
        Assert.True(firstOk);
        Assert.Same(first, (await entered.WaitAsync(Wait)).Session);
        Assert.Equal(1, f.Loader.Calls);
    }

    [Fact]
    public async Task Entry_whose_session_closed_during_load_never_enters_the_world()
    {
        await using var f = new Fixture();
        await f.StartAsync();

        f.Loader.Hold();
        var first = NewMapSession();
        var loading = f.EnterAsync(first);
        await WaitUntil(() => f.Loader.Calls == 1);

        first.Close("teste");
        await f.Harness.RunAsync(() => f.Players.Leave(first));   // o que o MapLifecycle faz ao desconectar
        f.Loader.Release();
        var (_, entered) = await loading.WaitAsync(Wait);
        await f.Harness.InvokeAsync(() => true);                  // a conclusão postada já rodou
        Assert.False(entered.IsCompleted);
        Assert.Equal(0, await f.OnlineAsync());

        // a conta volta a poder entrar
        var (ok, reentered) = await f.EnterAsync(NewMapSession());
        Assert.True(ok);
        await reentered.WaitAsync(Wait);
    }

    [Fact]
    public async Task Character_in_the_dead_letter_cannot_enter_until_it_is_resolved()
    {
        await using var f = new Fixture();
        await f.StartAsync();
        var character = Entities.Character();
        f.Harness.DeadLetters.Add(CharacterSnapshot.Of(character), "character.save", new TimeoutException("banco fora"));

        var (blocked, _) = await f.EnterAsync(NewMapSession());
        Assert.False(blocked);
        Assert.Equal(0, await f.OnlineAsync());

        f.Harness.DeadLetters.Resolve(character.Uid);
        var (ok, entered) = await f.EnterAsync(NewMapSession());
        Assert.True(ok);
        await entered.WaitAsync(Wait);
    }

    [Fact]
    public async Task Failed_load_releases_the_account()
    {
        await using var f = new Fixture();
        await f.StartAsync();

        f.Loader.ReturnNothing = true;
        var (ok, _) = await f.EnterAsync(NewMapSession());
        Assert.False(ok);

        f.Loader.ReturnNothing = false;
        var (retryOk, entered) = await f.EnterAsync(NewMapSession());
        Assert.True(retryOk);
        await entered.WaitAsync(Wait);
    }

    [Fact]
    public async Task Entry_while_logout_save_is_pending_waits_for_it()
    {
        await using var f = new Fixture();
        await f.StartAsync();

        var first = NewMapSession();
        var (_, entered) = await f.EnterAsync(first);
        var player = await entered.WaitAsync(Wait);
        await f.Harness.InvokeAsync(() => { player.Character.SetBits(77); return true; });

        f.Store.Hold();
        first.Close("logout");
        await f.Harness.RunAsync(() => f.Players.Leave(first));   // conta em Saving

        var relogin = f.EnterAsync(NewMapSession());
        await Task.Delay(100);
        Assert.Equal(1, f.Loader.Calls);

        f.Store.Release();
        var (ok, reentered) = await relogin.WaitAsync(Wait);
        Assert.True(ok);
        Assert.Equal(77, (await reentered.WaitAsync(Wait)).Character.Wallet.Bits);
    }

    // --- apagar personagem (ReleaseForDeleteAsync) ---

    [Fact]
    public async Task Delete_is_allowed_when_the_account_has_nothing_in_the_world()
    {
        await using var f = new Fixture();
        await f.StartAsync();

        Assert.True(await f.Players.ReleaseForDeleteAsync(Account.Uid, Entities.Character().Uid).WaitAsync(Wait));
    }

    [Fact]
    public async Task Delete_is_refused_while_the_character_is_in_the_world()
    {
        await using var f = new Fixture();
        await f.StartAsync();
        var session = NewMapSession();
        var (_, entered) = await f.EnterAsync(session);
        var player = await entered.WaitAsync(Wait);

        Assert.False(await f.Players.ReleaseForDeleteAsync(Account.Uid, player.Character.Uid).WaitAsync(Wait));
        Assert.Equal(1, await f.OnlineAsync());                      // recusar não tira ninguém do mundo
    }

    [Fact]
    public async Task Delete_of_another_character_is_allowed_while_one_is_in_the_world()
    {
        await using var f = new Fixture();
        await f.StartAsync();
        var (_, entered) = await f.EnterAsync(NewMapSession());
        var player = await entered.WaitAsync(Wait);

        Assert.True(await f.Players.ReleaseForDeleteAsync(Account.Uid, player.Character.Uid + 1).WaitAsync(Wait));
    }

    [Fact]
    public async Task Delete_is_refused_while_the_account_is_loading_into_the_world()
    {
        await using var f = new Fixture();
        await f.StartAsync();
        f.Loader.Hold();
        var loading = f.EnterAsync(NewMapSession());
        await WaitUntil(() => f.Loader.Calls == 1);

        Assert.False(await f.Players.ReleaseForDeleteAsync(Account.Uid, Entities.Character().Uid).WaitAsync(Wait));

        f.Loader.Release();
        await (await loading.WaitAsync(Wait)).Entered.WaitAsync(Wait);
    }

    [Fact]
    public async Task Delete_waits_for_the_save_of_the_exit_before_it_goes_on()
    {
        await using var f = new Fixture();
        await f.StartAsync();
        var session = NewMapSession();
        var (_, entered) = await f.EnterAsync(session);
        var player = await entered.WaitAsync(Wait);

        f.Store.Hold();                                              // o save de saída fica preso no banco
        session.Close("logout");
        await f.Harness.RunAsync(() => f.Players.Leave(session));    // conta em Saving
        var delete = f.Players.ReleaseForDeleteAsync(Account.Uid, player.Character.Uid);
        await Task.Delay(100);
        Assert.False(delete.IsCompleted);                            // apagar agora quebraria o save

        f.Store.Release();
        Assert.True(await delete.WaitAsync(Wait));
        Assert.Single(f.Store.Saved);                                // o save chegou antes de o delete poder seguir
    }

    [Fact]
    public async Task Delete_after_the_connection_dropped_leaves_the_world_and_waits_for_the_save()
    {
        await using var f = new Fixture();
        await f.StartAsync();
        var session = NewMapSession();
        var (_, entered) = await f.EnterAsync(session);
        var player = await entered.WaitAsync(Wait);

        f.Store.Hold();
        session.Close("conexão caiu");                               // o Leave do MapLifecycle ainda não rodou
        var delete = f.Players.ReleaseForDeleteAsync(Account.Uid, player.Character.Uid);
        await WaitUntil(() => f.OnlineAsync().Result == 0);          // o delete tirou o jogador do mundo
        Assert.False(delete.IsCompleted);

        f.Store.Release();
        Assert.True(await delete.WaitAsync(Wait));
        await f.Harness.RunAsync(() => f.Players.Leave(session));    // o Leave tardio do ciclo de vida não faz nada
        await f.Harness.InvokeAsync(() => true);
        Assert.Single(f.Store.Saved);                                // um save só, sem duplicar
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condição não chegou a tempo");
            await Task.Delay(10);
        }
    }
}
