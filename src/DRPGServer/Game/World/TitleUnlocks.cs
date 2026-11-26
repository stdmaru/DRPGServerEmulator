using DRPGServer.Game.Entities;
using DRPGServer.Game.Stats;

namespace DRPGServer.Game.World;

/// <summary>Condição de desbloqueio automático de título (<c>&lt;Unlock Condition=... Value=... /&gt;</c>).</summary>
public enum TitleUnlockCondition : byte
{
    /// <summary>Vitórias do Digimon.</summary>
    Wins,
    /// <summary>Derrotas: batalhas menos vitórias (fuga conta como derrota).</summary>
    Losses,
    /// <summary>Nível do Digimon.</summary>
    Level,
    /// <summary>Estágio já liberado na linha evolutiva (nome: <c>Champion</c>, <c>Ultimate</c>, <c>Mega</c>…).</summary>
    Stage,
}

/// <summary>Uma condição de desbloqueio. O título é ganho quando todas as condições dele valem.</summary>
public interface ITitleUnlockRule
{
    bool IsMet(Digimon digimon);
}

public sealed record WinsUnlock(int Count) : ITitleUnlockRule
{
    public bool IsMet(Digimon digimon) => digimon.TotalWins >= Count;
}

public sealed record LossesUnlock(int Count) : ITitleUnlockRule
{
    public bool IsMet(Digimon digimon) => Math.Max(0, digimon.TotalBattles - digimon.TotalWins) >= Count;
}

public sealed record LevelUnlock(int Level) : ITitleUnlockRule
{
    public bool IsMet(Digimon digimon) => digimon.Level >= Level;
}

public sealed record StageUnlock(byte Stage) : ITitleUnlockRule
{
    public bool IsMet(Digimon digimon) => digimon.UnlockedStage >= Stage;
}

public static class TitleUnlockRules
{
    /// <summary>Cria a regra de uma condição a partir do valor do XML. Null se o valor não serve para a condição.</summary>
    public static ITitleUnlockRule? Create(TitleUnlockCondition condition, string value) => condition switch
    {
        TitleUnlockCondition.Wins => PositiveInt(value) is { } wins ? new WinsUnlock(wins) : null,
        TitleUnlockCondition.Losses => PositiveInt(value) is { } losses ? new LossesUnlock(losses) : null,
        TitleUnlockCondition.Level => PositiveInt(value) is { } level ? new LevelUnlock(level) : null,
        TitleUnlockCondition.Stage => StageByName(value) is { } stage ? new StageUnlock(stage) : null,
        _ => null,
    };

    private static int? PositiveInt(string value) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) && n > 0
            ? n : null;

    private static byte? StageByName(string value) => value switch
    {
        "InTraining" => StatFormulas.InTraining,
        "Rookie" => StatFormulas.Rookie,
        "Champion" => StatFormulas.Champion,
        "Ultimate" => StatFormulas.Ultimate,
        "Mega" => StatFormulas.Mega,
        _ => null,
    };
}
