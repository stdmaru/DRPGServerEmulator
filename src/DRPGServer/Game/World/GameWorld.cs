using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DRPGServer.Game.World;

/// <summary>
/// A thread do mundo. Tudo o que é estado de jogo (zonas, jogadores, batalhas) só é lido e
/// alterado aqui: handlers de mapa entram por <see cref="Post"/>, e os ticks rodam a cada 50 ms.
/// Nada de trava no código de jogo; nada de I/O aqui (use <see cref="Persistence.PersistenceQueue"/>).
/// </summary>
public sealed class GameWorld(ILogger<GameWorld> log) : IHostedService
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    private readonly BlockingCollection<Action> _queue = new(new ConcurrentQueue<Action>());
    private readonly List<IWorldSystem> _systems = [];
    private readonly Stopwatch _clock = new();
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private volatile bool _stopping;

    /// <summary>Tempo de jogo desde a partida (monotônico).</summary>
    public TimeSpan Now => _clock.Elapsed;

    public Random Random { get; } = new();

    public bool IsWorldThread => Thread.CurrentThread == _thread;

    /// <summary>Agenda trabalho na thread do mundo. Seguro de qualquer thread.</summary>
    public void Post(Action work)
    {
        if (!_queue.IsAddingCompleted)
            _queue.TryAdd(work);
    }

    /// <summary>Roda na thread do mundo e devolve o resultado para quem está fora dela.</summary>
    public Task<T> InvokeAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception e) { tcs.SetException(e); }
        });
        return tcs.Task;
    }

    public void AddSystem(IWorldSystem system) => _systems.Add(system);

    [Conditional("DEBUG")]
    public void AssertWorldThread()
    {
        if (_thread is not null && !IsWorldThread)
            throw new InvalidOperationException("estado do mundo acessado fora da thread do mundo");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _clock.Start();
        _thread = new Thread(Run) { Name = "world", IsBackground = true };
        _thread.Start();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // os sistemas salvam quem está online antes de a thread parar
        Post(() =>
        {
            foreach (var system in _systems)
                Guard(() => system.Shutdown(), system);
            _stopping = true;
        });
        await Task.WhenAny(_stopped.Task, Task.Delay(Timeout.Infinite, cancellationToken));
    }

    private void Run()
    {
        log.LogInformation("Thread do mundo iniciada ({Systems} sistemas, tick {Tick} ms)", _systems.Count, TickInterval.TotalMilliseconds);
        var last = Now;
        var nextTick = last + TickInterval;
        try
        {
            while (!_stopping)
            {
                var wait = nextTick - Now;
                if (wait > TimeSpan.Zero && _queue.TryTake(out var work, wait))
                {
                    Execute(work);
                    continue;
                }

                var now = Now;
                var dt = now - last;
                last = now;
                nextTick = now + TickInterval;
                foreach (var system in _systems)
                    Guard(() => system.Update(now, dt), system);
            }
            _queue.CompleteAdding();
            while (_queue.TryTake(out var rest))
                Execute(rest);
        }
        finally
        {
            log.LogInformation("Thread do mundo parada");
            _stopped.TrySetResult();
        }
    }

    private void Execute(Action work)
    {
        try { work(); }
        catch (Exception e) { log.LogError(e, "Erro em trabalho do mundo"); }
    }

    private void Guard(Action action, IWorldSystem system)
    {
        try { action(); }
        catch (Exception e) { log.LogError(e, "Erro no sistema {System}", system.GetType().Name); }
    }
}

/// <summary>Algo que o mundo atualiza a cada tick (zonas, batalhas, autosave).</summary>
public interface IWorldSystem
{
    void Update(TimeSpan now, TimeSpan dt);

    /// <summary>Chamado na thread do mundo quando o servidor desliga.</summary>
    void Shutdown() { }
}
