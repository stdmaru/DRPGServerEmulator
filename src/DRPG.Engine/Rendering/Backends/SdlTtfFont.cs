using SDL;
using static SDL.SDL3;
using static SDL.SDL3_ttf;

namespace DRPG.Engine.Rendering.Backends;

/// <summary><see cref="IFont"/> on SDL3_ttf (FreeType). The only file that touches SDL_ttf.</summary>
public sealed unsafe class SdlTtfFont : IFont
{
    private static bool _initialized;
    private TTF_Font* _font;

    /// <summary>Opens <paramref name="path"/> with an em of <paramref name="pixelSize"/> pixels. Not GDI's cell
    /// height: fallback faces such as Noto CJK have tall line gaps and came out too small that way.</summary>
    public SdlTtfFont(string path, int pixelSize)
    {
        if (!_initialized)
        {
            if (!TTF_Init())
                throw new InvalidOperationException($"TTF_Init: {SDL_GetError()}");
            _initialized = true;
        }
        _font = TTF_OpenFont(path, pixelSize); // points at 72 dpi = pixels
        if (_font == null)
            throw new InvalidOperationException($"TTF_OpenFont({path}): {SDL_GetError()}");
    }

    public int LineHeight => TTF_GetFontHeight(_font);

    public int Ascent => TTF_GetFontAscent(_font);

    public ITexture Render(IRenderer renderer, string text, uint rgba)
    {
        if (text.Length == 0)
            return renderer.CreateTexture(0, 0, []);
        var color = new SDL_Color { r = (byte)rgba, g = (byte)(rgba >> 8), b = (byte)(rgba >> 16), a = (byte)(rgba >> 24) };
        var surface = TTF_RenderText_Blended(_font, text, 0, color);
        if (surface == null)
            throw new InvalidOperationException($"TTF_RenderText_Blended: {SDL_GetError()}");
        var converted = SDL_ConvertSurface(surface, SDL_PixelFormat.SDL_PIXELFORMAT_ABGR8888);
        SDL_DestroySurface(surface);
        if (converted == null)
            throw new InvalidOperationException($"SDL_ConvertSurface: {SDL_GetError()}");
        try
        {
            int w = converted->w, h = converted->h;
            var pixels = new uint[w * h];
            for (int y = 0; y < h; y++)
                new ReadOnlySpan<uint>((byte*)converted->pixels + y * converted->pitch, w).CopyTo(pixels.AsSpan(y * w, w));
            return renderer.CreateTexture(w, h, pixels);
        }
        finally
        {
            SDL_DestroySurface(converted);
        }
    }

    public void Dispose()
    {
        if (_font != null)
            TTF_CloseFont(_font);
        _font = null;
    }
}
