using DRPGServer.Game.Entities;
using DRPGServer.Game.Stats;
using DRPGServer.Network;

namespace DRPGServer.Tests;

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
}
