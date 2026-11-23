namespace DRPGServer.Game.Stats;

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
}
