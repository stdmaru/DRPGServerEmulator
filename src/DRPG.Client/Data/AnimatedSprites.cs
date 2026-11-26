using DRPG.Client.Ui;
using DRPG.Engine.Assets;
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
    /// ✅ The tamer body <c>Sprite\a_%02d.ani</c>. ❓ The index is <c>tamerId − 1</c>: the list marks "no tamer" with 0
    /// and the install has only <c>a_00..a_03</c>, for the four tamers.
    /// </summary>
    public SpriteAnimator? OpenTamer(int tamerId) => tamerId < 1 ? null : Open($"Sprite\\a_{tamerId - 1:00}.ani");
}
