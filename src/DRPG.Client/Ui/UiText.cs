using DRPG.Engine.Rendering;

namespace DRPG.Client.Ui;

/// <summary>
/// Text as the original draws it (✅ <c>FUN_00b55270(font, text, x, y, color, shadow, shadowColor)</c>): GDI
/// <c>TextOutA</c> at the top left corner, transparent background, and with <c>shadow</c> the same text first at
/// (x + 1, y + 1) in the shadow colour (0 = black). The colour is a COLORREF (<c>0x00BBGGRR</c>): <c>0xffffff</c> white,
/// <c>0xffff</c> yellow, <c>0x4080ff</c> orange. Rendered lines are cached by text and colour.
/// ❓ The original font is GulimChe 12 px; the font given here is whatever the client found (docs/cliente/tela-login-launcher.md#fonte).
/// Without a font nothing is drawn and a line measures as 6 × 12 px per character.
/// </summary>
public sealed class UiText(IFont? font) : IDisposable
{
    public const uint White = 0xFFFFFF, Yellow = 0xFFFF, Orange = 0x4080FF, NameHighlight = 0x63CBFF, Black = 0;

    /// <summary>Lines kept before the cache is emptied; the scenes draw a few dozen different lines.</summary>
    private const int MaxCached = 400;

    private const int FallbackCharacterWidth = 6;

    /// <summary>
    /// ✅ The GDI cell of the original's font, GulimChe 12 px (<c>0x00b563f0</c>): every text box and message box of the
    /// original is laid out with this height (<c>FUN_00b561f0</c> measures with <c>GetTextExtentPoint</c>).
    /// </summary>
    public const int CellHeight = 12;

    /// <summary>
    /// ❓ GulimChe's ascent in that cell (10 above the baseline, 2 below, from the face's metrics). A substitute face is
    /// drawn with its baseline here: Noto Sans CJK at 12 px has a 17 px line and a 14 px ascent, and drawn by its top
    /// it sat 3–4 px low inside the message boxes.
    /// </summary>
    public const int CellAscent = 10;

    private readonly Dictionary<(string Text, uint Color), ITexture> _cache = [];

    public (int Width, int Height) Measure(IRenderer renderer, string text)
    {
        if (text.Length == 0)
            return (0, CellHeight);
        if (font is null)
            return (text.Length * FallbackCharacterWidth, CellHeight);
        return (Line(renderer, text, White).Width, CellHeight);
    }

    /// <summary>
    /// Breaks <paramref name="text"/> into lines no wider than <paramref name="maxWidth"/> px, at spaces; a word wider than a
    /// line is cut inside. ❓ The original breaks with <c>0x00b30d00</c>, whose rule is not known.
    /// </summary>
    public List<string> Wrap(IRenderer renderer, string text, int maxWidth)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (Measure(renderer, candidate).Width <= maxWidth)
            {
                current = candidate;
                continue;
            }
            if (current.Length > 0)
                lines.Add(current);
            current = word;
            while (current.Length > 1 && Measure(renderer, current).Width > maxWidth)
            {
                int fit = current.Length - 1;
                while (fit > 1 && Measure(renderer, current[..fit]).Width > maxWidth)
                    fit--;
                lines.Add(current[..fit]);
                current = current[fit..];
            }
        }
        if (current.Length > 0)
            lines.Add(current);
        return lines;
    }

    /// <summary>Draws <paramref name="text"/> at (x, y) in <paramref name="colorRef"/>, with the shadow unless <paramref name="shadow"/> is false.</summary>
    public void Draw(IRenderer renderer, string text, int x, int y, uint colorRef = White, bool shadow = true, uint shadowColorRef = Black)
    {
        if (font is null || text.Length == 0)
            return;
        y += CellAscent - font.Ascent;          // the substitute's baseline where GulimChe's would be
        if (shadow)
            renderer.Draw(Line(renderer, text, shadowColorRef), x + 1, y + 1);
        renderer.Draw(Line(renderer, text, colorRef), x, y);
    }

    /// <summary>
    /// Draws <paramref name="text"/> at (x, y) clipped to the rectangle [<paramref name="clipLeft"/>, <paramref name="clipRight"/>)
    /// × [<paramref name="clipTop"/>, <paramref name="clipBottom"/>) (✅ <c>0x00b55bb0</c> <c>ExtTextOutA</c> with <c>ETO_CLIPPED</c>).
    /// </summary>
    public void DrawClippedRect(IRenderer renderer, string text, int x, int y, int clipLeft, int clipTop, int clipRight, int clipBottom,
        uint colorRef = White, bool shadow = true, uint shadowColorRef = Black)
    {
        if (font is null || text.Length == 0 || clipRight <= clipLeft || clipBottom <= clipTop)
            return;
        int drawY = y + CellAscent - font.Ascent;
        if (shadow)
            DrawLineClipped(renderer, Line(renderer, text, shadowColorRef), x + 1, drawY + 1, clipLeft, clipTop, clipRight, clipBottom);
        DrawLineClipped(renderer, Line(renderer, text, colorRef), x, drawY, clipLeft, clipTop, clipRight, clipBottom);
    }

    private static void DrawLineClipped(IRenderer renderer, ITexture line, int x, int y, int clipLeft, int clipTop, int clipRight, int clipBottom)
    {
        int left = Math.Max(x, clipLeft);
        int top = Math.Max(y, clipTop);
        int right = Math.Min(x + line.Width, clipRight);
        int bottom = Math.Min(y + line.Height, clipBottom);
        if (right <= left || bottom <= top)
            return;
        renderer.DrawRegion(line, left, top, left - x, top - y, right - left, bottom - top);
    }

    /// <summary>
    /// Draws the tail of <paramref name="text"/> that fits within <paramref name="maxWidth"/> px (for edit boxes so the
    /// caret stays visible) and returns the drawn width in pixels.
    /// </summary>
    public int DrawTail(IRenderer renderer, string text, int x, int y, int maxWidth, uint colorRef = White, bool shadow = true, uint shadowColorRef = Black)
    {
        if (text.Length == 0 || maxWidth <= 0)
            return 0;
        if (font is null)
            return Math.Min(text.Length * FallbackCharacterWidth, maxWidth);
        y += CellAscent - font.Ascent;
        var line = Line(renderer, text, colorRef);
        int shown = Math.Min(line.Width, maxWidth);
        int sourceX = line.Width - shown;
        if (shadow)
        {
            var shadowLine = Line(renderer, text, shadowColorRef);
            renderer.DrawPart(shadowLine, x + 1, y + 1, sourceX, shown);
        }
        renderer.DrawPart(line, x, y, sourceX, shown);
        return shown;
    }

    private ITexture Line(IRenderer renderer, string text, uint colorRef)
    {
        var key = (text, colorRef);
        if (_cache.TryGetValue(key, out var texture))
            return texture;
        if (_cache.Count >= MaxCached)
            Clear();
        // a COLORREF has the red in the lowest byte, like the textures; the alpha is added
        texture = font!.Render(renderer, text, 0xFF000000 | (colorRef & 0xFFFFFF));
        _cache[key] = texture;
        return texture;
    }

    private void Clear()
    {
        foreach (var texture in _cache.Values)
            texture.Dispose();
        _cache.Clear();
    }

    public void Dispose() => Clear();
}
