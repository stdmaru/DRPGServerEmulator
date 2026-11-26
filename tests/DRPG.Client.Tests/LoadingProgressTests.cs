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

        Assert.Equal(100, progress.TargetPercent);
    }

    [Theory]
    [InlineData(0f, 11, 1)]
    [InlineData(50f, 11, 6)]
    [InlineData(100f, 11, 10)]
    [InlineData(0f, 1, 0)]
    [InlineData(0f, 0, 0)]
    public void The_bar_frame_follows_the_formula_and_stays_inside_the_sprite(float displayed, int frames, int expected)
    {
        var progress = Displayed(displayed);

        Assert.Equal(expected, progress.BarFrame(frames));
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
