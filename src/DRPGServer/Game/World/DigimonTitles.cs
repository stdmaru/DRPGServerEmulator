using DRPGServer.Data.GameData;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Stats;
using DRPGServer.Network.Packets.Map;

namespace DRPGServer.Game.World;

/// <summary>
/// Regras automáticas de desbloqueio de títulos de Digimon (<c>digimon_title_us.dat</c>).
/// Chamado no fim de batalhas (<see cref="Battle.BattleSystem.Finish"/>) e em subidas de nível (<see cref="Progression.AfterLevelUp"/>).
/// </summary>
public static class DigimonTitles
{
    // ✅ Confirmados em digimon_title_us.dat e capturas v322 (conn=76, conn=141)
    public const uint NewStart = 20000;              // Mega desbloqueado (Lv. 41)
    public const uint BreakThroughFixedExp = 20010;  // Lv. 100
    public const uint LordOfDigimon = 20020;         // Lv. 110
    public const uint EndlessJourney = 20030;        // Lv. 120
    public const uint AnotherStart = 20040;          // Lv. 130
    public const uint LimitBreak = 20050;            // Lv. 140

    public const uint FirstWin = 20100;              // 1 vitória
    public const uint Win10 = 20110;                 // 10 vitórias
    public const uint Win100 = 20120;                // 100 vitórias
    public const uint Win1000 = 20130;               // 1000 vitórias
    public const uint Win10000 = 20140;              // 10000 vitórias

    public const uint FirstLose = 20200;             // 1 derrota (inclui fuga)
    public const uint Lose100 = 20210;               // 100 derrotas
    public const uint Lose1000 = 20220;              // 1000 derrotas
    public const uint KingOfLose = 20230;            // 10000 derrotas

    /// <summary>Verifica todos os marcos atingidos pelo Digimon e devolve os IDs recém-desbloqueados.</summary>
    public static IReadOnlyList<uint> CheckUnlocks(Digimon digimon)
    {
        if (digimon.IsWild) return [];

        var unlocked = new List<uint>();

        void TryUnlock(bool condition, uint titleId)
        {
            if (condition && digimon.UnlockTitle(titleId))
                unlocked.Add(titleId);
        }

        // Vitórias
        TryUnlock(digimon.TotalWins >= 1, FirstWin);
        TryUnlock(digimon.TotalWins >= 10, Win10);
        TryUnlock(digimon.TotalWins >= 100, Win100);
        TryUnlock(digimon.TotalWins >= 1000, Win1000);
        TryUnlock(digimon.TotalWins >= 10000, Win10000);

        // Derrotas (batalhas totais menos vitórias; fuga conta como derrota)
        int losses = Math.Max(0, digimon.TotalBattles - digimon.TotalWins);
        TryUnlock(losses >= 1, FirstLose);
        TryUnlock(losses >= 100, Lose100);
        TryUnlock(losses >= 1000, Lose1000);
        TryUnlock(losses >= 10000, KingOfLose);

        // Evolução / Nível
        TryUnlock(digimon.UnlockedStage >= StatFormulas.Mega, NewStart);
        TryUnlock(digimon.Level >= 100, BreakThroughFixedExp);
        TryUnlock(digimon.Level >= 110, LordOfDigimon);
        TryUnlock(digimon.Level >= 120, EndlessJourney);
        TryUnlock(digimon.Level >= 130, AnotherStart);
        TryUnlock(digimon.Level >= 140, LimitBreak);

        return unlocked;
    }

    /// <summary>
    /// Verifica os marcos do Digimon e envia <c>S2C 0x4F</c> (<c>You gained the Title of [Nome].</c>) para cada
    /// título recém-desbloqueado (✅ 18 capturas v322: conn=92/100/137/145/205/408/480).
    /// </summary>
    public static IReadOnlyList<uint> CheckAndNotify(Player owner, Digimon digimon, GameData? data = null)
    {
        var unlocked = CheckUnlocks(digimon);
        foreach (uint titleId in unlocked)
            NotifyUnlocked(owner, titleId, data);
        return unlocked;
    }

    /// <summary>Envia o aviso oficial cinza <c>S2C 0x4F</c> de título desbloqueado.</summary>
    public static void NotifyUnlocked(Player owner, uint titleId, GameData? data = null)
    {
        string name = data?.FindDigimonTitle(titleId)?.Name ?? DefaultNameOf(titleId);
        owner.Send(new SystemMessagePacket($"You gained the Title of [{name}]."));
    }

    private static string DefaultNameOf(uint titleId) => titleId switch
    {
        NewStart => "A New Start",
        BreakThroughFixedExp => "Break Through Fixed EXP",
        LordOfDigimon => "Lord of Digimon",
        EndlessJourney => "Endless Journey",
        AnotherStart => "Another Start",
        LimitBreak => "LIMIT BREAK",
        FirstWin => "First Win",
        Win10 => "10th Win",
        Win100 => "100th Win",
        Win1000 => "1000th Win",
        Win10000 => "10000th Win",
        FirstLose => "First Lose",
        Lose100 => "100th Lose",
        Lose1000 => "1000th Lose",
        KingOfLose => "King of Lose",
        _ => titleId.ToString(),
    };
}
