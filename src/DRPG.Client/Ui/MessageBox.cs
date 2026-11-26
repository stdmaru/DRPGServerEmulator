using DRPG.Engine.Rendering;

namespace DRPG.Client.Ui;

/// <summary>
/// The 9-part message box of the original (✅ <c>FUN_007dd610(text, x, y, mode, color)</c>, sprite
/// <c>GUI\dr2_in_000_003</c>), centred on (x, y): corners and edges of 14 px, the top and bottom edges repeated every
/// 7 px to fit the text. Only the centred form (mode other than 1 and 2) is drawn.
/// </summary>
public sealed class MessageBox(ITexture[] parts, UiText text)
{
    private const int Corner = 14, Step = 7, Padding = 28;

    /// <summary>The sprite has nine frames: 0..2 top row, 3..5 middle (not drawn by the original), 6..8 bottom row.</summary>
    public const int FrameCount = 9;

    public void Draw(IRenderer renderer, string message, int centerX, int centerY, uint colorRef = UiText.White)
    {
        if (parts.Length < FrameCount)
            return;
        var (width, height) = text.Measure(renderer, message);
        int left = centerX - Corner - width / 2;
        int top = centerY - Corner - height / 2;
        int repeats = (width - 24) / Step + 2;                  // ✅ n of 0x007dd610
        DrawRow(renderer, left, top, repeats, 0);
        DrawRow(renderer, left, top + Corner, repeats, 6);
        text.Draw(renderer, message, left + (Step * repeats - width + Padding) / 2, top + 9, colorRef);
    }

    private void DrawRow(IRenderer renderer, int left, int y, int repeats, int firstFrame)
    {
        renderer.Draw(parts[firstFrame], left, y);
        for (int i = 0; i <= repeats; i++)
            renderer.Draw(parts[firstFrame + 1], left + Corner + i * Step, y);
        renderer.Draw(parts[firstFrame + 2], left + Corner + Step * repeats, y);
    }
}
