using DRPG.Client.Ui;
using DRPG.Client.World;

namespace DRPG.Client.Tests;

/// <summary>The timing of the world entry's loading screen (✅ the loading thread <c>0x00b34ea0</c>), without a scene.</summary>
public sealed class WorldLoadingTests
{
    private sealed class FixedRandom(int value) : Random
    {
        public override int Next(int maxValue) => value % maxValue;
    }

    /// <summary>Runs loop iterations every <see cref="WorldLoading.FrameMs"/> until <paramref name="action"/> comes; returns its time.</summary>
    private static ulong Until(WorldLoading loading, LoadingAction action, ref ulong t)
    {
        for (int guard = 0; guard < 1000; guard++, t += WorldLoading.FrameMs)
            if (loading.Update(t) == action)
                return t;
        throw new Xunit.Sdk.XunitException($"{action} never came");
    }

    [Fact]
    public void The_two_runs_join_wait_for_the_data_and_end_after_the_map()
    {
        var loading = new WorldLoading(new FixedRandom(5), backgroundCount: 11);
        Assert.Equal(5, loading.Background);
        ulong t = 0;

        Assert.Equal(2430UL, Until(loading, LoadingAction.Join, ref t));
        Assert.Equal(27, loading.FramesDrawn);
        Assert.Equal(LoadingPhase.WaitingForData, loading.Phase);

        int gauge = loading.GaugeFrame;
        Assert.Equal(LoadingAction.None, loading.Update(t + 5000));          // waiting: nothing is drawn, the last frame stays
        Assert.Equal(gauge, loading.GaugeFrame);
        Assert.Equal(27, loading.FramesDrawn);

        t += 5000;
        loading.DataArrived();
        loading.MapLoaded();
        Until(loading, LoadingAction.EnterWorld, ref t);
        Assert.Equal(27 + WorldLoading.MinimumFrames, loading.FramesDrawn);
        Assert.False(loading.Showing);
        Assert.Equal(LoadingAction.None, loading.Update(t + 1000));
    }

    [Fact]
    public void The_second_run_waits_for_the_map_even_after_its_16_frames()
    {
        var loading = new WorldLoading(new FixedRandom(0), backgroundCount: 1);
        ulong t = 0;
        Until(loading, LoadingAction.Join, ref t);
        loading.DataArrived();

        for (int i = 0; i < 40; i++, t += WorldLoading.FrameMs * 2)
            Assert.Equal(LoadingAction.None, loading.Update(t));
        Assert.True(loading.Showing);

        loading.MapLoaded();
        Assert.Equal(LoadingAction.EnterWorld, loading.Update(t));
    }

    [Fact]
    public void The_gauge_turns_through_12_frames_and_goes_on_across_the_two_runs()
    {
        var loading = new WorldLoading(new FixedRandom(0), backgroundCount: 1);
        var seen = new List<int>();
        ulong t = 0;
        for (int i = 0; i < 14; i++, t += WorldLoading.FrameMs)
        {
            loading.Update(t);
            seen.Add(loading.GaugeFrame);
        }
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 0, 1], seen);
    }

    [Fact]
    public void Without_background_images_none_is_picked()
    {
        var loading = new WorldLoading(new FixedRandom(3), backgroundCount: 0);
        Assert.Equal(-1, loading.Background);
    }

    [Fact]
    public void The_steps_out_of_order_are_refused()
    {
        var loading = new WorldLoading(new FixedRandom(0), backgroundCount: 1);
        Assert.Throws<InvalidOperationException>(loading.DataArrived);          // before the 0x01
        Assert.Throws<InvalidOperationException>(loading.MapLoaded);
        ulong t = 0;
        Until(loading, LoadingAction.Join, ref t);
        Assert.Throws<InvalidOperationException>(loading.MapLoaded);            // the data is not in yet
        loading.DataArrived();
        Assert.Throws<InvalidOperationException>(loading.DataArrived);          // twice
    }
}
