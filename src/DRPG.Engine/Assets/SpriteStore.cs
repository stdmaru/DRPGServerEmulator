using DRPG.Engine.Rendering;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Formats.Sprite;

namespace DRPG.Engine.Assets;

/// <summary>Uploads sprites from <c>resource.hf/.pf</c> as textures, one per frame, read 1:1 and cached by name.</summary>
public sealed class SpriteStore(PackArchive pack, IRenderer renderer) : IDisposable
{
    private readonly Dictionary<string, ITexture[]> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpriteFrames> _lazy = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, SpriteFile>? _preloaded;

    /// <summary>
    /// Sprites already read and parsed (the start-up load): <see cref="Load(string)"/> uses them instead of reading
    /// the pack again. Keys are pack paths, compared ignoring case.
    /// </summary>
    public void Adopt(IReadOnlyDictionary<string, SpriteFile> preloaded) => _preloaded = preloaded;

    /// <summary>All frames of <c>gui\&lt;name&gt;.spr</c>; prefers the <c>_eng</c> variant when it exists.</summary>
    public ITexture[] Gui(string name) => Load(GuiPath(name), disabled: false);

    /// <summary>Grey copy of <see cref="Gui"/>, for disabled controls (the sprites have no disabled frame).</summary>
    public ITexture[] GuiDisabled(string name) => Load(GuiPath(name), disabled: true);

    /// <summary>
    /// Grey copy of only some areas of frame 0 of <c>gui\&lt;name&gt;</c>, transparent elsewhere: each area is the
    /// pixels of one colour connected to a seed (<see cref="KeepRegions"/>). Drawn over the normal sprite, it greys a
    /// field without losing its rounded shape.
    /// </summary>
    public ITexture GuiDisabledRegions(string name, params (int X, int Y)[] seeds)
    {
        var sprite = SpriteFile.Parse(pack.ReadEntry(GuiPath(name)));
        var info = sprite.Frames[0];
        var pixels = sprite.Decode(sprite.Image, 0);
        KeepRegions(pixels, info.Width, info.Height, seeds);
        Disable(pixels);
        return renderer.CreateTexture(info.Width, info.Height, pixels);
    }

    /// <summary>Per-channel difference still counted as the seed's colour (anti-aliased edges of the field).</summary>
    internal const int RegionTolerance = 24;

    /// <summary>
    /// Flood fill (4-connected) from each seed over pixels within <see cref="RegionTolerance"/> of the seed's colour;
    /// every other pixel becomes transparent. Seeds outside the image or on transparent pixels select nothing.
    /// </summary>
    internal static void KeepRegions(Span<uint> rgba, int width, int height, ReadOnlySpan<(int X, int Y)> seeds)
    {
        var keep = new bool[rgba.Length];
        var pending = new Stack<int>();
        foreach (var (sx, sy) in seeds)
        {
            if (sx < 0 || sy < 0 || sx >= width || sy >= height)
                continue;
            uint seed = rgba[sy * width + sx];
            if (seed >> 24 == 0)
                continue;
            pending.Push(sy * width + sx);
            while (pending.Count > 0)
            {
                int i = pending.Pop();
                if (keep[i] || rgba[i] >> 24 == 0 || !Close(rgba[i], seed))
                    continue;
                keep[i] = true;
                int x = i % width, y = i / width;
                if (x > 0) pending.Push(i - 1);
                if (x < width - 1) pending.Push(i + 1);
                if (y > 0) pending.Push(i - width);
                if (y < height - 1) pending.Push(i + width);
            }
        }
        for (int i = 0; i < rgba.Length; i++)
            if (!keep[i])
                rgba[i] = 0;
    }

    private static bool Close(uint a, uint b)
    {
        for (int shift = 0; shift < 24; shift += 8)
            if (Math.Abs((int)((a >> shift) & 0xFF) - (int)((b >> shift) & 0xFF)) > RegionTolerance)
                return false;
        return true;
    }

    private string GuiPath(string name)
    {
        var english = $"gui\\{name}_eng.spr";
        return pack.Contains(english) ? english : $"gui\\{name}.spr";
    }

    public ITexture[] Load(string path) => Load(path, disabled: false);

    /// <summary>
    /// Like <see cref="Load(string)"/>, but each frame is decoded and uploaded only when it is first drawn
    /// (<see cref="SpriteFrames"/>): for the animated sprites, whose many frames are mostly never shown.
    /// </summary>
    public SpriteFrames LoadLazy(string path)
    {
        if (_lazy.TryGetValue(path, out var frames))
            return frames;
        frames = new SpriteFrames(Parse(path), renderer);
        _lazy[path] = frames;
        return frames;
    }

    private SpriteFile Parse(string path) =>
        _preloaded is not null && _preloaded.TryGetValue(PackPath.Normalize(path), out var parsed)
            ? parsed
            : SpriteFile.Parse(pack.ReadEntry(path));

    private ITexture[] Load(string path, bool disabled)
    {
        var key = disabled ? path + "|disabled" : path;
        if (_cache.TryGetValue(key, out var frames))
            return frames;
        var sprite = Parse(path);
        frames = new ITexture[sprite.Frames.Count];
        for (int i = 0; i < frames.Length; i++)
        {
            var info = sprite.Frames[i];
            var pixels = sprite.Decode(sprite.Image, i);
            if (disabled)
                Disable(pixels);
            frames[i] = renderer.CreateTexture(info.Width, info.Height, pixels);
        }
        _cache[key] = frames;
        return frames;
    }

    /// <summary>
    /// Grey and a little darker: luminance (Rec. 601) scaled to 3/4. ❓ The original has no disabled look for this
    /// screen; this is the launcher's own (asked for by the owner).
    /// </summary>
    internal static void Disable(Span<uint> rgba)
    {
        for (int i = 0; i < rgba.Length; i++)
        {
            uint p = rgba[i];
            uint r = p & 0xFF, g = (p >> 8) & 0xFF, b = (p >> 16) & 0xFF;
            uint grey = (r * 299 + g * 587 + b * 114) / 1000 * 3 / 4;
            rgba[i] = (p & 0xFF000000) | (grey << 16) | (grey << 8) | grey;
        }
    }

    public void Dispose()
    {
        foreach (var frames in _cache.Values)
            foreach (var t in frames)
                t.Dispose();
        _cache.Clear();
        foreach (var frames in _lazy.Values)
            frames.Dispose();
        _lazy.Clear();
    }
}
