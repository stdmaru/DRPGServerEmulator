using DRPG.Engine.Rendering;

namespace DRPG.Launcher.Scenes;

/// <summary>
/// Draws a 9-part box from <c>gui\dr2_in_000_003.spr</c> (✅ v322 <c>FUN_007dcfb0</c>: 14×14 corners, 7 px horizontal
/// step, 14 px vertical step) for the launcher's left-column panels.
/// </summary>
internal sealed class NineSliceBox(ITexture[] parts)
{
    private const int Corner = 14, Step = 7;

    public void Draw(IRenderer r, int x, int y, int width, int height)
    {
        if (parts.Length < 9)
            return;
        int repeats = Math.Max(0, (width - Corner) / Step);
        int middleRows = Math.Max(0, (height - 2 * Corner) / Corner + 1);
        DrawRow(r, x, y, repeats, 0);
        int rowY = y;
        for (int row = 0; row < middleRows; row++)
        {
            rowY += Corner;
            DrawRow(r, x, rowY, repeats, 3);
        }
        DrawRow(r, x, rowY + Corner, repeats, 6);
    }

    private void DrawRow(IRenderer r, int left, int y, int repeats, int firstFrame)
    {
        r.Draw(parts[firstFrame], left, y);
        for (int i = 0; i <= repeats; i++)
            r.Draw(parts[firstFrame + 1], left + Corner + i * Step, y);
        r.Draw(parts[firstFrame + 2], left + Corner + Step * repeats, y);
    }
}
