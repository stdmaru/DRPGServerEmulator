using DRPG.Engine.Rendering;

namespace DRPG.Engine.Tests;

public sealed class FrameLayoutTests
{
    [Theory]
    [InlineData(800, 600, 1, 0, 0)]
    [InlineData(1150, 785, 1, 175, 92)]     // tiled window: 1×, centered, no fractional scale
    [InlineData(1600, 1200, 2, 0, 0)]       // HiDPI 2×
    [InlineData(1920, 1080, 1, 560, 240)]   // 1080p fullscreen: 2× does not fit vertically
    [InlineData(2560, 1440, 2, 480, 120)]
    [InlineData(450, 110, 1, -175, -245)]   // smaller than the frame: cropped, not shrunk
    public void Centers_at_whole_number_scale(int w, int h, int scale, int x, int y) =>
        Assert.Equal(new FrameLayout(scale, x, y), FrameLayout.For(w, h));

    [Fact]
    public void Maps_output_pixels_back_to_frame_coordinates()
    {
        var layout = FrameLayout.For(2560, 1440);
        Assert.Equal((0, 0), layout.ToFrame(480, 120));
        Assert.Equal((399, 299), layout.ToFrame(480 + 799, 120 + 599));
        Assert.Equal((-1, -1), layout.ToFrame(479, 119));
    }
}
