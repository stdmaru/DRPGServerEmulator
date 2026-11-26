namespace DRPG.Server.Game.Stats;

/// <summary>
/// EXP para passar de nível. ✅ Fórmulas tiradas das capturas (docs/mecanicas/formulas-exp.md):
/// substituem as tabelas do protótipo, que erravam do nível 14 em diante.
/// </summary>
public static class ExpFormulas
{
    /// <summary>A partir daqui o resultado não cabe em int32; o oficial manda o valor do nível 77 (✅ digimon L160).</summary>
    public const int DigimonCapLevel = 77;

    /// <summary>
    /// Digimon: ⌊20·L · 1,2^L⌋ calculado em float32. ✅ níveis 1–21 e 160 (= nível 77), v242 e v322.
    /// Em float64 o resultado já diverge a partir do nível 29.
    /// </summary>
    public static long DigimonExpToNext(int level)
    {
        int l = Math.Clamp(level, 1, DigimonCapLevel);
        float value = (float)(20 * l) * MathF.Pow(1.2f, l);
        return (long)MathF.Floor(value);
    }

    /// <summary>Tamer: 5·(L + 1). ✅ níveis 1, 5, 14, 15 e 201 (0x02, v242 e v322).</summary>
    public static long TamerExpToNext(int level) => 5L * (Math.Max(level, 1) + 1);

    /// <summary>
    /// Penalidade de EXP por diferença de nível (<c>enemyLevel - digimonLevel</c>).
    /// ✅ 132 amostras v322 (<c>captures.db</c>): ≥ −3 dá 100%, −6..−4 dá 50%, −9..−7 dá 20% (❓), ≤ −10 dá 0%.
    /// </summary>
    public static long ApplyLevelDifference(long baseExp, int enemyLevel, int digimonLevel)
    {
        if (baseExp <= 0) return 0;
        int diff = enemyLevel - digimonLevel;
        return diff switch
        {
            >= -3 => baseExp,
            >= -6 => baseExp / 2,
            >= -9 => baseExp / 5,
            _ => 0,
        };
    }

    /// <summary>
    /// Bônus de EXP por quantidade de inimigos na batalha (<c>enemyCount</c>).
    /// ✅ 7 de 7 amostras Veteran v322 (L11–L15, L19, L20) com 0 erro e todos os Beginner (1–3 inimigos):
    /// até 3 inimigos dá 100 %, 4 inimigos dá 105 % (+5 %) e 5 inimigos dá 110 % (+10 %).
    /// Com Veteran tendo base 2× Beginner, isso produz exatamente 2,0× (n=3), 2,1× (n=4) e 2,2× (n=5).
    /// </summary>
    public static long ApplyEnemyCountBonus(long exp, int enemyCount)
    {
        if (exp <= 0) return 0;
        return enemyCount switch
        {
            <= 3 => exp,
            4 => exp * 105 / 100,
            _ => exp * 110 / 100,
        };
    }
}
