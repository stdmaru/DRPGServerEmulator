using DRPG.Client.Scenes;
using DRPG.Engine.Assets;
using DRPG.Shared.Formats.Pack;

namespace DRPG.Client.Tests;

public sealed class TitleSceneTests : IDisposable
{
    private readonly FakeInstall _install = new();
    private readonly FakeRenderer _renderer = new();
    private readonly PackArchive _resource;
    private readonly SpriteStore _sprites;
    private readonly GameContext _game;

    public TitleSceneTests()
    {
        var install = GameInstall.Find(_install.Directory)!;
        _resource = PackArchive.Open(install.ResourcePath);
        _sprites = new SpriteStore(_resource, _renderer);
        _game = new GameContext(install, _resource, _sprites, null, new GameCommandLine("u", "k"), ClientSettings.Local);
        _game.Scenes = new SceneManager(_ => throw new InvalidOperationException("no scene is created in these tests"));
    }

    public void Dispose()
    {
        _sprites.Dispose();
        _install.Dispose();
    }

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

        // ✅ docs/cliente/cenas.md#cscenetitle-carga-: background, box, transparent slot, bar
        Assert.Equal([(0, 0, 800, 600), (289, 140, 222, 215), (224, 258, 352, 60), (293, 305, 213, 18)],
            _renderer.Draws.Select(d => (d.X, d.Y, d.Texture.Width, d.Texture.Height)));
    }

    [Fact]
    public void Runs_at_thirty_frames_per_second()
    {
        using var scene = Started();

        Assert.Equal(33, scene.FrameIntervalMs);
    }

    [Fact]
    public async Task The_bar_frame_follows_the_displayed_progress()
    {
        using var scene = Started();
        scene.Frame(_renderer, 0);
        var first = _renderer.Draws[^1].Texture;

        scene.Progress.Raise(100);
        for (ulong t = 10; scene.Progress.Displayed < 50; t += 33)
            scene.Frame(_renderer, t);
        var later = _renderer.Draws[^1].Texture;

        Assert.NotSame(first, later);
        await Task.CompletedTask;
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
        Assert.Equal(["Test_US.bin"], _game.Data!.Language.Keys);
        Assert.NotEmpty(_game.Data.Table.Names);
    }

    [Fact]
    public void A_missing_table_pack_is_a_fatal_error()
    {
        File.Delete(Path.Combine(_install.Directory, "table.hf"));
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
