using DRPG.Client.Scenes;
using DRPG.Engine.Rendering;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Formats.Sprite;

namespace DRPG.Client.Ui;

/// <summary>
/// The sprites of the field's UI by path, loaded once. A file the pack lacks or a damaged one is no frames, logged once: the original
/// draws nothing for it (<c>0x00b56ff0</c> with an empty sprite).
/// </summary>
public sealed class UiSprites(GameContext game)
{
    private readonly Dictionary<string, ITexture[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A sprite by its pack path (<c>interface\w_unitframe.spr</c>).</summary>
    public ITexture[] Load(string path) => Get(path, gui: false);

    /// <summary>✅ A <c>GUI\%s.spr</c> sprite by name, the <c>_eng</c> one when the pack has it (<c>SpriteStore.Gui</c>).</summary>
    public ITexture[] Gui(string name) => Get($"gui\\{name}.spr", gui: true);

    private ITexture[] Get(string path, bool gui)
    {
        if (_cache.TryGetValue(path, out var frames))
            return frames;
        try
        {
            if (gui)
            {
                var name = path["gui\\".Length..^".spr".Length];
                frames = game.Resource.Contains(path) || game.Resource.Contains($"gui\\{name}_eng.spr") ? game.Sprites.Gui(name) : [];
            }
            else
            {
                frames = game.Resource.Contains(path) ? game.Sprites.Load(path) : [];
            }
        }
        catch (Exception e) when (e is SpriteFormatException or PackFormatException or InvalidDataException)
        {
            game.Log($"{path}: {e.Message}");
            frames = [];
        }
        _cache[path] = frames;
        return frames;
    }
}
