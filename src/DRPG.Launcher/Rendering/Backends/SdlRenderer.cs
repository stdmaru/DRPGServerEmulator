using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace DRPG.Launcher.Rendering.Backends;

/// <summary>
/// <see cref="IRenderer"/> on the SDL3 2D renderer, asking for its <c>gpu</c> driver (SDL_GPU: Vulkan, D3D12, Metal)
/// and falling back to whatever SDL picks. The launcher only blits a few dozen sprites, so the 2D renderer and its
/// bundled shaders are enough; the game client gets a direct SDL_GPU backend behind the same interface.
/// The only file that touches SDL rendering calls (docs/arquitetura/cliente.md).
/// </summary>
public sealed unsafe class SdlRenderer : IRenderer
{
    private readonly SDL_Renderer* _renderer;

    // Layout of the 800×600 frame inside the window, recomputed every frame from the real output size.
    private FrameLayout _layout = new(1, 0, 0);

    public SdlRenderer(SDL_Window* window, string? preferredDriver = "gpu")
    {
        _renderer = preferredDriver is null ? null : SDL_CreateRenderer(window, preferredDriver);
        if (_renderer == null)
            _renderer = SDL_CreateRenderer(window, (string?)null);
        if (_renderer == null)
            throw new InvalidOperationException($"SDL_CreateRenderer: {SDL_GetError()}");
        BackendName = SDL_GetRendererName(_renderer) ?? "?";
        SDL_SetRenderVSync(_renderer, 1);
        // FixedScreen is done here, not with SDL's logical presentation: tiling compositors (Hyprland) resize the
        // window anyway, so every frame reads the real output size and lays the frame out itself (see BeginFrame).
    }

    public string BackendName { get; }

    public ITexture CreateTexture(int width, int height, ReadOnlySpan<uint> pixels)
    {
        if (pixels.Length < width * height)
            throw new ArgumentException("not enough pixels", nameof(pixels));
        var texture = SDL_CreateTexture(_renderer, SDL_PixelFormat.SDL_PIXELFORMAT_ABGR8888,
            SDL_TextureAccess.SDL_TEXTUREACCESS_STATIC, Math.Max(width, 1), Math.Max(height, 1));
        if (texture == null)
            throw new InvalidOperationException($"SDL_CreateTexture: {SDL_GetError()}");
        SDL_SetTextureBlendMode(texture, SDL_BlendMode.SDL_BLENDMODE_BLEND);
        SDL_SetTextureScaleMode(texture, SDL_ScaleMode.SDL_SCALEMODE_NEAREST);
        if (width > 0 && height > 0)
            fixed (uint* p = pixels)
                SDL_UpdateTexture(texture, null, (IntPtr)p, width * 4);
        return new Texture(texture, width, height);
    }

    /// <summary>
    /// Reads the output size and centers the frame at the largest whole-number scale that fits, never below 1×.
    /// A window smaller than 800×600 crops the frame instead of shrinking it, so sprites never deform.
    /// </summary>
    public void BeginFrame()
    {
        int width, height;
        if (SDL_GetCurrentRenderOutputSize(_renderer, &width, &height))
        {
            _layout = FrameLayout.For(width, height);
        }
        SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        SDL_RenderClear(_renderer);
    }

    public void Draw(ITexture texture, int x, int y)
    {
        var t = (Texture)texture;
        if (t.Width == 0 || t.Height == 0)
            return;
        var dst = new SDL_FRect
        {
            x = _layout.OffsetX + x * _layout.Scale,
            y = _layout.OffsetY + y * _layout.Scale,
            w = t.Width * _layout.Scale,
            h = t.Height * _layout.Scale,
        };
        SDL_RenderTexture(_renderer, t.Handle, null, &dst);
    }

    public void EndFrame() => SDL_RenderPresent(_renderer);

    public uint[] Capture()
    {
        var area = new SDL_Rect { x = _layout.OffsetX, y = _layout.OffsetY, w = IRenderer.FrameWidth * _layout.Scale, h = IRenderer.FrameHeight * _layout.Scale };
        var surface = SDL_RenderReadPixels(_renderer, &area);
        if (surface == null)
            throw new InvalidOperationException($"SDL_RenderReadPixels: {SDL_GetError()}");
        try
        {
            var rgba = SDL_ConvertSurface(surface, SDL_PixelFormat.SDL_PIXELFORMAT_ABGR8888);
            if (rgba == null)
                throw new InvalidOperationException($"SDL_ConvertSurface: {SDL_GetError()}");
            try
            {
                // One sample per frame pixel, whatever the current scale.
                var pixels = new uint[IRenderer.FrameWidth * IRenderer.FrameHeight];
                for (int y = 0; y < IRenderer.FrameHeight && y * _layout.Scale < rgba->h; y++)
                {
                    var row = new ReadOnlySpan<uint>((byte*)rgba->pixels + y * _layout.Scale * rgba->pitch, rgba->w);
                    for (int x = 0; x < IRenderer.FrameWidth && x * _layout.Scale < rgba->w; x++)
                        pixels[y * IRenderer.FrameWidth + x] = row[x * _layout.Scale];
                }
                return pixels;
            }
            finally
            {
                SDL_DestroySurface(rgba);
            }
        }
        finally
        {
            SDL_DestroySurface(surface);
        }
    }

    /// <summary>Window coordinates (from mouse events) to frame coordinates, with the layout of the last frame.</summary>
    internal (int X, int Y) ToFrame(float windowX, float windowY)
    {
        float px, py;
        if (!SDL_RenderCoordinatesFromWindow(_renderer, windowX, windowY, &px, &py))
            return (-1, -1);
        return _layout.ToFrame(px, py);
    }

    public void Dispose() => SDL_DestroyRenderer(_renderer);

    private sealed class Texture(SDL_Texture* handle, int width, int height) : ITexture
    {
        public SDL_Texture* Handle { get; private set; } = handle;
        public int Width { get; } = width;
        public int Height { get; } = height;

        public void Dispose()
        {
            if (Handle != null)
                SDL_DestroyTexture(Handle);
            Handle = null;
        }
    }
}
