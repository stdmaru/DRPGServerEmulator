using DRPGServer.Data.Persistence;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

/// <summary>Persistência sem descarte silencioso (S7): repetição, dead-letter, bloqueio e reaplicação.</summary>
public class DeadLetterTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>"Banco" que falha enquanto <see cref="Down"/> está ligado.</summary>
    private sealed class FlakyStore : ICharacterSnapshotStore
    {
        public volatile bool Down;
        public int FailuresLeft;
        public int Attempts;
        public List<CharacterSnapshot> Saved { get; } = [];

        public Task SaveAsync(CharacterSnapshot snapshot, CancellationToken ct)
        {
            Interlocked.Increment(ref Attempts);
            if (Down || Interlocked.Decrement(ref FailuresLeft) >= 0)
                throw new TimeoutException("banco fora");
            lock (Saved) Saved.Add(snapshot);
            return Task.CompletedTask;
        }
    }

    private static CharacterSnapshot Snapshot(uint uid = 1, double bits = 100)
    {
        var character = Entities.Character();
        character.SetBits(bits);
        return CharacterSnapshot.Of(character) with { Uid = uid };
    }

    private static async Task RunAsync(PersistenceQueue queue, DeadLetterStore deadLetters, Action<PersistenceQueue> enqueue,
        Action<Configuration.PersistenceOptions>? configure = null)
    {
        var service = TestSupport.NewPersistenceService(queue, deadLetters, configure);
        await service.StartAsync(CancellationToken.None);
        enqueue(queue);
        await queue.FlushAsync().WaitAsync(Wait);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Transient_failure_is_retried_until_it_succeeds()
    {
        var store = new FlakyStore { FailuresLeft = 3 };
        var deadLetters = TestSupport.DeadLetters();

        await RunAsync(new PersistenceQueue(), deadLetters, q => q.SaveCharacter(store, Snapshot()));

        Assert.Equal(4, store.Attempts);
        Assert.Single(store.Saved);
        Assert.False(deadLetters.IsBlocked(1));
    }

    [Fact]
    public async Task Exhausted_save_goes_to_the_dead_letter_and_blocks_the_character()
    {
        var store = new FlakyStore { Down = true };
        var deadLetters = TestSupport.DeadLetters();

        await RunAsync(new PersistenceQueue(), deadLetters, q => q.SaveCharacter(store, Snapshot(bits: 777)));

        Assert.Equal(5, store.Attempts);
        Assert.True(deadLetters.IsBlocked(1));
        Assert.Equal(777, deadLetters.Find(1)!.Bits);
        Assert.True(File.Exists(Path.Combine(deadLetters.Directory, "character-1.json")));
    }

    [Fact]
    public async Task During_an_outage_each_job_gets_a_single_attempt()
    {
        var store = new FlakyStore { Down = true };
        var deadLetters = TestSupport.DeadLetters();

        await RunAsync(new PersistenceQueue(), deadLetters, q =>
        {
            q.SaveCharacter(store, Snapshot(uid: 1));
            q.SaveCharacter(store, Snapshot(uid: 2));
            q.SaveCharacter(store, Snapshot(uid: 3));
        });

        Assert.Equal(5 + 1 + 1, store.Attempts);
        Assert.Equal([1u, 2u, 3u], deadLetters.Blocked.Order());
    }

    [Fact]
    public async Task Outage_ends_with_the_first_success()
    {
        var store = new FlakyStore { Down = true };
        var deadLetters = TestSupport.DeadLetters();
        var queue = new PersistenceQueue();
        var service = TestSupport.NewPersistenceService(queue, deadLetters);
        await service.StartAsync(CancellationToken.None);

        queue.SaveCharacter(store, Snapshot(uid: 1));
        await queue.FlushAsync().WaitAsync(Wait);
        store.Down = false;
        queue.SaveCharacter(store, Snapshot(uid: 2));
        store.FailuresLeft = 2;                         // depois do sucesso, volta a ter todas as tentativas
        queue.SaveCharacter(store, Snapshot(uid: 3));
        await queue.FlushAsync().WaitAsync(Wait);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal([2u, 3u], store.Saved.Select(s => s.Uid));
        Assert.Equal([1u], deadLetters.Blocked);
    }

    [Fact]
    public async Task Newer_successful_save_resolves_the_dead_letter()
    {
        var store = new FlakyStore { Down = true };
        var deadLetters = TestSupport.DeadLetters();
        var queue = new PersistenceQueue();
        var service = TestSupport.NewPersistenceService(queue, deadLetters);
        await service.StartAsync(CancellationToken.None);

        queue.SaveCharacter(store, Snapshot(bits: 1));
        await queue.FlushAsync().WaitAsync(Wait);
        Assert.True(deadLetters.IsBlocked(1));

        store.Down = false;
        queue.SaveCharacter(store, Snapshot(bits: 2));
        await queue.FlushAsync().WaitAsync(Wait);
        await service.StopAsync(CancellationToken.None);

        Assert.False(deadLetters.IsBlocked(1));
        Assert.False(File.Exists(Path.Combine(deadLetters.Directory, "character-1.json")));
    }

    [Fact]
    public async Task Committed_transfer_resolves_the_dead_letter()
    {
        // o snapshot da transferência é mais novo: reaplicar o antigo depois desfaria a parte do personagem
        var deadLetters = TestSupport.DeadLetters();
        deadLetters.Add(Snapshot(), "character.save", new TimeoutException());

        await RunAsync(new PersistenceQueue(), deadLetters, q => q.EnqueueCharacterTransfer(
            new PersistenceJob("transfer", _ => Task.FromResult(true)) { CharacterUid = 1 }));

        Assert.False(deadLetters.IsBlocked(1));
    }

    [Fact]
    public async Task Refused_transfer_keeps_the_dead_letter()
    {
        var deadLetters = TestSupport.DeadLetters();
        deadLetters.Add(Snapshot(), "character.save", new TimeoutException());

        await RunAsync(new PersistenceQueue(), deadLetters, q => q.EnqueueCharacterTransfer(
            new PersistenceJob("transfer", _ => Task.FromResult(false)) { CharacterUid = 1 }));

        Assert.True(deadLetters.IsBlocked(1));
    }

    [Fact]
    public void Dead_letter_survives_a_restart()
    {
        var first = TestSupport.DeadLetters();
        first.Add(Snapshot(uid: 42, bits: 9000), "character.save", new TimeoutException("banco fora"));

        var restarted = TestSupport.DeadLetters(first.Directory);
        Assert.True(restarted.IsBlocked(42));
        Assert.Equal(9000, restarted.Find(42)!.Bits);
        Assert.Equal(Snapshot(uid: 42, bits: 9000).Items.Count, restarted.Find(42)!.Items.Count);
    }

    [Fact]
    public void Unreadable_dead_letter_stops_the_boot()
    {
        var store = TestSupport.DeadLetters();
        File.WriteAllText(Path.Combine(store.Directory, "character-5.json"), "{ quebrado");
        var e = Assert.Throws<InvalidOperationException>(() => TestSupport.DeadLetters(store.Directory));
        Assert.Contains("character-5.json", e.Message);
    }

    [Fact]
    public void Dead_letter_file_of_another_character_stops_the_boot()
    {
        var store = TestSupport.DeadLetters();
        store.Add(Snapshot(uid: 7), "character.save", new TimeoutException());
        File.Move(Path.Combine(store.Directory, "character-7.json"), Path.Combine(store.Directory, "character-8.json"));
        Assert.Throws<InvalidOperationException>(() => TestSupport.DeadLetters(store.Directory));
    }

    [Fact]
    public async Task Replay_writes_offline_characters_and_skips_online_ones()
    {
        await using var harness = new WorldHarness();
        await harness.StartAsync();
        var store = new FlakyStore();
        var zones = new ZoneSystem(TestSupport.Data, harness.World, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
        var players = new PlayerSystem(harness.World, zones, TestSupport.NewBattles(harness.World), harness.Persistence, store,
            TestSupport.GameOptions(), NullLogger<PlayerSystem>.Instance);
        var replay = new DeadLetterReplay(harness.DeadLetters, harness.Persistence, store, harness.World, players,
            TestSupport.PersistenceOptions(), TimeProvider.System, NullLogger<DeadLetterReplay>.Instance);

        var online = new Character(Entities.Digimon(), TestSupport.EmptyInventory(999))
        {
            Uid = 2, AccountUid = 2, Slot = 0, Name = "Online", TamerId = 1, MapId = 79, X = 56, Y = 56,
        };
        await harness.InvokeAsync(() => players.EnterNow(TestSupport.NewSession(ServerRole.Map), new AuthenticatedAccount(2, "b", Authority.None), online));
        harness.DeadLetters.Add(Snapshot(uid: 1, bits: 10), "character.save", new TimeoutException());
        harness.DeadLetters.Add(Snapshot(uid: 2, bits: 20), "character.save", new TimeoutException());

        await replay.ReplayAsync(CancellationToken.None);
        await harness.SettleAsync();

        Assert.Equal([1u], store.Saved.Select(s => s.Uid));
        Assert.Equal(10, store.Saved[0].Bits);
        Assert.Equal([2u], harness.DeadLetters.Blocked);
    }

    [Fact]
    public async Task Failed_replay_keeps_the_character_blocked()
    {
        await using var harness = new WorldHarness();
        await harness.StartAsync();
        var store = new FlakyStore { Down = true };
        var zones = new ZoneSystem(TestSupport.Data, harness.World, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
        var players = new PlayerSystem(harness.World, zones, TestSupport.NewBattles(harness.World), harness.Persistence, store,
            TestSupport.GameOptions(), NullLogger<PlayerSystem>.Instance);
        var replay = new DeadLetterReplay(harness.DeadLetters, harness.Persistence, store, harness.World, players,
            TestSupport.PersistenceOptions(), TimeProvider.System, NullLogger<DeadLetterReplay>.Instance);
        harness.DeadLetters.Add(Snapshot(uid: 1, bits: 10), "character.save", new TimeoutException());

        await replay.ReplayAsync(CancellationToken.None);
        await harness.SettleAsync();

        Assert.True(harness.DeadLetters.IsBlocked(1));
        Assert.Equal(10, harness.DeadLetters.Find(1)!.Bits);

        store.Down = false;
        await replay.ReplayAsync(CancellationToken.None);
        await harness.SettleAsync();
        Assert.False(harness.DeadLetters.IsBlocked(1));
    }

    [Fact]
    public void Persistence_defaults_are_valid()
    {
        Assert.Empty(new Configuration.PersistenceOptionsValidator().Validate(null, new Configuration.PersistenceOptions()).Failures ?? []);
        var bad = new Configuration.PersistenceOptions { MaxAttempts = 0, RetryBaseMilliseconds = 500, RetryMaxMilliseconds = 100, DeadLetterPath = " " };
        Assert.Equal(3, new Configuration.PersistenceOptionsValidator().Validate(null, bad).Failures!.Count());
    }
}
