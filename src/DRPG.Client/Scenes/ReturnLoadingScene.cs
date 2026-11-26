using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Scenes;

/// <summary>
/// The screen the original shows for 3 s before going back to scene 2 (✅ <c>FUN_00b500a0(2)</c> with
/// <c>[DAT_0135b150]+0x690 == 0</c>: 30 × <c>FUN_00b34250(i % 12)</c> and <c>Sleep(100)</c>, input not read). Each step
/// (<c>0x00b34250</c>, sprites of <c>0x00b34980</c>):
/// <list type="bullet">
/// <item><c>GUI\loading_image_ENG.spr</c>, one frame drawn at random once, at (0, 0);</item>
/// <item><c>GUI\loading_under_line.spr</c> frame 0 at (0, 550);</item>
/// <item>a tip, line <c>n</c> (random 1..15, once) of text <c>0xa6b</c> split at <c>†</c>, yellow with a black shadow,
/// centred on the 800 px width at y 580 (<c>FUN_00b55560</c>);</item>
/// <item><c>GUI\game_end_gauge.spr</c> frame <c>i % 12</c> at (740, 540).</item>
/// </list>
/// Then scene 2 is created again (which logs in again).
/// </summary>
public sealed class ReturnLoadingScene(GameContext game, Random random) : IScene
{
    public const int Steps = 30, StepMs = 100, GaugeFrames = 12, TipText = 0xa6b;

    private ITexture? _image;
    private ITexture[] _underLine = [], _gauge = [];
    private string _tip = "";
    private int _step;

    public int FrameIntervalMs => StepMs;

    public void Init()
    {
        var images = game.Sprites.GuiLazy("loading_image");   // big, and only one frame is shown
        if (images.Count > 0)
            _image = images[random.Next(images.Count)];
        _underLine = game.Sprites.Gui("loading_under_line");
        _gauge = game.Sprites.Gui("game_end_gauge");
        _tip = Tip(game.Texts[TipText], random.Next(15) + 1);
    }

    /// <summary>✅ <c>FUN_0091b7d0(0xa6b, n)</c>: the n-th piece of the text split at the line breaks; empty when there is none.</summary>
    internal static string Tip(string text, int n)
    {
        var pieces = text.Split('†');
        return n >= 0 && n < pieces.Length ? pieces[n] : "";
    }

    public void Handle(InputEvent input)
    {
    }

    public void Frame(IRenderer renderer, ulong nowMs)
    {
        if (_image is not null)
            renderer.Draw(_image, 0, 0);
        if (_underLine.Length > 0)
            renderer.Draw(_underLine[0], 0, 550);
        int width = game.Text.Measure(renderer, _tip).Width;
        game.Text.Draw(renderer, _tip, (800 - width) / 2, 580, Ui.UiText.Yellow);
        int frame = _step % GaugeFrames;
        if (frame < _gauge.Length)
            renderer.Draw(_gauge[frame], 740, 540);
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
