using DRPG.Client.Scenes;

namespace DRPG.Client.Tests;

public sealed class ReturnLoadingSceneTests
{
    [Fact]
    public void The_first_entry_to_scene_2_skips_the_screen_and_every_return_shows_it()
    {
        using var fixture = new SceneFixture();
        Assert.IsType<ServerScene>(Program.ServerOrReturnLoading(fixture.Game));
        Assert.IsType<ReturnLoadingScene>(Program.ServerOrReturnLoading(fixture.Game));
        Assert.IsType<ReturnLoadingScene>(Program.ServerOrReturnLoading(fixture.Game));
    }

    [Fact]
    public void Thirty_steps_of_100_ms_with_the_gauge_turning_then_scene_2()
    {
        using var fixture = new SceneFixture();
        fixture.Game.SkipReturnLoading = false;
        var switched = new List<SceneId>();
        fixture.Game.Scenes = new SceneManager(id =>
        {
            switched.Add(id);
            return Program.ServerOrReturnLoading(fixture.Game);
        });
        var scene = new ReturnLoadingScene(fixture.Game, new Random(1));
        scene.Init();
        Assert.Equal(100, scene.FrameIntervalMs);

        var gaugeFrames = new List<int>();
        for (int step = 0; step < ReturnLoadingScene.Steps; step++)
        {
            fixture.Renderer.Draws.Clear();
            scene.Frame(fixture.Renderer, 0);
            var gauge = fixture.Renderer.Draws.Single(d => (d.X, d.Y) == (740, 540));
            Assert.Equal(40, gauge.Texture.Width);
            Assert.Contains(fixture.Renderer.Draws, d => (d.X, d.Y, d.Texture.Width) == (0, 0, 800));
            Assert.Contains(fixture.Renderer.Draws, d => (d.X, d.Y, d.Texture.Width) == (0, 550, 800));
            gaugeFrames.Add(step % DRPG.Client.Ui.LoadingScreen.GaugeFrames);
            Assert.Empty(switched);                       // nothing switches before the last step has run
            if (step < ReturnLoadingScene.Steps - 1)
                Assert.False(fixture.Game.SkipReturnLoading);
        }

        Assert.True(fixture.Game.SkipReturnLoading);
        fixture.Game.Scenes.Frame(fixture.Renderer, 0);   // the switch asked in the last step happens here
        Assert.Equal([SceneId.Server], switched);
        Assert.IsType<ServerScene>(fixture.Game.Scenes.Current);
    }

    [Theory]
    [InlineData(1, " \"b\", ")]
    [InlineData(2, " \"c\"")]
    [InlineData(3, "")]          // past the end
    [InlineData(-1, "")]
    public void The_tip_is_a_piece_of_the_text_split_at_the_line_breaks(int n, string expected) =>
        Assert.Equal(expected, DRPG.Client.Ui.LoadingScreen.Tip("\"a\", † \"b\", † \"c\"", n));
}
