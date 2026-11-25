using System.Threading.Channels;
using DRPGServer.Data.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DRPGServer.Data.Persistence;

/// <summary>
/// Gravações em segundo plano, em ordem. O mundo enfileira snapshots e segue sem esperar o banco.
/// Gravações do mesmo personagem são coalescidas para o snapshot mais recente pendente (evita transações redundantes).
/// Quem precisa ler o que acabou de ser salvo (entrar no mapa logo depois de sair) chama <see cref="FlushAsync"/>.
/// </summary>
public sealed class PersistenceQueue
{
    private readonly Channel<Func<CancellationToken, Task>> _jobs = Channel.CreateUnbounded<Func<CancellationToken, Task>>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<uint, Func<CancellationToken, Task>> _pendingCharacterSaves = [];
    private readonly Lock _coalesceLock = new();

    internal ChannelReader<Func<CancellationToken, Task>> Reader => _jobs.Reader;

    public void Enqueue(Func<CancellationToken, Task> job) => _jobs.Writer.TryWrite(job);

    /// <summary>
    /// Enfileira a gravação de um personagem com coalescing por <paramref name="characterUid"/>:
    /// se já existe um snapshot aguardando na fila, substitui pelo mais recente sem duplicar transações.
    /// </summary>
    public void EnqueueCharacterSave(uint characterUid, Func<CancellationToken, Task> job)
    {
        lock (_coalesceLock)
        {
            bool alreadyQueued = _pendingCharacterSaves.ContainsKey(characterUid);
            _pendingCharacterSaves[characterUid] = job;
            if (alreadyQueued) return;

            _jobs.Writer.TryWrite(ct =>
            {
                Func<CancellationToken, Task>? latest;
                lock (_coalesceLock)
                    _pendingCharacterSaves.Remove(characterUid, out latest);
                return latest is null ? Task.CompletedTask : latest(ct);
            });
        }
    }

    /// <summary>Completa quando tudo o que foi enfileirado antes desta chamada estiver gravado.</summary>
    public Task FlushAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_jobs.Writer.TryWrite(_ => { done.TrySetResult(); return Task.CompletedTask; }))
            done.TrySetResult();
        return done.Task;
    }

    internal void Complete() => _jobs.Writer.TryComplete();
}

/// <summary>Consome a <see cref="PersistenceQueue"/>. Para por último, depois de o mundo salvar todo mundo.</summary>
public sealed class PersistenceService(PersistenceQueue queue, ILogger<PersistenceService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        // não usa o token de parada no laço: na parada, drena o que falta e só então sai
        await foreach (var job in queue.Reader.ReadAllAsync(CancellationToken.None))
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await job(CancellationToken.None);
                    break;
                }
                catch (Exception e) when (attempt < 3)
                {
                    log.LogWarning(e, "Falha ao gravar (tentativa {Attempt}); repetindo", attempt);
                    await Task.Delay(TimeSpan.FromSeconds(attempt), CancellationToken.None);
                }
                catch (Exception e)
                {
                    log.LogError(e, "Gravação descartada depois de 3 tentativas");
                    break;
                }
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        await base.StopAsync(cancellationToken);
    }
}

public static class PersistenceQueueExtensions
{
    public static void SaveCharacter(this PersistenceQueue queue, ICharacterSnapshotStore repository, CharacterSnapshot snapshot) =>
        queue.EnqueueCharacterSave(snapshot.Uid, ct => repository.SaveAsync(snapshot, ct));
}
