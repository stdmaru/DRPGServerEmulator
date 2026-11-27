using DRPG.Client.Scenes;
using DRPG.Engine.Assets;
using DRPG.Shared.Formats.Pack;

namespace DRPG.Client.Tests;

public sealed class TitleSceneTests : IDisposable
{
    private readonly SceneFixture _fixture = new();
    private FakeRenderer _renderer => _fixture.Renderer;
    private GameContext _game => _fixture.Game;

    public void Dispose() => _fixture.Dispose();

    private TitleScene Started()
    {
        var scene = new TitleScene(_game);
        scene.Init();
        return scene;
    }

    [Fact]
    public void Draws_the_sprites_at_the_positions_of_the_original_in_order()
    {
        using var scene = Started();

        scene.Frame(_renderer, 0);

        // ✅ docs/cliente/cenas.md#cscenetitle-carga-: background, box, transparent slot, bar (frame 0 clipped), outline (frame 1)
        Assert.Equal([(0, 0, 800, 600), (289, 140, 222, 215), (224, 258, 352, 60), (293, 305, 231, 18), (293, 305, 231, 18)],
            _renderer.Draws.Select(d => (d.X, d.Y, d.Texture.Width, d.Texture.Height)));
    }

    [Fact]
    public void Runs_at_thirty_frames_per_second()
    {
        using var scene = Started();

        Assert.Equal(33, scene.FrameIntervalMs);
    }

    [Fact]
    public void The_bar_is_the_clipped_full_frame_and_then_the_whole_outline()
    {
        using var scene = Started();
        scene.Frame(_renderer, 0);

        var clipped = _renderer.Draws[3];
        var outline = _renderer.Draws[4];

        Assert.Equal(1, clipped.PartWidth);                // ✅ 1 + 230 × 0 / 100
        Assert.Null(outline.PartWidth);                    // frame 1 is not clipped
        Assert.NotSame(clipped.Texture, outline.Texture);  // two different frames of the sprite
    }

    [Fact]
    public void The_clip_grows_with_the_displayed_progress()
    {
        using var scene = Started();
        scene.Frame(_renderer, 0);

        scene.Progress.Raise(100);
        for (ulong t = 10; scene.Progress.Displayed < 50; t += 33)
        {
            _renderer.BeginFrame();
            scene.Frame(_renderer, t);
        }

        Assert.InRange(_renderer.Draws[3].PartWidth!.Value, 114, 120);
    }

    [Fact]
    public void Asks_for_the_server_scene_when_the_load_ends_and_keeps_the_data()
    {
        SceneId? requested = null;
        var manager = new SceneManager(id =>
        {
            requested = id;
            throw new InvalidOperationException("stop here");
        });
        _game.Scenes = manager;
        using var scene = Started();

        WaitFor(() =>
        {
            scene.Frame(_renderer, 0);
            return _game.Data is not null;
        });
        manager.Switch(SceneId.Server); // what the scene did, applied below
        var error = Assert.Throws<InvalidOperationException>(() => manager.Frame(_renderer, 0));

        Assert.Equal("stop here", error.Message);
        Assert.Equal(SceneId.Server, requested);
        Assert.Equal(["TextAttach_US.bin"], _game.Data!.Language.Keys);
        Assert.NotEmpty(_game.Data.Table.Names);
        Assert.Equal("Net Ocean", _game.Texts[1498]);          // the interface texts are ready for the next scene
    }

    [Fact]
    public void A_missing_table_pack_is_a_fatal_error()
    {
        File.Delete(Path.Combine(_fixture.Install.Directory, "table.hf"));
        using var scene = Started();

        WaitFor(() =>
        {
            scene.Frame(_renderer, 0);
            return _game.FatalError is not null;
        });

        Assert.Contains("Loading failed", _game.FatalError);
        Assert.Null(_game.Data);
    }

    private static void WaitFor(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            Thread.Sleep(5);
        }
    }
}
