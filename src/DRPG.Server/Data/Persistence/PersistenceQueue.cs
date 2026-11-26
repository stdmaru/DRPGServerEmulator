using System.Threading.Channels;
using DRPG.Server.Data.Repositories;

namespace DRPG.Server.Data.Persistence;

/// <summary>
/// Gravações em segundo plano, em ordem. O mundo enfileira snapshots e segue sem esperar o banco.
/// Gravações do mesmo personagem são coalescidas para o snapshot mais recente pendente (evita transações redundantes).
/// Quem precisa ler o que acabou de ser salvo (entrar no mapa logo depois de sair) chama <see cref="FlushAsync"/>.
/// </summary>
public sealed class PersistenceQueue
{
    // cada item devolve o trabalho na hora de rodar: um save coalescido só decide o snapshot quando chega a vez dele
    private readonly Channel<Func<PersistenceJob?>> _jobs = Channel.CreateUnbounded<Func<PersistenceJob?>>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<uint, PersistenceJob> _pendingCharacterSaves = [];
    private readonly Lock _coalesceLock = new();

    internal ChannelReader<Func<PersistenceJob?>> Reader => _jobs.Reader;

    public void Enqueue(PersistenceJob job) => _jobs.Writer.TryWrite(() => job);

    /// <summary>
    /// Enfileira a gravação de um personagem com coalescing por <see cref="PersistenceJob.CharacterUid"/>: se já existe
    /// um snapshot aguardando na fila, ele é trocado pelo mais recente sem duplicar transações.
    /// </summary>
    public void EnqueueCharacterSave(PersistenceJob job)
    {
        var characterUid = job.CharacterUid ?? throw new ArgumentException("save de personagem sem CharacterUid", nameof(job));
        lock (_coalesceLock)
        {
            bool alreadyQueued = _pendingCharacterSaves.ContainsKey(characterUid);
            _pendingCharacterSaves[characterUid] = job;
            if (alreadyQueued) return;

            _jobs.Writer.TryWrite(() =>
            {
                lock (_coalesceLock)
                    return _pendingCharacterSaves.Remove(characterUid, out var latest) ? latest : null;
            });
        }
    }

    /// <summary>
    /// Enfileira uma transferência conta ↔ personagem (não coalescível). O snapshot dela é o mais novo do personagem,
    /// então um save coalescido do mesmo personagem que ainda espera na fila é descartado.
    /// </summary>
    public void EnqueueCharacterTransfer(PersistenceJob job)
    {
        var characterUid = job.CharacterUid ?? throw new ArgumentException("transferência sem CharacterUid", nameof(job));
        lock (_coalesceLock)
        {
            _pendingCharacterSaves.Remove(characterUid);
            _jobs.Writer.TryWrite(() => job);
        }
    }

    /// <summary>Completa quando tudo o que foi enfileirado antes desta chamada estiver gravado (ou no dead-letter).</summary>
    public Task FlushAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var marker = new PersistenceJob("flush", _ =>
        {
            done.TrySetResult();
            return Task.FromResult(false);
        });
        if (!_jobs.Writer.TryWrite(() => marker))
            done.TrySetResult();
        return done.Task;
    }

    internal void Complete() => _jobs.Writer.TryComplete();
}

public static class PersistenceQueueExtensions
{
    public static void SaveCharacter(this PersistenceQueue queue, ICharacterSnapshotStore repository, CharacterSnapshot snapshot,
        Action? committed = null) =>
        queue.EnqueueCharacterSave(PersistenceJob.SaveCharacter(repository, snapshot, committed));
}
