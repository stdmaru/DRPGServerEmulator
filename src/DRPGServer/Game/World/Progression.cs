using DRPGServer.Game.Entities;
using DRPGServer.Game.Stats;
using DRPGServer.Network.Packets.Map;

namespace DRPGServer.Game.World;

/// <summary>
/// O que acontece depois que o digimon sobe de nível (docs/mecanicas/evolucao.md):
/// ✅ 0x70 com o bloco da forma de antes; no nível 11 o in-training vira rookie (0x80 com o bloco novo);
/// no 21/31/41 o estágio é liberado e, em batalha, o digimon evolui na hora (0x6E → 0x70 → 0x80).
/// </summary>
public static class Progression
{
    /// <summary>Soma EXP e manda o 0x6E do digimon e, se subiu, o 0x70 e o 0x80.</summary>
    public static int GainExp(Player owner, Digimon digimon, long exp, double bits, int maxLevel, bool inBattle)
    {
        int levels = digimon.GainExp(exp, maxLevel);
        owner.Send(new RewardBitExpPacket(digimon.Serial, exp, bits));
        if (levels > 0) AfterLevelUp(owner, digimon, inBattle);
        return levels;
    }

    /// <summary>0x70 e as mudanças de forma do nível novo.</summary>
    public static void AfterLevelUp(Player owner, Digimon digimon, bool inBattle)
    {
        owner.Send(new LevelUpPacket(digimon));
        if (digimon.BecomeRookie())
        {
            owner.Send(new DigimonEvolvedPacket(digimon));
            return;
        }
        byte stage = digimon.UnlockStage();
        // ✅ só em batalha a evolução é automática; no mapa a forma nova fica liberada para o menu
        if (stage >= StatFormulas.Champion && inBattle && digimon.LineSpecies(stage) is { } next && digimon.EvolveTo(next))
            owner.Send(new DigimonEvolvedPacket(digimon));
    }
}
