using DRPG.Engine.Rendering;

namespace DRPG.Client;

/// <summary>
/// ✅ The <c>[SPRITE]</c> section of <c>option.ini</c> (<c>0x00b42be0</c>, the object <c>DAT_01158530</c>): <c>anti</c> → <c>+0</c>,
/// <c>alpha</c> → <c>+1</c>, <c>shadow</c> → <c>+2</c>, <c>card</c> → <c>+3</c>; each missing key is 1, as in the original. Only read:
/// the client never writes the install (the options window <c>0x00b16980</c> that toggles them is not made yet).
/// </summary>
public sealed record SpriteOptions(bool Edge, bool Alpha, bool Shadow, bool Card)
{
    /// <summary>✅ <c>0x00b42be0</c>: <c>GetPrivateProfileInt(…, 1)</c> for the four keys.</summary>
    public static readonly SpriteOptions Default = new(Edge: true, Alpha: true, Shadow: true, Card: true);

    /// <summary>
    /// ✅ The mode −1 of <c>0x00b569f0</c>/<c>0x00b57460</c>/<c>0x00b57330</c>: with <see cref="Alpha"/> the frame is mixed at
    /// <paramref name="alpha"/>/32 (<c>0x00875a00</c>); without it, drawn plain (<c>0x008754f0</c>).
    /// </summary>
    public void DrawBlended(IRenderer renderer, ITexture texture, int x, int y, int alpha) =>
        DrawRegionBlended(renderer, texture, x, y, texture.Width, texture.Height, alpha);

    /// <summary><see cref="DrawBlended"/> of the top-left <paramref name="width"/> × <paramref name="height"/> of the frame.</summary>
    public void DrawRegionBlended(IRenderer renderer, ITexture texture, int x, int y, int width, int height, int alpha)
    {
        if (Alpha)
            renderer.DrawRegionBlended(texture, x, y, 0, 0, width, height, alpha);
        else
            renderer.DrawRegion(texture, x, y, 0, 0, width, height);
    }

    /// <summary>From the values of <c>IniFile</c> (keys <c>section.key</c>).</summary>
    public static SpriteOptions From(IReadOnlyDictionary<string, string> ini) => new(
        Flag(ini, "sprite.anti"), Flag(ini, "sprite.alpha"), Flag(ini, "sprite.shadow"), Flag(ini, "sprite.card"));

    /// <summary>✅ The options are bytes: only the low byte counts (256 is off).</summary>
    private static bool Flag(IReadOnlyDictionary<string, string> ini, string key) => IniProfile.ReadByte(ini, key, 1) != 0;
}
