using DRPGServer.Game.Battle;
using DRPGServer.Game.Stats;

namespace DRPGServer.Tests;

public class ExpFormulaTests
{
    // ✅ capturas v242 e v322: EXP para o próximo nível, níveis 1–21 (offset 256 do bloco de status)
    private static readonly long[] Captured =
        [24, 57, 103, 165, 248, 358, 501, 687, 928, 1238, 1634, 2139, 2781, 3594, 4622, 5916, 7543, 9584, 12140, 15335, 19322];

    [Fact]
    public void Digimon_exp_matches_captures()
    {
        for (int level = 1; level <= Captured.Length; level++)
            Assert.Equal(Captured[level - 1], ExpFormulas.DigimonExpToNext(level));
        // ✅ digimon nível 160 da sessão 56: o valor do nível 77
        Assert.Equal(1_925_209_472, ExpFormulas.DigimonExpToNext(77));
        Assert.Equal(1_925_209_472, ExpFormulas.DigimonExpToNext(160));
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(5, 30)]
    [InlineData(14, 75)]
    [InlineData(15, 80)]
    [InlineData(201, 1010)]
    public void Tamer_exp_matches_captures(int level, long expected) =>
        Assert.Equal(expected, ExpFormulas.TamerExpToNext(level));
}

public class CombatFormulaTests
{
    // amostras do oficial (v322): STR AGI CON INT → ATK DEF BR HP VP
    [Theory]
    [InlineData(10, 10, 10, 10, 145, 87, 131, 115, 115)]     // nível 1, qualquer espécie
    [InlineData(13, 12, 12, 11, 163, 94, 139, 128, 123)]     // Gigimon L2 (DEF 94 exato)
    [InlineData(25, 16, 16, 13, 218, 108, 153, 157, 136)]    // Hopmon L4
    [InlineData(44, 32, 31, 21, 293, 147, 201, 216, 179)]    // Hopmon L10
    [InlineData(70, 60, 50, 50, 376, 195, 300, 273, 259)]    // rookie L11
    [InlineData(91, 76, 70, 69, 428, 231, 349, 319, 305)]    // V-mon L20
    [InlineData(114, 98, 91, 91, 481, 264, 399, 362, 349)]   // champion L21
    [InlineData(927, 768, 528, 489, 1366, 630, 975, 928, 819)] // Tactimon L160 (mega oficial, 0 erros)
    public void Combat_stats_match_official_samples(int str, int agi, int con, int @int, int atk, int def, int br, int hp, int vp)
    {
        var s = StatFormulas.Combat(new Attributes(str, agi, con, @int), 1);
        Assert.Equal((atk, def, br, hp, vp), (s.Atk, s.Def, s.Br, s.MaxHp, s.MaxVp));
    }

    [Theory]
    [InlineData(1, 11, 3, 11, 1.05)]  // Vacina > Vírus
    [InlineData(3, 11, 2, 11, 1.05)]  // Vírus > Dados
    [InlineData(2, 11, 1, 11, 1.05)]  // Dados > Vacina
    [InlineData(1, 11, 2, 11, 0.90)]  // Vacina < Dados
    [InlineData(0, 1,  0, 2,  1.05)]  // Elemental: Fogo > Natureza (Gigimon vs Yuramon)
    [InlineData(0, 11, 0, 2,  1.00)]  // Elemental: Neutro (Hopmon vs Yuramon)
    public void Affinity_multipliers_match_expected(byte aAttr, byte aElem, byte tAttr, byte tElem, double expected)
    {
        double mult = BattleFormulas.AffinityMultiplier(aAttr, aElem, tAttr, tElem);
        Assert.Equal(expected, mult, precision: 2);
    }
}
