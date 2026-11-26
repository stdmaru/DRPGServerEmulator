using DRPG.Client.Scenes;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Tests;

public sealed class SceneManagerTests
{
    private sealed class Probe(string name, List<string> log) : IScene
    {
        public Action<IRenderer>? OnFrame { get; init; }
        public int FrameIntervalMs => 0;
        public void Init() => log.Add($"{name}.init");
        public void Handle(InputEvent input) => log.Add($"{name}.handle");
        public void Frame(IRenderer renderer, ulong nowMs)
        {
            log.Add($"{name}.frame");
            OnFrame?.Invoke(renderer);
        }
        public void Dispose() => log.Add($"{name}.dispose");
    }

    [Fact]
    public void A_switch_takes_effect_after_the_running_frame_ends()
    {
        var log = new List<string>();
        SceneManager? manager = null;
        manager = new SceneManager(id => id == SceneId.Title
            ? new Probe("title", log) { OnFrame = _ => manager!.Switch(SceneId.Server) }
            : new Probe("server", log));
        using var renderer = new FakeRenderer();

        manager.Switch(SceneId.Title);
        manager.Frame(renderer, 0);
        manager.Frame(renderer, 1);

        // ✅ like FUN_00b500a0: the old scene is freed, then the new one is created and initialized
        Assert.Equal(["title.init", "title.frame", "title.dispose", "server.init", "server.frame"], log);
    }

    [Fact]
    public void Input_goes_to_the_current_scene_only()
    {
        var log = new List<string>();
        using var manager = new SceneManager(id => new Probe(id.ToString(), log));
        using var renderer = new FakeRenderer();
        manager.Handle(new QuitEvent());
        manager.Switch(SceneId.Title);
        manager.Frame(renderer, 0);

        manager.Handle(new MouseMoveEvent(1, 2));

        Assert.Equal(["Title.init", "Title.frame", "Title.handle"], log);
    }

    [Fact]
    public void Disposing_frees_the_current_scene()
    {
        var log = new List<string>();
        var manager = new SceneManager(id => new Probe(id.ToString(), log));
        using var renderer = new FakeRenderer();
        manager.Switch(SceneId.Title);
        manager.Frame(renderer, 0);

        manager.Dispose();

        Assert.Equal("Title.dispose", log[^1]);
    }
}
