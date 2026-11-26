using DRPG.Client.Ui;
using DRPG.Engine.Assets;
using DRPG.Engine.Rendering;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Formats.Sprite;

namespace DRPG.Client.Data;

/// <summary>Opens the <c>.ani</c> + <c>.spr</c> pairs of the pack for the scenes. A missing or broken pair is null, never an error.</summary>
public sealed class AnimatedSprites(PackArchive resource, SpriteStore sprites, Action<string> log)
{
    /// <summary>✅ <c>0x00b46120</c>: the file used when a species has no animation of its own.</summary>
    public const string FallbackDigimon = "Sprite\\065_1.ani";

    /// <summary>✅ <c>0x00b46120</c>: the sprite id of this one is a tamer body, not a digimon.</summary>
    public const int TamerSpriteId = 610;
    public const string TamerBody = "Sprite\\tamer_09.ani";

    public SpriteAnimator? Open(string aniPath)
    {
        if (!resource.Contains(aniPath))
            return null;
        try
        {
            var ani = AniFile.Parse(resource.ReadEntry(aniPath));
            var folder = aniPath[..(aniPath.LastIndexOf('\\') + 1)];
            return new SpriteAnimator(ani, sprites.Load(folder + ani.SpriteName + ".spr"));
        }
        catch (Exception e) when (e is AniFormatException or SpriteFormatException or PackFormatException or InvalidDataException)
        {
            log($"{aniPath}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// ✅ The digimon of a species (<c>0x00b23d20</c> and <c>0x00b46120</c>): <c>Sprite\%03d_1.ani</c> of the table's sprite
    /// id, <c>tamer_09</c> for id 610, and <c>065_1</c> when the file does not exist. ❓ The case of 817 (<c>+0x80 == 2</c>) is not handled.
    /// </summary>
    public SpriteAnimator? OpenDigimon(int species)
    {
        int id = SpeciesTables.SpriteId(species);
        var path = id == TamerSpriteId ? TamerBody : $"Sprite\\{id:000}_1.ani";
        return Open(path) ?? Open(FallbackDigimon);
    }

    /// <summary>
    /// ✅ The default clothes of a tamer (<c>0x00b21cd0</c> loads them for tamers 1–4, <c>0x0099fb90</c> draws them),
    /// in the order they are drawn over the body: <c>a_15</c> (<c>0x00b1e630</c>), <c>a_16</c> behind (<c>0x00b1e460</c>),
    /// <c>a_17</c> (<c>0x00b1e120</c>), <c>a_16</c> again in front, <c>a_18</c> (<c>0x00b1da70</c>), <c>a_19</c>
    /// (<c>0x00b1e2c0</c>), <c>a_20</c> (<c>0x00b1cf10</c>) and <c>a_21</c> (<c>0x00b1df90</c>). Each is
    /// <c>Sprite\a_NN_%02d_00.spr</c> of the tamer id, with the same frames as the body.
    /// </summary>
    public static readonly int[] TamerLayers = [15, 16, 17, 16, 18, 19, 20, 21];

    /// <summary>
    /// ✅ The tamer body <c>Sprite\a_%02d.ani</c> of <c>tamerId − 1</c> (<c>0x00b21cd0</c>) and its default clothes
    /// (<see cref="TamerLayers"/>, of <c>tamerId</c>). ❓ The 11 equipment ids of the list (always zero in the captures)
    /// replace these through the item tables; not done yet.
    /// </summary>
    public TamerDoll? OpenTamer(int tamerId)
    {
        if (tamerId < 1 || Open($"Sprite\\a_{tamerId - 1:00}.ani") is not { } body)
            return null;
        var layers = new List<ITexture[]>();
        var loaded = new Dictionary<int, ITexture[]?>();
        foreach (int part in TamerLayers)
        {
            if (!loaded.TryGetValue(part, out var frames))
                loaded[part] = frames = OpenSprite($"Sprite\\a_{part}_{tamerId:00}_00.spr");
            if (frames is not null)
                layers.Add(frames);
        }
        return new TamerDoll(body, layers);
    }

    private ITexture[]? OpenSprite(string path)
    {
        if (!resource.Contains(path))
        {
            log($"{path} not found");
            return null;
        }
        try
        {
            return sprites.Load(path);
        }
        catch (Exception e) when (e is SpriteFormatException or PackFormatException or InvalidDataException)
        {
            log($"{path}: {e.Message}");
            return null;
        }
    }
}

/// <summary>A tamer as the character selection draws it: the animated body and the clothes over it, all by the body's frame.</summary>
public sealed class TamerDoll(SpriteAnimator body, IReadOnlyList<ITexture[]> layers)
{
    public SpriteAnimator Body { get; } = body;

    public IReadOnlyList<ITexture[]> Layers { get; } = layers;

    /// <summary>✅ <c>0x0099fb90</c>: every layer by the top-left corner of the frame at the same point; a layer without that frame is skipped.</summary>
    public void Draw(IRenderer renderer, int x, int y)
    {
        if (Body.Texture is { } frame)
            renderer.Draw(frame, x, y);
        foreach (var layer in Layers)
            if (Body.Frame >= 0 && Body.Frame < layer.Length)
                renderer.Draw(layer[Body.Frame], x, y);
    }
}
