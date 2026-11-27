using DRPG.Server.Game.Battle;
using DRPG.Server.Game.Stats;

namespace DRPG.Server.Tests;

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

    [Theory]
    [InlineData(28, 10, 12, 28)]  // diff -2 -> 100%
    [InlineData(25, 9,  12, 25)]  // diff -3 -> 100%
    [InlineData(22, 8,  12, 11)]  // diff -4 -> 50%
    [InlineData(20, 7,  12, 10)]  // diff -5 -> 50%
    [InlineData(12, 3,  9,  6)]   // diff -6 -> 50%
    [InlineData(14, 4,  12, 2)]   // diff -8 -> 20%
    [InlineData(12, 3,  12, 2)]   // diff -9 -> 20%
    [InlineData(11, 2,  12, 0)]   // diff -10 -> 0%
    [InlineData(159, 26, 16, 159)] // diff +10 -> 100%
    [InlineData(0,  2,  2,  0)]   // override 0 (tutorial) continua 0
    public void Exp_level_difference_penalty_matches_captures(long baseExp, int enemyLevel, int digimonLevel, long expected) =>
        Assert.Equal(expected, ExpFormulas.ApplyLevelDifference(baseExp, enemyLevel, digimonLevel));

    // ✅ 7 de 7 amostras Veteran v322 (base = 2× Beginner) + Beginner (1–3 inimigos = 100%)
    [Theory]
    [InlineData(50,  1, 50)]   // Beginner L15, 1 inimigo -> 100%
    [InlineData(50,  3, 50)]   // Beginner L15, 3 inimigos -> 100%
    [InlineData(64,  4, 67)]   // Veteran L11 (2×32=64), 4 inimigos -> 105% = 67
    [InlineData(72,  4, 75)]   // Veteran L12 (2×36=72), 4 inimigos -> 105% = 75
    [InlineData(80,  4, 84)]   // Veteran L13 (2×40=80), 4 inimigos -> 105% = 84
    [InlineData(90,  3, 90)]   // Veteran L14 (2×45=90), 3 inimigos -> 100% = 90
    [InlineData(100, 3, 100)]  // Veteran L15 (2×50=100), 3 inimigos -> 100% = 100
    [InlineData(154, 3, 154)]  // Veteran L19 (2×77=154), 3 inimigos -> 100% = 154
    [InlineData(172, 5, 189)]  // Veteran L20 (2×86=172), 5 inimigos -> 110% = 189
    public void Exp_enemy_count_bonus_matches_captures(long baseExp, int enemyCount, long expected) =>
        Assert.Equal(expected, ExpFormulas.ApplyEnemyCountBonus(baseExp, enemyCount));
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

    [Fact]
    public void Cumulative_base_attributes_match_official_champion_and_ultimate_captures()
    {
        // ✅ sessão 56, uid 0x32610: V-mon (635, 3/2/2/1) → XV-mon (735, 3/2/1/2) no L21
        var vmon = new Data.GameData.DigimonSpecies(635, "V-mon(Tamed)", StatFormulas.Rookie, 1, new Attributes(3, 2, 2, 1), 144, 145);
        var xvmon = new Data.GameData.DigimonSpecies(735, "XV-mon(Tamed)", StatFormulas.Champion, 1, new Attributes(3, 2, 1, 2), 146, 147);

        var xvmonBase = StatFormulas.BaseAttributes([vmon, xvmon], level: 21);
        Assert.Equal(new Attributes(110, 90, 89, 71), xvmonBase);

        // + 34 pontos distribuídos no V-mon (+4 STR, +8 AGI, +2 CON, +20 INT) = (114, 98, 91, 91)
        var xvmonTotal = xvmonBase + new Attributes(4, 8, 2, 20);
        Assert.Equal(new Attributes(114, 98, 91, 91), xvmonTotal);
        var xvmonCombat = StatFormulas.Combat(xvmonTotal, level: 21);
        Assert.Equal((481, 264, 399, 362, 349), (xvmonCombat.Atk, xvmonCombat.Def, xvmonCombat.Br, xvmonCombat.MaxHp, xvmonCombat.MaxVp));

        // ✅ sessão 56, uid 0x38a85: Bearmon (674) → Gryzmon (781) → Grappu Leomon (886) no L160 (sem pontos)
        var bearmon = new Data.GameData.DigimonSpecies(674, "Bearmon", StatFormulas.Rookie, 1, new Attributes(3, 2, 2, 1), 393, 394);
        var gryzmon = new Data.GameData.DigimonSpecies(781, "Gryzmon", StatFormulas.Champion, 1, new Attributes(3, 2, 2, 1), 395, 396);
        var grappu = new Data.GameData.DigimonSpecies(886, "Grappu Leomon", StatFormulas.Ultimate, 1, new Attributes(3, 2, 2, 1), 397, 398);

        Assert.Equal(new Attributes(87, 68, 68, 49), StatFormulas.BaseAttributes([bearmon], level: 160));
        Assert.Equal(new Attributes(137, 108, 108, 79), StatFormulas.BaseAttributes([bearmon, gryzmon], level: 160));
        Assert.Equal(new Attributes(187, 148, 148, 109), StatFormulas.BaseAttributes([bearmon, gryzmon, grappu], level: 160));
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

    // ✅ capturas v322: status dos Veteran (grau 3) de L11 a L50 — 0 erro em todas as 5 amostras
    [Theory]
    [InlineData(11, 823,  2100, 237, 175, 533)]   // Piyomon L11 Veteran
    [InlineData(13, 859,  2450, 251, 185, 559)]   // Kunemon L13 Veteran
    [InlineData(15, 895,  2800, 265, 195, 585)]   // Palmon L15 Veteran
    [InlineData(17, 931,  3150, 279, 205, 611)]   // Plotmon L17 Veteran
    [InlineData(50, 1525, 8925, 510, 370, 1040)]  // Otamamon L50 Veteran
    public void Veteran_wild_stats_match_official_samples(int level, int hp, int vp, int atk, int def, int br)
    {
        var s = WildFormulas.Combat(level, WildFormulas.GradeVeteran);
        Assert.Equal((hp, vp, atk, def, br), (s.MaxHp, s.MaxVp, s.Atk, s.Def, s.Br));
    }

    [Fact]
    public void Veteran_spawn_writes_grade_3_in_0x11_and_status_block()
    {
        var spawn = new Data.GameData.SpawnArea(
            79, 50, 50, 50, 50, 1, TimeSpan.FromSeconds(10),
            [new Data.GameData.SpawnOption(10, 11, 67, 25, 100, 0)],
            [(50, 50)],
            Exclusive: true,
            Grade: WildFormulas.GradeVeteran);
        var wild = new Game.Entities.WildDigimon(TestSupport.Data, spawn, 50, 50);

        Assert.Equal(WildFormulas.Veteran(11), wild.Leader.Stats);
        Assert.Equal(WildFormulas.GradeVeteran, wild.Leader.WildGrade);

        var fieldPayload = Network.Packets.Map.WildDigimonUpdate.Packet(wild).EncodePayload();
        Assert.Equal(WildFormulas.GradeVeteran, fieldPayload[44]);

        var statusPayload = new Network.Packets.Map.BattleDigimonStatusPacket(wild.Leader).EncodePayload();
        Assert.Equal(WildFormulas.GradeVeteran, statusPayload[300]);
    }

    // ❓ [PRECISA DE REVISÃO] Fórmula provisória de acerto/esquiva ajustada sobre 661 golpes limpos v322
    [Theory]
    [InlineData(100, 100, 0.79)]  // BR igual -> 0.66 + 0.13 * 1.0 = 0.79
    [InlineData(200, 100, 0.92)]  // BR 2x -> 0.66 + 0.13 * 2.0 = 0.92
    [InlineData(300, 100, 1.00)]  // BR >= 2.62x -> teto 1.00 (120/120 acertos nas capturas)
    [InlineData(50,  100, 0.725)] // BR 0.5x -> 0.66 + 0.13 * 0.5 = 0.725
    public void Hit_chance_scales_with_br_ratio(int attackerBr, int targetBr, double expected)
    {
        double hitChance = BattleFormulas.CalculateHitChance(attackerBr, targetBr);
        Assert.Equal(expected, hitChance, precision: 3);
    }

    [Fact]
    public void Hit_chance_respects_br_card_multipliers_and_guaranteed_hit()
    {
        // Card de ataque +100% BR dobra BR_a (100*2 / 100 = 2.0 -> 0.92)
        Assert.Equal(0.92, BattleFormulas.CalculateHitChance(100, 100, attackerBrMultiplier: 2.0), precision: 3);
        // Card de defesa +100% BR dobra BR_d (100 / 100*2 = 0.5 -> 0.725)
        Assert.Equal(0.725, BattleFormulas.CalculateHitChance(100, 100, targetBrMultiplier: 2.0), precision: 3);
        // Acerto garantido (ex.: Golden Drill X) ignora desvantagem de BR
        Assert.Equal(1.0, BattleFormulas.CalculateHitChance(1, 10_000, guaranteedHit: true), precision: 3);
        // Valores inválidos/zerados de BR não causam divisão por zero
        double safe = BattleFormulas.CalculateHitChance(0, 0);
        Assert.InRange(safe, BattleFormulas.MinHitChance, BattleFormulas.MaxHitChance);
    }
}
