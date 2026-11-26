using DRPG.Launcher.Assets;

namespace DRPG.Launcher.Tests;

public sealed class DisabledLookTests
{
    [Fact]
    public void Disabled_pixels_are_grey_darker_and_keep_alpha()
    {
        uint[] pixels = [0xFF0000FF, 0x80FFFFFF, 0x00000000, 0xFF00FF00];

        SpriteStore.Disable(pixels);

        foreach (var p in pixels)
        {
            uint r = p & 0xFF, g = (p >> 8) & 0xFF, b = (p >> 16) & 0xFF;
            Assert.Equal(r, g);
            Assert.Equal(g, b);
        }
        Assert.Equal(0xFF000000u, pixels[0] & 0xFF000000);
        Assert.Equal(0x80000000u, pixels[1] & 0xFF000000);
        Assert.Equal(0u, pixels[2]);                    // transparent stays transparent
        Assert.Equal(191u, pixels[1] & 0xFF);           // white -> 255 * 3/4
        Assert.True((pixels[3] & 0xFF) > (pixels[0] & 0xFF)); // green is brighter than red, as in Rec. 601
    }
}
