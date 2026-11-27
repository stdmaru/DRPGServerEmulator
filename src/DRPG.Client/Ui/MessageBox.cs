using DRPG.Engine.Rendering;

namespace DRPG.Client.Ui;

/// <summary>
/// The 9-part message and dialog box of the original (✅ <c>FUN_007dd610</c> for centred single-line messages and
/// <c>FUN_007dcfb0(x, y, w, h, 0x20)</c> for boxes of variable height, sprite <c>GUI\dr2_in_000_003</c>): corners and
/// edges of 14 px, horizontal edges repeated every 7 px.
/// </summary>
public sealed class MessageBox(ITexture[] parts, UiText text)
{
    private const int Corner = 14, Step = 7, Padding = 28;

    /// <summary>The sprite has nine frames: 0..2 top row, 3..5 middle rows, 6..8 bottom row.</summary>
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

    /// <summary>
    /// ✅ <c>FUN_007dcfb0(x, y, w, h, 0x20)</c>: draws a 9-part box of variable height:
    /// <c>n = (w - 14) / 7</c> horizontal repeats (middle tile drawn <c>n + 1</c> times, right tile at <c>x + 14 + 7n</c>)
    /// and <c>m = (h - 28) / 14 + 1</c> middle rows of 14 px.
    /// </summary>
    public void DrawBox(IRenderer renderer, int x, int y, int width, int height)
    {
        if (parts.Length < FrameCount)
            return;
        int repeats = (width - Corner) / Step;                  // ✅ iVar4 of 0x007dcfb0
        int middleRows = (height - 2 * Corner) / Corner + 1;    // ✅ param_3 of 0x007dcfb0
        DrawRow(renderer, x, y, repeats, 0);
        int rowY = y;
        for (int row = 0; row < middleRows; row++)
        {
            rowY += Corner;
            DrawRow(renderer, x, rowY, repeats, 3);
        }
        DrawRow(renderer, x, rowY + Corner, repeats, 6);
    }

    private void DrawRow(IRenderer renderer, int left, int y, int repeats, int firstFrame)
    {
        renderer.Draw(parts[firstFrame], left, y);
        for (int i = 0; i <= repeats; i++)
            renderer.Draw(parts[firstFrame + 1], left + Corner + i * Step, y);
        renderer.Draw(parts[firstFrame + 2], left + Corner + Step * repeats, y);
    }
}
