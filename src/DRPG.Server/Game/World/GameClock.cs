namespace DRPG.Server.Game.World;

/// <summary>
/// Calendário do jogo (data e hora UTC): expiração de item, timestamps em pacote e no banco, validade de tickets.
/// Tempo de jogo (ticks, recargas, cooldowns) é <see cref="GameWorld.Now"/>, monotônico. Nada no servidor lê
/// <see cref="DateTimeOffset.UtcNow"/> direto: os testes trocam o relógio.
/// </summary>
public interface IGameClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Segundos Unix UTC (formato dos timestamps do protocolo e das colunas <c>expires_at</c>).</summary>
    long UnixNow => UtcNow.ToUnixTimeSeconds();
}

/// <summary>Relógio sobre um <see cref="TimeProvider"/> (o do sistema em produção).</summary>
public sealed class GameClock(TimeProvider time) : IGameClock
{
    public DateTimeOffset UtcNow => time.GetUtcNow();
}
