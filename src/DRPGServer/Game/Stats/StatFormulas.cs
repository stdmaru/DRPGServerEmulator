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

    // ✅ status = ⌊c1·√a + c2·√b⌋, sem nível nem espécie (ajuste em 80 amostras v322, todos os estágios).
    // Erra 1 ponto em: ATK 1, HP 0, DEF 0, BR 8, VP 2 amostras. Coeficientes = centro da região viável.
    public const double AtkStr = 34.521, AtkAgi = 11.364;
    public const double HpStr = 11.536, HpCon = 25.054;
    public const double DefCon = 21.115, DefInt = 6.603;
    public const double BrAgi = 9.900, BrInt = 31.642;
    public const double VpCon = 11.213, VpInt = 25.446;

    /// <summary>
    /// ✅ Cada status é uma soma de raízes de dois atributos (os que o jogo diz: STR → ATK, HP;
    /// AGI → BR, ATK; CON → DEF, HP, VP; INT → BR, VP, DEF). ❓ Bônus de equipamento/buff não entram.
    /// </summary>
    public static CombatStats Combat(Attributes a, int level) => new(
        MaxHp: Stat(HpStr, a.Str, HpCon, a.Con),
        MaxVp: Stat(VpCon, a.Con, VpInt, a.Int),
        MaxEvp: MaxEvp(level),
        Atk: Stat(AtkStr, a.Str, AtkAgi, a.Agi),
        Def: Stat(DefCon, a.Con, DefInt, a.Int),
        Br: Stat(BrAgi, a.Agi, BrInt, a.Int),
        MaxActionGauge: MaxActionGauge);

    private static int Stat(double c1, int a, double c2, int b) =>
        (int)Math.Floor(c1 * Math.Sqrt(Math.Max(0, a)) + c2 * Math.Sqrt(Math.Max(0, b)));
}
