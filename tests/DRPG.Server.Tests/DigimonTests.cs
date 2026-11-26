using DRPG.Server.Game.Entities;
using DRPG.Server.Game.Stats;
using DRPG.Server.Network;

namespace DRPG.Server.Tests;

public class DigimonTests
{
    [Fact]
    public void MaxEvp_follows_confirmed_formula()
    {
        Assert.Equal(0, StatFormulas.MaxEvp(1));
        Assert.Equal(30, StatFormulas.MaxEvp(6));
        Assert.Equal(156, StatFormulas.MaxEvp(13));
        Assert.Equal(156, Entities.Digimon(level: 13).Stats.MaxEvp);
    }

    [Fact]
    public void Allocating_needs_points_and_raises_attribute()
    {
        var d = Entities.Digimon();
        Assert.False(d.AllocatePoint(StatKind.Str));
        d.LevelUp();
        var before = d.Attributes.Str;
        Assert.True(d.AllocatePoint(StatKind.Str));
        Assert.True(d.AllocatePoint(StatKind.Str));
        Assert.False(d.AllocatePoint(StatKind.Str));
        Assert.Equal(before + 2, d.Attributes.Str);
    }

    [Fact]
    public void Exp_carries_over_several_levels()
    {
        var d = Entities.Digimon();
        long needed = TestSupport.Data.DigimonExpToNext(1) + TestSupport.Data.DigimonExpToNext(2);
        Assert.Equal(2, d.GainExp(needed + 3, maxLevel: 100, allowMultiLevelUp: true));
        Assert.Equal(3, d.Level);
        Assert.Equal(3, d.Exp);
        Assert.Equal(d.Stats.MaxHp, d.Hp);
    }

    [Fact]
    public void Single_level_up_discards_overflow_exp_when_multi_level_disabled()
    {
        var d = Entities.Digimon();
        long huge = TestSupport.Data.DigimonExpToNext(1) + TestSupport.Data.DigimonExpToNext(2) + 500;
        Assert.Equal(1, d.GainExp(huge, maxLevel: 100, allowMultiLevelUp: false));
        Assert.Equal(2, d.Level);
        Assert.Equal(0, d.Exp);
        Assert.Equal(d.Stats.MaxHp, d.Hp);
    }

    [Fact]
    public void Level_cap_is_respected()
    {
        var d = Entities.Digimon();
        d.GainExp(long.MaxValue / 2, maxLevel: 5);
        Assert.Equal(5, d.Level);
    }

    [Fact]
    public void Champion_inherits_rookie_allocated_points_and_cumulative_base()
    {
        // Linha da fixture: 50 Rookiemon (Rookie) → 80 Champmon (Champion)
        var d = new Digimon(TestSupport.Data, TestSupport.Data.Species[50], level: 21);
        var champSpecies = TestSupport.Data.Species[80];
        var rookieBase = d.Attributes;

        // Gasta todos os 40 pontos do Rookie em STR
        Assert.Equal((ushort)40, d.AbilityPoints);
        for (int i = 0; i < 40; i++)
            Assert.True(d.AllocatePoint(StatKind.Str));
        Assert.Equal((ushort)0, d.AbilityPoints);
        Assert.Equal(rookieBase.Str + 40, d.Attributes.Str);
        var rookieAtk = d.Stats.Atk;

        // Evolui para Champion no L21 (0 AP próprios): herda os 40 STR do Rookie + base cumulativo
        Assert.True(d.EvolveTo(champSpecies));
        Assert.Equal((ushort)0, d.AbilityPoints);
        var expectedChampBase = StatFormulas.BaseAttributes([TestSupport.Data.Species[50], champSpecies], level: 21);
        Assert.Equal(expectedChampBase + new Attributes(40, 0, 0, 0), d.Attributes);
        Assert.True(d.Stats.Atk > rookieAtk);

        // Voltar para Rookie mantém os atributos originais do Rookie e os pontos independentes por forma
        Assert.True(d.Devolve());
        Assert.Equal(rookieBase + new Attributes(40, 0, 0, 0), d.Attributes);
        Assert.Equal((ushort)0, d.PointsOf(d.BaseForm));
    }

    [Fact]
    public void Allocating_rookie_points_propagates_to_champion_view_without_consuming_champion_ap()
    {
        var d = new Digimon(TestSupport.Data, TestSupport.Data.Species[50], level: 31);
        var champForm = d.FormAtStage(StatFormulas.Champion)!;

        Assert.Equal((ushort)40, d.PointsOf(d.BaseForm));
        Assert.Equal((ushort)20, d.PointsOf(champForm));

        var champBefore = d.ViewAs(champForm, () => d.Attributes);

        // Gasta 5 pontos em STR e 3 em AGI no Rookie
        for (int i = 0; i < 5; i++) Assert.True(d.AllocatePoint(d.BaseForm, StatKind.Str));
        for (int i = 0; i < 3; i++) Assert.True(d.AllocatePoint(d.BaseForm, StatKind.Agi));

        var champAfterRookie = d.ViewAs(champForm, () => d.Attributes);
        Assert.Equal(champBefore + new Attributes(5, 3, 0, 0), champAfterRookie);
        Assert.Equal((ushort)20, d.PointsOf(champForm)); // AP próprio do Champion intacto

        // Gasta 2 pontos em CON no Champion: soma só no Champion, não altera o Rookie
        var rookieBefore = d.Attributes;
        Assert.True(d.AllocatePoint(champForm, StatKind.Con));
        Assert.True(d.AllocatePoint(champForm, StatKind.Con));

        Assert.Equal(rookieBefore, d.Attributes);
        Assert.Equal(champAfterRookie + new Attributes(0, 0, 2, 0), d.ViewAs(champForm, () => d.Attributes));
        Assert.Equal((ushort)18, d.PointsOf(champForm));
    }
}
