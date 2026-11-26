using DRPG.Server.Game.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DRPG.Server.Game.World;

/// <summary>
/// Único caminho de I/O (banco, serviços externos) a partir da thread do mundo (docs/emulador/handlers.md).
/// <list type="bullet">
/// <item>O trabalho roda no pool de threads, nunca na thread do mundo.</item>
/// <item>A continuação volta para a thread do mundo. Ligada a um jogador, ela só roda se ele continuar online; se
/// não, o resultado vai para <c>discarded</c>, para quem precisa desfazer algo na conta.</item>
/// <item>Exceção do trabalho ou da continuação é registrada aqui, com a operação e o jogador.</item>
/// <item>Na parada do servidor, <see cref="StopAsync"/> espera o I/O pendente (e as continuações dele) antes de o
/// mundo salvar os jogadores.</item>
/// </list>
/// </summary>
public sealed class WorldIo(GameWorld world, ILogger<WorldIo> log) : IHostedService
{
    private readonly CancellationTokenSource _abort = new();
    private readonly object _gate = new();
    private int _pending;
    private TaskCompletionSource _idle = NewIdle(completed: true);

    /// <summary>Operações iniciadas cuja continuação ainda não rodou.</summary>
    public int Pending => Volatile.Read(ref _pending);

    /// <summary>I/O ligado a um jogador, com resultado.</summary>
    /// <param name="operation">Nome curto para o log (ex.: <c>cash-shop.buy</c>).</param>
    /// <param name="work">Roda fora da thread do mundo. Não toca em estado de jogo.</param>
    /// <param name="then">Na thread do mundo, se o jogador continua online.</param>
    /// <param name="failed">Na thread do mundo, se o trabalho falhou e o jogador continua online.</param>
    /// <param name="discarded">Na thread do mundo, se o trabalho terminou mas o jogador já saiu.</param>
    public void Run<T>(Player player, string operation, Func<CancellationToken, Task<T>> work, Action<T> then,
        Action<Exception>? failed = null, Action<T>? discarded = null)
    {
        Start(operation, player, work, result =>
        {
            if (player.IsOnline) then(result);
            else discarded?.Invoke(result);
        }, e =>
        {
            if (player.IsOnline) failed?.Invoke(e);
        });
    }

    /// <summary>I/O ligado a um jogador, sem resultado.</summary>
    public void Run(Player player, string operation, Func<CancellationToken, Task> work, Action then,
        Action<Exception>? failed = null) =>
        Run(player, operation, async ct =>
        {
            await work(ct);
            return true;
        }, _ => then(), failed);

    /// <summary>
    /// I/O sem jogador: compensações e tarefas do servidor. A falha só é registrada (nível de erro), então o
    /// <paramref name="operation"/> deve dizer o que ficou por fazer.
    /// </summary>
    public void Run(string operation, Func<CancellationToken, Task> work) =>
        Start(operation, null, async ct =>
        {
            await work(ct);
            return true;
        }, _ => { }, _ => { });

    private void Start<T>(string operation, Player? player, Func<CancellationToken, Task<T>> work,
        Action<T> completed, Action<Exception> faulted)
    {
        Enter();
        _ = Task.Run(async () =>
        {
            try
            {
                T result;
                try
                {
                    result = await work(_abort.Token);
                }
                catch (Exception e)
                {
                    LogFailure(e, operation, player);
                    await world.InvokeAsync(() => Guard(() => faulted(e), operation, player));
                    return;
                }
                await world.InvokeAsync(() => Guard(() => completed(result), operation, player));
            }
            finally
            {
                Exit();
            }
        });
    }

    private bool Guard(Action continuation, string operation, Player? player)
    {
        try
        {
            continuation();
        }
        catch (Exception e)
        {
            log.LogError(e, "Erro na continuação de {Operation} ({Player})", operation, player?.ToString() ?? "servidor");
        }
        return true;
    }

    private void LogFailure(Exception e, string operation, Player? player)
    {
        if (player is null)
            log.LogError(e, "Falha de I/O em {Operation} (servidor)", operation);
        else
            log.LogWarning(e, "Falha de I/O em {Operation} ({Player})", operation, player);
    }

    /// <summary>Espera não haver I/O pendente, incluindo o que as continuações iniciarem.</summary>
    public Task DrainAsync(CancellationToken ct = default)
    {
        Task idle;
        lock (_gate) idle = _idle.Task;
        return idle.WaitAsync(ct);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await DrainAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            log.LogError("Parada sem esperar {Pending} operações de I/O; cancelando", Pending);
            await _abort.CancelAsync();
        }
    }

    private void Enter()
    {
        lock (_gate)
        {
            if (_pending++ == 0)
                _idle = NewIdle(completed: false);
        }
    }

    private void Exit()
    {
        TaskCompletionSource? idle = null;
        lock (_gate)
        {
            if (--_pending == 0)
                idle = _idle;
        }
        idle?.TrySetResult();
    }

    private static TaskCompletionSource NewIdle(bool completed)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (completed) tcs.SetResult();
        return tcs;
    }
}
