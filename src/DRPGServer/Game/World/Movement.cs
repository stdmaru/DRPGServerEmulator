namespace DRPGServer.Game.World;

/// <summary>
/// Validação do 0x20 (docs/mecanicas/movimento.md).
/// ✅ Física confirmada em <c>DigimonRPG.bin @ 0x85e5b0..0x85e73b</c>:
/// cada célula mede 64×32 px (<c>0x85e848..0x85e86c</c>); em cada tick do cliente, o tamer anda
/// <c>±8 px</c> em X e/ou <c>±4 px</c> em Y no estado normal (<c>0x05</c>, <c>1/8</c> célula/tick por eixo) e
/// <c>±16 px</c> / <c>±8 px</c> com fantasia de velocidade (<c>0x0F</c>, <c>1/4</c> célula/tick = 200% da base).
/// Como X e Y avançam simultaneamente na diagonal, a distância real é Chebyshev (<c>max(|dx|, |dy|)</c>).
/// </summary>
public static class Movement
{
    /// <summary>✅ Intervalo do 0x20 com o personagem andando (mediana de 9623 intervalos em captures.db; ~40 ticks do cliente).</summary>
    public static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(867);

    /// <summary>Intervalo máximo considerado no passo (1,5× o envio normal: parado não acumula distância).</summary>
    public static readonly TimeSpan MaxElapsed = TimeSpan.FromTicks(ReportInterval.Ticks * 3 / 2);

    /// <summary>
    /// ✅ Velocidade base do tamer sem bônus, em células/s por eixo (Chebyshev).
    /// Em ~40 ticks por 0x20 (0,867 s) a 1/8 célula/tick: 5 células/envio ≈ 5,77 células/s (margem até 6,0).
    /// Com item de 200% (1/4 célula/tick): 10 células/envio ≈ 11,54 células/s (limite 12,0).
    /// </summary>
    public const double BaseMoveSpeed = 6.0;

    /// <summary>Folga fixa em células (arredondamento de grade e jitter de rede).</summary>
    public const double Slack = 1.5;

    /// <summary>Distância Chebyshev entre duas células (<c>max(|dx|, |dy|)</c>), igual ao passo por eixo do cliente.</summary>
    public static int Distance(int fromX, int fromY, int x, int y) =>
        Math.Max(Math.Abs(x - fromX), Math.Abs(y - fromY));

    /// <summary>Distância máxima para o intervalo <paramref name="elapsed"/> e a velocidade <paramref name="moveSpeed"/>.</summary>
    public static double MaxDistance(TimeSpan elapsed, double moveSpeed = BaseMoveSpeed)
    {
        var t = Math.Clamp(elapsed.TotalSeconds, ReportInterval.TotalSeconds, MaxElapsed.TotalSeconds);
        return Slack + moveSpeed * t;
    }

    public enum Verdict { Ok, Blocked, TooFast }

    /// <summary>Confere o passo de (fromX, fromY) para (x, y) na zona considerando a velocidade do jogador.</summary>
    public static Verdict Check(Zone zone, int fromX, int fromY, int x, int y, TimeSpan elapsed, double moveSpeed = BaseMoveSpeed)
    {
        if (!zone.IsNearWalkable(x, y)) return Verdict.Blocked;
        return Distance(fromX, fromY, x, y) > MaxDistance(elapsed, moveSpeed) ? Verdict.TooFast : Verdict.Ok;
    }
}
