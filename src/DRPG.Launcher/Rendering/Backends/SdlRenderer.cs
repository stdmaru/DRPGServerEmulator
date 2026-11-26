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

    public SdlRenderer(SDL_Window* window, string? preferredDriver = "gpu")
    {
        _renderer = preferredDriver is null ? null : SDL_CreateRenderer(window, preferredDriver);
        if (_renderer == null)
            _renderer = SDL_CreateRenderer(window, (string?)null);
        if (_renderer == null)
            throw new InvalidOperationException($"SDL_CreateRenderer: {SDL_GetError()}");
        BackendName = SDL_GetRendererName(_renderer) ?? "?";
        SDL_SetRenderVSync(_renderer, 1);
        // FixedScreen: 800×600 centered with black bars, scaled only by whole numbers (HiDPI, fullscreen) so pixels
        // never deform; mouse events converted to frame coordinates.
        SDL_SetRenderLogicalPresentation(_renderer, IRenderer.FrameWidth, IRenderer.FrameHeight,
            SDL_RendererLogicalPresentation.SDL_LOGICAL_PRESENTATION_INTEGER_SCALE);
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

    public void BeginFrame()
    {
        SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        SDL_RenderClear(_renderer);
    }

    public void Draw(ITexture texture, int x, int y)
    {
        var t = (Texture)texture;
        if (t.Width == 0 || t.Height == 0)
            return;
        var dst = new SDL_FRect { x = x, y = y, w = t.Width, h = t.Height };
        SDL_RenderTexture(_renderer, t.Handle, null, &dst);
    }

    public void EndFrame() => SDL_RenderPresent(_renderer);

    public uint[] Capture()
    {
        var surface = SDL_RenderReadPixels(_renderer, null);
        if (surface == null)
            throw new InvalidOperationException($"SDL_RenderReadPixels: {SDL_GetError()}");
        try
        {
            var rgba = SDL_ConvertSurface(surface, SDL_PixelFormat.SDL_PIXELFORMAT_ABGR8888);
            if (rgba == null)
                throw new InvalidOperationException($"SDL_ConvertSurface: {SDL_GetError()}");
            try
            {
                var pixels = new uint[rgba->w * rgba->h];
                for (int y = 0; y < rgba->h; y++)
                    new ReadOnlySpan<uint>((byte*)rgba->pixels + y * rgba->pitch, rgba->w).CopyTo(pixels.AsSpan(y * rgba->w));
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

    internal bool ToFrameCoordinates(SDL_Event* e) => SDL_ConvertEventToRenderCoordinates(_renderer, e);

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
