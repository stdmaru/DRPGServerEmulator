namespace DRPG.Launcher.Rendering;

/// <summary>
/// Where the fixed 800×600 frame goes inside an output of any size: centered, at the largest whole-number scale that
/// fits and never below 1×. A smaller output crops the frame (negative offset) instead of shrinking it, so sprites
/// are never scaled by a fraction.
/// </summary>
public readonly record struct FrameLayout(int Scale, int OffsetX, int OffsetY)
{
    public static FrameLayout For(int outputWidth, int outputHeight)
    {
        int fit = Math.Min(outputWidth / IRenderer.FrameWidth, outputHeight / IRenderer.FrameHeight);
        int scale = Math.Max(1, fit);
        return new FrameLayout(
            scale,
            (outputWidth - IRenderer.FrameWidth * scale) / 2,
            (outputHeight - IRenderer.FrameHeight * scale) / 2);
    }

    /// <summary>Output pixel to frame coordinate.</summary>
    public (int X, int Y) ToFrame(float outputX, float outputY) =>
        ((int)MathF.Floor((outputX - OffsetX) / Scale), (int)MathF.Floor((outputY - OffsetY) / Scale));
}
