namespace DRPG.Engine.Rendering;

/// <summary>Post-processing of rendered text.</summary>
public static class TextPixels
{
    /// <summary>The coverage (alpha) from which a pixel of a glyph is drawn when the edges are not smoothed: half.</summary>
    public const byte SolidThreshold = 0x80;

    /// <summary>
    /// Turns smoothed glyphs (RGBA8888, alpha in the high byte) into hard ones, as GDI's <c>TextOutA</c> draws the bitmap
    /// GulimChe of the v322: a pixel with at least <see cref="SolidThreshold"/> coverage becomes opaque, the others transparent.
    /// The colour is left as it is, so the text keeps its full colour instead of fading into the background at the edges.
    /// </summary>
    public static void Harden(Span<uint> rgba)
    {
        for (int i = 0; i < rgba.Length; i++)
        {
            uint alpha = rgba[i] >> 24;
            rgba[i] = alpha >= SolidThreshold ? rgba[i] | 0xFF000000 : 0;
        }
    }
}
