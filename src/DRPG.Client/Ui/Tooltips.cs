using DRPG.Engine.Rendering;

namespace DRPG.Client.Ui;

/// <summary>
/// The two tooltip boxes of the original, both drawn by the object <c>DAT_01158510</c> at the moment the owner sees its control hot
/// (no delay, no fade): there is no tooltip queue, the box is drawn where the owner's draw is.
/// </summary>
public sealed class Tooltips(ITexture[] panel, MessageBox box, UiText text)
{
    /// <summary>✅ <c>Interface\w_panel.spr</c>: one 800×600 frame, cut to the box.</summary>
    public const string PanelSprite = "interface\\w_panel.spr";

    /// <summary>✅ The arguments the bar passes to <see cref="DrawPanel"/>: 3 px around the text, mixed at 22/32.</summary>
    public const int PanelPadding = 3, PanelAlpha = 0x16;

    /// <summary>✅ <c>0x007dcfb0(…, 0x20)</c>: 32/32 draws the box plain.</summary>
    public const int OpaqueBox = IRenderer.OpaqueAlpha;

    /// <summary>
    /// ✅ <c>0x007de1b0(text, x, y, w, h, padding, alpha)</c>: the text's size when <paramref name="width"/>/<paramref name="height"/> are 0,
    /// plus <paramref name="padding"/> on each side; the top-left of <c>w_panel</c> cut to that size at (x, y) mixed at
    /// <paramref name="alpha"/>/32 (mode −1 of <c>0x00b569f0</c>: plain without the <c>alpha</c> option), then the text in white at
    /// (x + padding, y + padding), with no shadow (<c>0x00b55760</c>, a transparent <c>TextOut</c>).
    /// </summary>
    public void DrawPanel(IRenderer renderer, SpriteOptions options, string message, int x, int y,
        int width = 0, int height = 0, int padding = PanelPadding, int alpha = PanelAlpha)
    {
        var (textWidth, textHeight) = text.Measure(renderer, message);
        if (width == 0)
            width = textWidth;
        if (height == 0)
            height = textHeight;
        width += padding * 2;
        height += padding * 2;
        if (panel.Length > 0)
            options.DrawRegionBlended(renderer, panel[0], x, y, Math.Min(width, panel[0].Width), Math.Min(height, panel[0].Height), alpha);
        text.Draw(renderer, message, x + padding, y + padding, UiText.White, shadow: false);
    }

    /// <summary>✅ <c>0x007dcfb0(x, y, w, h, 0x20)</c>: the 9-part box of <c>GUI\dr2_in_000_003</c>, plain.</summary>
    public void DrawBox(IRenderer renderer, int x, int y, int width, int height) => box.DrawBox(renderer, x, y, width, height);

    /// <summary>The text inside a <see cref="DrawBox"/>: <c>0x00b55270(0, text, x, y, 0xffffff, 1, 0)</c>, white with shadow.</summary>
    public void DrawBoxText(IRenderer renderer, string message, int x, int y) => text.Draw(renderer, message, x, y);

    /// <summary>The width of a text, for the boxes that size themselves on it (<c>0x00b56310</c>).</summary>
    public int Measure(IRenderer renderer, string message) => text.Measure(renderer, message).Width;
}
