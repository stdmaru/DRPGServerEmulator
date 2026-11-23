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

    /// <summary>
    /// ✅ Fórmula exata confirmada em todas as amostras limpas v322 (0 erros em todas as capturas):
    /// status = ⌊√(192 · (w1·attr1 + w2·attr2))⌋ = ⌊8 · √(3 · (w1·attr1 + w2·attr2))⌋
    /// </summary>
    public static CombatStats Combat(Attributes a, int level) => new(
        MaxHp: Stat(2, a.Str, 5, a.Con),
        MaxVp: Stat(2, a.Con, 5, a.Int),
        MaxEvp: MaxEvp(level),
        Atk: Stat(8, a.Str, 3, a.Agi),
        Def: Stat(3, a.Con, 1, a.Int),
        Br: Stat(2, a.Agi, 7, a.Int),
        MaxActionGauge: MaxActionGauge);

    private static int Stat(int w1, int a, int w2, int b) =>
        (int)Math.Floor(Math.Sqrt(192.0 * (w1 * Math.Max(0, a) + w2 * Math.Max(0, b))));
}
