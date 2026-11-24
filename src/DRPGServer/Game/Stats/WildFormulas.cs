namespace DRPGServer.Game.Stats;

/// <summary>
/// Status dos digimons selvagens. ✅ Só dependem do nível e do grau (byte 44 do 0x11 / byte 300 do 0x63),
/// não da espécie nem dos atributos (o 0x63 manda os atributos zerados). Ver docs/mecanicas/selvagens.md.
/// - Beginner (grau 2): ✅ 21 combinações espécie × nível em 125 batalhas v322 (L1–L26), todas exatas.
/// - Veteran (grau 3): ✅ 5 níveis confirmados (L11, L13, L15, L17, L50), todas exatas com 0 erro.
/// - ❓ Graus superiores (Elite/Legend, grau ≥ 4): ainda sem capturas.
/// </summary>
public static class WildFormulas
{
    public const byte GradeBeginner = 2;
    public const byte GradeVeteran = 3;

    public static CombatStats Combat(int level, byte grade = GradeBeginner) => grade switch
    {
        GradeVeteran => Veteran(level),
        _ => Beginner(level),
    };

    public static CombatStats Beginner(int level)
    {
        int l = Math.Max(1, level);
        return new CombatStats(
            MaxHp: (13 * l + 220) / 2,          // ⌈116 + 6,5·(L − 1)⌉
            MaxVp: 25 * l + 25,
            MaxEvp: StatFormulas.MaxEvp(l),
            Atk: 6 * l + 74,
            Def: 3 * l + 60,
            Br: 5 * l + 80,
            MaxActionGauge: StatFormulas.MaxActionGauge);
    }

    public static CombatStats Veteran(int level)
    {
        int l = Math.Max(1, level);
        return new CombatStats(
            MaxHp: 18 * l + 625,
            MaxVp: 175 * l + 175,               // ✅ exatamente 7× o VP do Beginner
            MaxEvp: StatFormulas.MaxEvp(l),
            Atk: 7 * l + 160,
            Def: 5 * l + 120,
            Br: 13 * l + 390,
            MaxActionGauge: StatFormulas.MaxActionGauge);
    }
}
