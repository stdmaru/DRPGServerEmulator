using DRPG.Engine.Rendering;

namespace DRPG.Engine.Tests;

public sealed class TextPixelsTests
{
    [Theory]
    [InlineData(0xFF80FFFFu, 0xFF80FFFFu)]      // fully covered: unchanged
    [InlineData(0x8080FFFFu, 0xFF80FFFFu)]      // half: opaque, full colour
    [InlineData(0x7F80FFFFu, 0u)]               // under half: gone
    [InlineData(0x0080FFFFu, 0u)]
    [InlineData(0u, 0u)]
    public void Harden_keeps_the_colour_and_makes_the_alpha_all_or_nothing(uint pixel, uint expected)
    {
        uint[] pixels = [pixel];

        TextPixels.Harden(pixels);

        Assert.Equal(expected, pixels[0]);
    }

    [Fact]
    public void Harden_of_nothing_is_nothing()
    {
        TextPixels.Harden(Span<uint>.Empty);
    }
}
