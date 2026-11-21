using DRPGServer.Game.Entities;

namespace DRPGServer.Game.Battle;

/// <summary>❌ Fórmulas de batalha do protótipo (inventadas). A próxima tarefa substitui.</summary>
public static class BattleFormulas
{
    public static int Damage(Digimon attacker, Digimon target)
    {
        double raw = attacker.Stats.Atk * 0.4 - target.Stats.Def * 0.22;
        return Math.Max(0, (int)Math.Round(raw, MidpointRounding.AwayFromZero));
    }

    /// <summary>Quanto a barra de ação enche por passo (200 ms).</summary>
    public static int GaugeGain(Combatant c)
    {
        if (c.Owner is null) return 10;
        return c.Digimon.Level switch
        {
            >= 30 => 101,
            >= 20 => 92,
            >= 5 => 90,
            >= 3 => 83,
            >= 2 => 70,
            _ => 38,
        };
    }
}
