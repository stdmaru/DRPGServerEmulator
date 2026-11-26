using DRPGServer.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DRPGServer.Data.Persistence;

/// <summary>
/// Consome a <see cref="PersistenceQueue"/>, em ordem. Para por último, depois de o mundo salvar todo mundo. Nada é
/// descartado em silêncio (docs/emulador/seguranca.md#persistência):
/// <list type="bullet">
/// <item>Falha é repetida com espera dobrando (<see cref="PersistenceOptions.RetryBaseMilliseconds"/> até
/// <see cref="PersistenceOptions.RetryMaxMilliseconds"/>), até <see cref="PersistenceOptions.MaxAttempts"/>.</item>
/// <item>Esgotadas as tentativas, o snapshot vai para o <see cref="DeadLetterStore"/> e o personagem fica bloqueado.</item>
/// <item>Depois de uma desistência (banco fora), cada trabalho tem uma tentativa só até uma gravação dar certo: a fila
/// não fica parada minutos esperando, e os snapshots vão logo para o dead-letter. Na parada, também uma só.</item>
/// </list>
/// </summary>
public sealed class PersistenceService(
    PersistenceQueue queue,
    DeadLetterStore deadLetters,
    IOptions<PersistenceOptions> options,
    TimeProvider time,
    ILogger<PersistenceService> log) : BackgroundService
{
    private bool _outage;

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        // não usa o token de parada para ler: na parada, drena o que falta e só então sai
        await foreach (var next in queue.Reader.ReadAllAsync(CancellationToken.None))
        {
            if (next() is { } job)
                await RunAsync(job, stopping);
        }
    }

    private async Task RunAsync(PersistenceJob job, CancellationToken stopping)
    {
        var o = options.Value;
        for (int attempt = 1; ; attempt++)
        {
            int maxAttempts = _outage || stopping.IsCancellationRequested ? 1 : o.MaxAttempts;
            try
            {
                bool wroteCharacter = await job.Run(CancellationToken.None);
                _outage = false;
                if (wroteCharacter)
                    Committed(job);
                return;
            }
            catch (Exception e) when (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(Math.Min(o.RetryMaxMilliseconds, o.RetryBaseMilliseconds * Math.Pow(2, attempt - 1)));
                log.LogWarning(e, "Falha ao gravar {Job} (tentativa {Attempt} de {Max}); repetindo em {Delay} ms",
                    job.Name, attempt, maxAttempts, delay.TotalMilliseconds);
                try
                {
                    await Task.Delay(delay, time, stopping);
                }
                catch (OperationCanceledException)
                {
                    // parando: a próxima tentativa é a última
                }
            }
            catch (Exception e)
            {
                _outage = true;
                GiveUp(job, attempt, e);
                return;
            }
        }
    }

    private void Committed(PersistenceJob job)
    {
        if (job.CharacterUid is { } uid)
            deadLetters.Resolve(uid);
        try
        {
            job.Committed?.Invoke();
        }
        catch (Exception e)
        {
            log.LogError(e, "Erro depois de gravar {Job}", job.Name);
        }
    }

    private void GiveUp(PersistenceJob job, int attempts, Exception error)
    {
        if (job.DeadLetter is { } snapshot)
        {
            deadLetters.Add(snapshot, job.Name, error);
            return;
        }
        if (job.CharacterUid is { } uid && deadLetters.IsBlocked(uid))
        {
            log.LogError(error, "{Job} do personagem {Uid} falhou {Attempts} vez(es); continua no dead-letter", job.Name, uid, attempts);
            return;
        }
        log.LogCritical(error, "{Job} perdido depois de {Attempts} tentativa(s): não tem snapshot para o dead-letter", job.Name, attempts);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        await base.StopAsync(cancellationToken);
    }
}
