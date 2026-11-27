using DRPG.Engine.Assets;

namespace DRPG.Engine.Tests;

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

public sealed class KeepRegionsTests
{
    private const uint Field = 0xFF884808, Edge = 0xFF904C0A, Rim = 0xFFB87010, Clear = 0;

    [Fact]
    public void Keeps_only_the_connected_area_of_the_seed_colour()
    {
        // a 5×3 "field" with an anti-aliased corner, a rim of another colour and an unconnected same-colour pixel
        uint[] pixels =
        [
            Rim,  Rim,   Rim,   Rim,   Field,
            Rim,  Edge,  Field, Field, Rim,
            Clear, Rim,  Field, Rim,   Rim,
        ];

        SpriteStore.KeepRegions(pixels, 5, 3, [(2, 1)]);

        Assert.Equal(new uint[]
        {
            0, 0, 0, 0, 0,
            0, Edge, Field, Field, 0,
            0, 0, Field, 0, 0,
        }, pixels);
    }

    [Fact]
    public void Seeds_outside_or_on_transparent_pixels_select_nothing()
    {
        uint[] pixels = [Field, Clear, Field, Field];
        SpriteStore.KeepRegions(pixels, 2, 2, [(-1, 0), (5, 5), (1, 0)]);
        Assert.All(pixels, p => Assert.Equal(0u, p));
    }
}
