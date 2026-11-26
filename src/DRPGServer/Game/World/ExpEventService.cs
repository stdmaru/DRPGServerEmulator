using DRPGServer.Configuration;
using DRPGServer.Data.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DRPGServer.Game.World;

/// <summary>
/// Mantém o evento de EXP ativo em memória, consultando <c>exp_event</c> periodicamente fora da thread do mundo
/// (e sob demanda via <c>/reloadevents</c>). Quando o estado muda, dispara <see cref="Changed"/> para que os
/// jogadores online recebam o pacote <c>Map 0xE6</c>.
/// </summary>
public sealed class ExpEventService(
    IExpEventStore store,
    IOptions<GameOptions> options,
    ILogger<ExpEventService> log) : BackgroundService
{
    private volatile ActiveExpEvent? _current;

    /// <summary>Evento atualmente ativo (null se não houver evento em vigor).</summary>
    public ActiveExpEvent? Current => _current;

    /// <summary>Multiplicador de EXP do evento ativo (1.0 quando não há evento).</summary>
    public double ExpMultiplier => _current?.ExpMultiplier ?? 1.0;

    /// <summary>Disparado fora da thread do mundo quando o evento muda ou em reload forçado.</summary>
    public event Action<ActiveExpEvent?>? Changed;

    /// <summary>Consulta o banco de dados e, se o evento mudou (ou <paramref name="forceNotify"/>), notifica os ouvintes.</summary>
    public async Task<ActiveExpEvent?> ReloadAsync(bool forceNotify = false, CancellationToken ct = default)
    {
        var next = await store.FindActiveAsync(ct);
        Apply(next, forceNotify);
        return next;
    }

    /// <summary>Aplica um novo snapshot de evento e dispara <see cref="Changed"/> quando houver mudança.</summary>
    public bool Apply(ActiveExpEvent? next, bool forceNotify = false)
    {
        var previous = _current;
        bool changed = !Equals(previous, next);
        _current = next;

        if (!changed && !forceNotify) return false;

        if (next is not null)
            log.LogInformation("Evento de EXP ativo: {Name} (+{Percent}%, até {EndsAt:yyyy-MM-dd})",
                next.Name, next.BonusPercent, next.EndsAt);
        else if (previous is not null)
            log.LogInformation("Evento de EXP encerrado: {Name}", previous.Name);

        Changed?.Invoke(next);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.EventPollSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ReloadAsync(forceNotify: false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                log.LogWarning(e, "Falha ao consultar tabela exp_event; mantendo estado atual");
            }
        }
    }
}
