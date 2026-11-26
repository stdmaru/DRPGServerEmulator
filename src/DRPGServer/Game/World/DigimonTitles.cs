using DRPGServer.Game.Entities;
using DRPGServer.Network.Packets.Map;

namespace DRPGServer.Game.World;

/// <summary>
/// Desbloqueio automático de títulos de Digimon pelas regras de <c>Resources/Digimon/DigimonTitles.xml</c>.
/// Chamado no fim de batalhas (<see cref="Battle.BattleSystem.Finish"/>) e em subidas de nível (<see cref="Progression.AfterLevelUp"/>).
/// </summary>
public static class DigimonTitles
{
    /// <summary>
    /// Desbloqueia os títulos cujas condições (<see cref="DigimonTitleInfo.Unlocks"/>) o Digimon cumpre e devolve os
    /// recém-ganhos, em ordem de id.
    /// </summary>
    public static IReadOnlyList<uint> CheckUnlocks(Digimon digimon)
    {
        if (digimon.IsWild) return [];

        var unlocked = new List<uint>();
        foreach (var title in digimon.AutoUnlockTitles)
        {
            if (digimon.HasTitle(title.Id)) continue;
            if (!title.Unlocks.All(rule => rule.IsMet(digimon))) continue;
            if (digimon.UnlockTitle(title.Id))
                unlocked.Add(title.Id);
        }
        return unlocked;
    }

    /// <summary>
    /// Verifica os marcos do Digimon e envia <c>S2C 0x4F</c> (<c>You gained the Title of [Nome].</c>) para cada
    /// título recém-desbloqueado (✅ 18 capturas v322: conn=92/100/137/145/205/408/480).
    /// </summary>
    public static IReadOnlyList<uint> CheckAndNotify(Player owner, Digimon digimon)
    {
        var unlocked = CheckUnlocks(digimon);
        foreach (uint titleId in unlocked)
            NotifyUnlocked(owner, digimon, titleId);
        return unlocked;
    }

    /// <summary>Envia o aviso oficial cinza <c>S2C 0x4F</c> de título desbloqueado (nome lido de <c>digimon_title_us.dat</c>).</summary>
    public static void NotifyUnlocked(Player owner, Digimon digimon, uint titleId)
    {
        string name = digimon.FindTitleName(titleId) ?? titleId.ToString();
        owner.Send(new SystemMessagePacket($"You gained the Title of [{name}]."));
    }
}
