using DRPGServer.Data.GameData;

namespace DRPGServer.Game.Stats;

public readonly record struct Attributes(int Str, int Agi, int Con, int Int)
{
    public static Attributes operator +(Attributes a, Attributes b) =>
        new(a.Str + b.Str, a.Agi + b.Agi, a.Con + b.Con, a.Int + b.Int);
}

public readonly record struct CombatStats(int MaxHp, int MaxVp, int MaxEvp, int Atk, int Def, int Br, int MaxActionGauge);

/// <summary>
/// Todas as fórmulas de status num lugar só (ver docs/mecanicas/formulas-status.md).
/// Só MaxEVP está confirmada. O resto é palpite do protótipo e erra até ~100 pontos:
/// é o alvo da próxima tarefa.
/// </summary>
public static class StatFormulas
{
    /// <summary>✅ Confirmada nas 19 amostras.</summary>
    public static int MaxEvp(int level) => level * (level - 1);

    /// <summary>✅ Medidor de ação máximo: 500 em todas as amostras, de qualquer espécie.</summary>
    public const int MaxActionGauge = 500;

    /// <summary>❓ Bônus por atributo a partir de rookie (só rookie confirmado nas capturas).</summary>
    public const int RookieBonus = 20;

    /// <summary>
    /// 10 em cada atributo no nível 1 e o crescimento da dex (<see cref="DigimonSpecies.Growth"/>) por nível.
    /// ✅ in-training; rookie soma +20 (❓ champion em diante). Espécie sem dex não cresce.
    /// </summary>
    public static Attributes BaseAttributes(DigimonSpecies species, int level)
    {
        int levels = Math.Max(0, level - 1);
        var g = species.Growth ?? default;
        int bonus = species.Stage >= 2 ? RookieBonus : 0;
        return new(10 + bonus + g.Str * levels, 10 + bonus + g.Agi * levels,
                   10 + bonus + g.Con * levels, 10 + bonus + g.Int * levels);
    }

    /// <summary>❌ Coeficientes do protótipo, sabidamente errados.</summary>
    public static CombatStats Combat(Attributes a, int level) => new(
        MaxHp: Round(a.Str * 0.90 + a.Con * 3.10 + 75),
        MaxVp: Round(a.Con * 1.4436 + a.Int * 2.5207 + 75),
        MaxEvp: MaxEvp(level),
        Atk: Round(a.Str * 4.3706 + a.Agi * 1.8705 + 60),
        Def: Round(a.Con * 3.0693 + a.Int * 0.6298 + 43),
        Br: Round(a.Agi * 1.1758 + a.Int * 3.5702 + 85),
        MaxActionGauge: MaxActionGauge);

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
