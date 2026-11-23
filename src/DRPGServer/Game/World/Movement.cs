namespace DRPGServer.Game.World;

/// <summary>
/// Validação do 0x20 (docs/mecanicas/movimento.md). O cliente manda a posição a cada ~0,87 s enquanto anda.
/// </summary>
public static class Movement
{
    /// <summary>✅ Intervalo do 0x20 com o personagem andando (mediana de 1655 intervalos).</summary>
    public static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(867);

    /// <summary>✅ Maior passo visto entre dois 0x20: 9,2 células em 0,87 s (≈ 10,6 células/s). Margem até 11.</summary>
    public const double MaxCellsPerSecond = 11;

    /// <summary>Folga fixa (arredondamento do cliente, atraso de rede).</summary>
    public const double Slack = 2;

    /// <summary>Distância máxima para o intervalo <paramref name="elapsed"/> (limitado a dois envios: parado não acumula).</summary>
    public static double MaxDistance(TimeSpan elapsed)
    {
        var t = Math.Clamp(elapsed.TotalSeconds, ReportInterval.TotalSeconds, 2 * ReportInterval.TotalSeconds);
        return Slack + MaxCellsPerSecond * t;
    }

    public enum Verdict { Ok, Blocked, TooFast }

    /// <summary>Confere o passo de (fromX, fromY) para (x, y) na zona.</summary>
    public static Verdict Check(Zone zone, int fromX, int fromY, int x, int y, TimeSpan elapsed)
    {
        if (!zone.IsNearWalkable(x, y)) return Verdict.Blocked;
        double dx = x - fromX, dy = y - fromY;
        return Math.Sqrt(dx * dx + dy * dy) > MaxDistance(elapsed) ? Verdict.TooFast : Verdict.Ok;
    }
}
