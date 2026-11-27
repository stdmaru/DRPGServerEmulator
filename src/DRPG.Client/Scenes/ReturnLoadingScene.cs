using DRPG.Engine.Platform;
using DRPG.Client.Ui;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Scenes;

/// <summary>
/// The screen the original shows for 3 s before going back to scene 2 (✅ <c>FUN_00b500a0(2)</c> with
/// <c>[DAT_0135b150]+0x690 == 0</c>: 30 × <c>FUN_00b34250(i % 12)</c> and <c>Sleep(100)</c>, input not read). Each step draws the
/// <see cref="LoadingScreen"/> with one background drawn at random once, the tip piece <c>n</c> (random 1..15, once) and the gauge
/// frame <c>i % 12</c>. Then scene 2 is created again (which logs in again).
/// </summary>
public sealed class ReturnLoadingScene(GameContext game, Random random) : IScene
{
    public const int Steps = 30, StepMs = 100;

    private LoadingScreen _screen = null!;                     // set in Init
    private int _background;
    private string _tip = "";
    private int _step;

    public int FrameIntervalMs => StepMs;

    /// <summary>✅ <c>0x00b34250</c> does not call <c>FUN_00b3e3d0</c>: the cursor is hidden during the 3 s loading screen.</summary>
    public bool ShowCursor => false;

    public void Init()
    {
        _screen = new LoadingScreen(game);
        _background = _screen.BackgroundCount > 0 ? random.Next(_screen.BackgroundCount) : -1;
        _tip = _screen.TipOf(random.Next(15) + 1);
    }

    public void Handle(InputEvent input)
    {
    }

    public void Frame(IRenderer renderer, ulong nowMs)
    {
        _screen.Draw(renderer, _background, _step % LoadingScreen.GaugeFrames, _tip);
        if (++_step >= Steps)
        {
            game.SkipReturnLoading = true;
            game.Scenes.Switch(SceneId.Server);
        }
    }

    public void Dispose()
    {
    }
}
