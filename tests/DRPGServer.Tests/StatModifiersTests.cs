using DRPGServer.Game.Stats;

namespace DRPGServer.Tests;

public sealed class StatModifiersTests
{
    [Fact]
    public void Of_sums_flat_and_percent_separately()
    {
        var m = StatModifiers.Of([
            new(StatModifierKind.Atk, 10), new(StatModifierKind.Atk, 5),
            new(StatModifierKind.Atk, 3, StatModifierForm.Percent),
        ]);

        Assert.Equal(15, m.Flat(StatModifierKind.Atk));
        Assert.Equal(3, m.Percent(StatModifierKind.Atk));
        Assert.Equal(0, m.Flat(StatModifierKind.Def));
    }

    [Fact]
    public void Of_rejects_a_form_the_kind_does_not_take()
    {
        Assert.Throws<ArgumentException>(() => StatModifiers.Of([new(StatModifierKind.Str, 5, StatModifierForm.Percent)]));
        Assert.Throws<ArgumentException>(() => StatModifiers.Of([new(StatModifierKind.DigimonExp, 5)]));
    }

    [Fact]
    public void Combine_follows_the_stacking_rule_of_each_kind()
    {
        var dark = StatModifiers.Of([new(StatModifierKind.CardSlashLimit, 23), new(StatModifierKind.HpRecovery, 4)]);
        var other = StatModifiers.Of([new(StatModifierKind.CardSlashLimit, 15), new(StatModifierKind.HpRecovery, 8)]);
        var aura = StatModifiers.Of([new(StatModifierKind.MaintainEvolution, 1)]);

        var total = StatModifiers.Combine(dark, other, aura, aura);

        Assert.Equal(23, total.Flat(StatModifierKind.CardSlashLimit));  // Max
        Assert.Equal(12, total.Flat(StatModifierKind.HpRecovery));      // Sum
        Assert.Equal(1, total.Flat(StatModifierKind.MaintainEvolution)); // Any
        Assert.True(total.Has(StatModifierKind.MaintainEvolution));
    }

    [Fact]
    public void Empty_sources_give_None()
    {
        Assert.Same(StatModifiers.None, StatModifiers.Of([new(StatModifierKind.Atk, 0)]));
        Assert.Same(StatModifiers.None, StatModifiers.Combine(StatModifiers.None, StatModifiers.None));
        Assert.True(StatModifiers.None.IsEmpty);
    }

    [Fact]
    public void Attributes_reads_the_four_flat_attributes()
    {
        var m = StatModifiers.Of([
            new(StatModifierKind.Str, 1), new(StatModifierKind.Agi, 2),
            new(StatModifierKind.Con, 3), new(StatModifierKind.Int, 4),
        ]);

        Assert.Equal(new Attributes(1, 2, 3, 4), m.Attributes);
    }
}
