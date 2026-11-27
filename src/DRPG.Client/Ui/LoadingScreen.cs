using DRPG.Client.Scenes;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Ui;

/// <summary>
/// The generic loading screen of the original (✅ <c>CLoadingMgr</c>, sprites loaded in <c>0x00b34980</c>), the same in its two uses:
/// the return to scene 2 (<c>0x00b34250</c>, <see cref="ReturnLoadingScene"/>) and the world entry (the loading thread
/// <c>0x00b34ea0</c>, <see cref="GameScene"/>). One frame, in this order:
/// <list type="bullet">
/// <item><c>GUI\loading_image_ENG.spr</c> (member <c>+0x7c</c>), one frame drawn at random once, at (0, 0);</item>
/// <item><c>GUI\loading_under_line.spr</c> (<c>+0x5ec</c>) frame 0 at (0, 550) (the thread draws the gauge before it; nothing overlaps);</item>
/// <item>a tip, piece <c>n</c> of the text <c>0xa6b</c> split at <c>†</c> (<c>FUN_0091b7d0</c>), yellow (COLORREF <c>0xffff</c>)
/// with a black shadow one pixel down and right, centred on the 800 px width at y 580 (<c>0x00b55560</c> ignores the x it is given);</item>
/// <item><c>GUI\game_end_gauge.spr</c> (<c>+0x658</c>) at (740, 540), the frame turning (<see cref="GaugeFrames"/>).</item>
/// </list>
/// The cursor is not drawn (neither caller calls <c>0x00b3e3d0</c>).
/// </summary>
public sealed class LoadingScreen
{
    /// <summary>
    /// ✅ The gauge turns through 12 frames: <c>i % 0xc</c> in <c>0x00b500a0</c>, and in the thread the frame count of
    /// <c>GUI\loading_gauge.spr</c> (<c>+0x48</c>, 12 frames in the pack).
    /// </summary>
    public const int GaugeFrames = 12;

    /// <summary>✅ The text of the tips; piece 0 is the one of the world entry before the map (<c>+0x5e4</c>).</summary>
    public const int TipText = 0xa6b;

    public const int UnderLineY = 550, TipY = 580, GaugeX = 740, GaugeY = 540;

    private readonly GameContext _game;
    private readonly IReadOnlyList<ITexture> _images;
    private readonly ITexture[] _underLine, _gauge;

    public LoadingScreen(GameContext game)
    {
        _game = game;
        _images = game.Sprites.GuiLazy("loading_image");           // big, and only one frame is shown
        _underLine = game.Sprites.Gui("loading_under_line");
        _gauge = game.Sprites.Gui("game_end_gauge");
    }

    /// <summary>How many background images there are (the random pick is <c>rand() % count</c>).</summary>
    public int BackgroundCount => _images.Count;

    /// <summary>✅ <c>FUN_0091b7d0(0xa6b, n)</c>: the n-th piece of the text split at the line breaks; empty when there is none.</summary>
    public static string Tip(string text, int n)
    {
        var pieces = text.Split('†');
        return n >= 0 && n < pieces.Length ? pieces[n] : "";
    }

    public string TipOf(int n) => Tip(_game.Texts[TipText], n);

    public void Draw(IRenderer renderer, int background, int gaugeFrame, string tip)
    {
        if (background >= 0 && background < _images.Count)
            renderer.Draw(_images[background], 0, 0);
        if (_underLine.Length > 0)
            renderer.Draw(_underLine[0], 0, UnderLineY);
        int width = _game.Text.Measure(renderer, tip).Width;
        _game.Text.Draw(renderer, tip, (800 - width) / 2, TipY, UiText.Yellow);
        if (gaugeFrame >= 0 && gaugeFrame < _gauge.Length)
            renderer.Draw(_gauge[gaugeFrame], GaugeX, GaugeY);
    }
}
