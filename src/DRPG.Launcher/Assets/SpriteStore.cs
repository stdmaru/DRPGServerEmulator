using DRPG.Launcher.Rendering;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Formats.Sprite;

namespace DRPG.Launcher.Assets;

/// <summary>The official install the launcher sits in: found next to the executable or given with <c>--game-dir</c>.</summary>
public sealed class GameInstall
{
    private GameInstall(string directory) => Directory = directory;

    public string Directory { get; }
    public string ResourcePath => Path.Combine(Directory, "resource.hf");
    public string VersionInfoPath => Path.Combine(Directory, "VersionInfo.ini");

    public static GameInstall? Find(string? explicitDirectory)
    {
        foreach (var dir in new[] { explicitDirectory, AppContext.BaseDirectory, Environment.CurrentDirectory })
            if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "resource.hf")) && File.Exists(Path.Combine(dir, "resource.pf")))
                return new GameInstall(Path.GetFullPath(dir));
        return null;
    }
}

/// <summary>Uploads sprites from <c>resource.hf/.pf</c> as textures, one per frame, read 1:1 and cached by name.</summary>
public sealed class SpriteStore(PackArchive pack, IRenderer renderer) : IDisposable
{
    private readonly Dictionary<string, ITexture[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All frames of <c>gui\&lt;name&gt;.spr</c>; prefers the <c>_eng</c> variant when it exists.</summary>
    public ITexture[] Gui(string name)
    {
        var english = $"gui\\{name}_eng.spr";
        return Load(pack.Contains(english) ? english : $"gui\\{name}.spr");
    }

    public ITexture[] Load(string path)
    {
        if (_cache.TryGetValue(path, out var frames))
            return frames;
        var sprite = SpriteFile.Parse(pack.ReadEntry(path));
        frames = new ITexture[sprite.Frames.Count];
        for (int i = 0; i < frames.Length; i++)
        {
            var info = sprite.Frames[i];
            frames[i] = renderer.CreateTexture(info.Width, info.Height, sprite.Decode(sprite.Image, i));
        }
        _cache[path] = frames;
        return frames;
    }

    public void Dispose()
    {
        foreach (var frames in _cache.Values)
            foreach (var t in frames)
                t.Dispose();
        _cache.Clear();
    }
}
