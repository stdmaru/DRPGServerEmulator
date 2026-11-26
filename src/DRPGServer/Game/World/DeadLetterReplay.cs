using System.Collections.Concurrent;
using DRPGServer.Configuration;
using DRPGServer.Data.Persistence;
using DRPGServer.Data.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DRPGServer.Game.World;

/// <summary>
/// Reaplica o dead-letter (<see cref="DeadLetterStore"/>) na partida e a cada
/// <see cref="PersistenceOptions.ReplaySeconds"/>. Só de personagem offline: com ele no mundo, o estado em memória é o
/// mais novo e o próximo save resolve. Offline e bloqueado, ele não entra até a reaplicação dar certo. A reaplicação
/// passa pela <see cref="PersistenceQueue"/>, atrás de qualquer save mais novo do mesmo personagem, e lê o snapshot só
/// na hora de gravar.
/// </summary>
public sealed class DeadLetterReplay(
    DeadLetterStore deadLetters,
    PersistenceQueue queue,
    ICharacterSnapshotStore characters,
    GameWorld world,
    PlayerSystem players,
    IOptions<PersistenceOptions> options,
    TimeProvider time,
    ILogger<DeadLetterReplay> log) : BackgroundService
{
    private readonly ConcurrentDictionary<uint, bool> _queued = new();

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.ReplaySeconds), time);
        try
        {
            do
            {
                await ReplayAsync(stopping);
            }
            while (await timer.WaitForNextTickAsync(stopping));
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Enfileira a reaplicação de cada personagem bloqueado e offline (uma por vez por personagem).</summary>
    public async Task ReplayAsync(CancellationToken ct)
    {
        foreach (var uid in deadLetters.Blocked)
        {
            if (_queued.ContainsKey(uid)) continue;
            if (await world.InvokeAsync(() => players.IsOnline(uid)).WaitAsync(ct)) continue;
            _queued[uid] = true;
            queue.Enqueue(new PersistenceJob("dead-letter.replay", async jobCt =>
            {
                try
                {
                    if (deadLetters.Find(uid) is not { } snapshot) return false;   // um save mais novo já resolveu
                    await characters.SaveAsync(snapshot, jobCt);
                    log.LogWarning("Dead-letter do personagem {Uid} reaplicado", uid);
                    return true;
                }
                finally
                {
                    _queued.TryRemove(uid, out _);
                }
            })
            {
                CharacterUid = uid,
            });
        }
    }
}
