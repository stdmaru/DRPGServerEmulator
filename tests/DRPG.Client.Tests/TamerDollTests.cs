using DRPG.Engine.Assets;
using DRPG.Client.Data;
using DRPG.Client.Ui;

namespace DRPG.Client.Tests;

public sealed class TamerDollTests
{
    [Fact]
    public void The_tamer_is_the_body_and_its_default_clothes_in_the_original_order_all_by_the_same_corner()
    {
        using var fixture = new SceneFixture();
        var animated = new AnimatedSprites(fixture.Resource, fixture.Sprites, fixture.Log.Add);
        var doll = animated.OpenTamer(1)!;

        doll.Draw(fixture.Renderer, 10, 20);

        // body (100 wide), a_15, a_16, a_17, a_16 again, a_18, a_19, (no a_20 in the pack), a_21
        Assert.Equal([100, 15, 16, 17, 16, 18, 19, 21], fixture.Renderer.Draws.Select(d => d.Texture.Width));
        Assert.All(fixture.Renderer.Draws, d => Assert.Equal((10, 20), (d.X, d.Y)));
        Assert.Contains(fixture.Log, line => line.Contains("a_20_01_00.spr"));
    }

    [Fact]
    public void A_layer_without_the_frame_of_the_body_is_skipped()
    {
        using var fixture = new SceneFixture();
        var doll = new AnimatedSprites(fixture.Resource, fixture.Sprites, fixture.Log.Add).OpenTamer(1)!;
        doll.Body.Restart(SpriteAnimator.Selected);         // frame 8: a_19 has only 4 frames

        doll.Draw(fixture.Renderer, 0, 0);

        Assert.DoesNotContain(fixture.Renderer.Draws, d => d.Texture.Width == 19);
        Assert.Equal(7, fixture.Renderer.Draws.Count);
    }

    [Fact]
    public void Only_the_frames_that_are_drawn_are_uploaded()
    {
        using var fixture = new SceneFixture();
        var doll = new AnimatedSprites(fixture.Resource, fixture.Sprites, fixture.Log.Add).OpenTamer(1)!;
        var clothes = Assert.IsType<SpriteFrames>(doll.Layers[0]);
        Assert.Equal(0, clothes.Uploaded);       // opening the screen uploads nothing (it froze for 2 s uploading all)

        doll.Draw(fixture.Renderer, 0, 0);

        Assert.Equal(1, clothes.Uploaded);
        Assert.Equal(16, clothes.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]       // no a_01.ani in the test pack
    public void A_tamer_without_a_body_is_not_drawn(int tamerId)
    {
        using var fixture = new SceneFixture();
        Assert.Null(new AnimatedSprites(fixture.Resource, fixture.Sprites, fixture.Log.Add).OpenTamer(tamerId));
    }
}
