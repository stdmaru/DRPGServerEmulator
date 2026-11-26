using DRPG.Client.Startup;

namespace DRPG.Client.Tests;

public sealed class LoadingProgressTests
{
    [Fact]
    public void The_first_tick_only_starts_the_clock()
    {
        var progress = new LoadingProgress();
        progress.Raise(100);

        progress.Tick(1000);

        Assert.Equal(0, progress.Displayed);
    }

    [Fact]
    public void A_far_target_moves_seven_times_faster_than_a_near_one()
    {
        var far = new LoadingProgress();
        far.Raise(100);                       // boost 39 < 100 − 1: the fast step
        far.Tick(0);
        far.Tick(10);

        var near = new LoadingProgress();
        near.Raise(30);                       // boost 39 ≥ 30 − 1: the slow step
        near.Tick(0);
        near.Tick(10);

        Assert.Equal(39 * 0.01f * 0.18f * 7, far.Displayed, 5);
        Assert.Equal(39 * 0.01f * 0.18f, near.Displayed, 5);
    }

    [Fact]
    public void Steps_come_at_most_every_ten_milliseconds()
    {
        var progress = new LoadingProgress();
        progress.Raise(100);
        progress.Tick(0);

        progress.Tick(9);
        Assert.Equal(0, progress.Displayed);
        progress.Tick(10);
        var afterOne = progress.Displayed;
        progress.Tick(15);
        Assert.Equal(afterOne, progress.Displayed);
    }

    [Fact]
    public void The_displayed_value_stops_once_it_passes_the_target()
    {
        var progress = new LoadingProgress();
        progress.Raise(1);
        for (ulong t = 0; t < 2000; t += 10)
            progress.Tick(t);

        // one step past the target at most, then it waits for the loader to raise it
        Assert.InRange(progress.Displayed, 1, 1 + 39 * 0.01f * 0.18f);
        var settled = progress.Displayed;
        progress.Tick(5000);
        Assert.Equal(settled, progress.Displayed);
    }

    [Fact]
    public void Raising_only_goes_up()
    {
        var progress = new LoadingProgress();
        progress.Raise(40);
        progress.Raise(20);
        progress.Raise(500);

        Assert.Equal(LoadingProgress.MaxTarget, progress.TargetPercent);
    }

    [Theory]
    [InlineData(0f, 1)]
    [InlineData(50f, 116)]
    [InlineData(100f, 231)]
    public void The_bar_width_is_one_plus_the_fraction_of_230(float displayed, int expected)
    {
        var progress = Displayed(displayed);

        // ✅ 1 + 230 × displayed / 100, truncated (docs/cliente/cenas.md#cscenetitle-carga-); the chase overshoots a hair
        Assert.InRange(progress.BarFillWidth(), expected, expected + 2);
    }

    [Fact]
    public void The_bar_is_never_wider_than_its_sprite_even_past_the_hundred()
    {
        var progress = new LoadingProgress();
        progress.Raise(StartupLoader.AfterTitle);
        progress.Tick(0);
        for (ulong t = 10; t < 20_000; t += 10)
            progress.Tick(t);

        Assert.True(progress.Displayed > 100);
        Assert.Equal(LoadingProgress.BarWidth, progress.BarFillWidth());
    }

    [Fact]
    public void The_last_target_is_one_hundred_and_one()
    {
        var progress = new LoadingProgress();
        progress.Raise(StartupLoader.AfterTitle);

        Assert.Equal(101, progress.TargetPercent);
    }

    private static LoadingProgress Displayed(float value)
    {
        // drive the chase until the displayed value is at least `value`
        var progress = new LoadingProgress();
        progress.Raise(100);
        progress.Tick(0);
        for (ulong t = 10; progress.Displayed < value; t += 10)
            progress.Tick(t);
        return progress;
    }
}
