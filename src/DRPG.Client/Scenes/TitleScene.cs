using DRPG.Client.Startup;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Scenes;

/// <summary>
/// <c>CSceneTitle</c>, the loading screen (✅ v322, docs/cliente/cenas.md#cscenetitle-carga-). Init starts the real load
/// (<see cref="StartupLoader"/>); each frame advances the bar and draws the sprites; when the load ends it asks for
/// the server scene (<c>FUN_00b500a0(2)</c>). The original draws it from its own "PreLoadingBar" thread at 30 FPS.
/// </summary>
public sealed class TitleScene(GameContext game) : IScene
{
    // ✅ Sprite positions of 0x00b35560 (decimal of 0x121/0x8c, 0xe0/0x102, 0x125/0x131).
    private const int BoxX = 289, BoxY = 140;
    private const int SlotX = 224, SlotY = 258;
    private const int BarX = 293, BarY = 305;

    /// <summary>✅ <c>FUN_00867ed0(0x1e)</c>: 30 frames per second.</summary>
    private const int FramesPerSecond = 30;

    private const int MenuButtonSound = 5;
    private const string MenuButtonFile = "sound\\menu-bt.wav";

    private readonly LoadingProgress _progress = new();
    private readonly CancellationTokenSource _cancel = new();
    private Task<GameData>? _load;
    private ITexture _background = null!, _box = null!, _slot = null!; // set in Init
    private ITexture[] _bar = null!;                                   // set in Init

    public int FrameIntervalMs => 1000 / FramesPerSecond;

    public LoadingProgress Progress => _progress;

    public void Init()
    {
        var sprites = game.Sprites;
        _background = sprites.Gui("dr2_in_000_000")[0];   // ✅ GUI\dr2_in_000_000_ENG.spr
        _box = sprites.Gui("dr2_in_001_000")[0];          // "Loading" box
        _slot = sprites.Gui("dr2_in_001_001")[0];         // transparent
        _bar = sprites.Gui("dr2_in_001_002");
        game.Sounds.Register(MenuButtonSound, MenuButtonFile); // ✅ 0x00b34980
        // ✅ the loader reports to the progress from a pool thread, like the original's load runs apart from the bar
        _load = new StartupLoader(game.Install, game.Resource, _progress, game.Log).RunAsync(_cancel.Token);
    }

    public void Handle(InputEvent input)
    {
    }

    public void Frame(IRenderer renderer, ulong nowMs)
    {
        _progress.Tick(nowMs);
        renderer.Draw(_background, 0, 0);
        renderer.Draw(_box, BoxX, BoxY);
        renderer.Draw(_slot, SlotX, SlotY);
        // ✅ the full bar clipped to the progress, then the outline (frame 1) whole on top
        renderer.DrawPart(_bar[0], BarX, BarY, 0, _progress.BarFillWidth());
        if (_bar.Length > 1)
            renderer.Draw(_bar[1], BarX, BarY);
        game.DrawVersion(renderer, 0);
        Finish();
    }

    private void Finish()
    {
        if (_load is not { IsCompleted: true } load)
            return;
        if (load.IsFaulted)
            game.FatalError = $"Loading failed: {load.Exception!.GetBaseException().Message}";
        else if (load.IsCanceled)
            game.FatalError = "Loading was cancelled.";
        else
        {
            game.Data = load.Result;
            game.Sprites.Adopt(load.Result.Sprites);
            game.Scenes.Switch(SceneId.Server);
        }
        _load = null;
    }

    public void Dispose()
    {
        _cancel.Cancel();
        _cancel.Dispose();
    }
}
