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

    /// <summary>Estágios (coluna 2 do <c>digimon_us</c>, byte 160 do bloco de status).</summary>
    public const byte InTraining = 1, Rookie = 2, Champion = 3, Ultimate = 4, Mega = 5;

    /// <summary>✅ Nível em que o digimon chega a cada estágio: rookie no 11, champion 21, ultimate 31, mega 41.</summary>
    public static int StageLevel(byte stage) => stage switch
    {
        <= InTraining => 1,
        Rookie => 11,
        _ => 21 + 10 * (stage - Champion),
    };

    /// <summary>
    /// Último nível em que a forma ainda cresce: ✅ in-training 10, rookie 20 (Kotemon L160 = base de L20, sem pontos;
    /// V-mon igual no 20 e no 21), ❓ champion 30, ultimate 40; mega até o máximo.
    /// </summary>
    public static int GrowthCap(byte stage) => stage >= Mega ? int.MaxValue : Math.Max(InTraining, (int)stage) * 10;

    /// <summary>
    /// Atributos base da forma: 10 + 20 por estágio acima de in-training + status-base × (nível limitado − 1).
    /// ✅ in-training e rookie (Monodramon L11 60/50/50/50, Kotemon L160 87/68/68/49). ❓ champion e ultimate batem
    /// em CON e INT (Musyamon, Vajramon L160) mas não em STR/AGI; mega não segue a fórmula. Espécie sem dex não cresce.
    /// </summary>
    public static Attributes BaseAttributes(DigimonSpecies species, int level)
    {
        int stage = Math.Max(InTraining, (int)species.Stage);
        int levels = Math.Max(0, Math.Min(level, GrowthCap((byte)stage)) - 1);
        var g = species.Growth ?? default;
        int b = 10 + 20 * (stage - 1);
        return new(b + g.Str * levels, b + g.Agi * levels, b + g.Con * levels, b + g.Int * levels);
    }

    /// <summary>
    /// Pontos de atributo que a forma ganhou até o nível: 2 por nível dentro da janela do estágio. ✅ rookie 2·(L − 1)
    /// até o 21 (40 no L160), champion 0 no L21 e 20 no L160, mega 2·(L − 41) (238 no L160).
    /// </summary>
    public static int AbilityPointsEarned(byte stage, int level)
    {
        int start = stage <= Rookie ? 1 : StageLevel(stage);
        int end = stage >= Mega ? int.MaxValue : stage <= InTraining ? 11 : StageLevel((byte)(stage + 1));
        return 2 * Math.Max(0, Math.Min(level, end) - start);
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
