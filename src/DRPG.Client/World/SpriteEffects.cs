namespace DRPG.Client.World;

/// <summary>
/// The blended layers drawn with a character's sprite, by map (✅ docs/cliente/cena-jogo.md#tamer-e-digimon-no-campo): the shadow
/// (<c>0x00b57460</c>, option <c>shadow</c>) and the soft edge (<c>0x00b57330</c>, option <c>anti</c>), each the matching layer of the same
/// frame at the same corner as the sprite, before it. Alphas are of 32 (<see cref="DRPG.Engine.Rendering.IRenderer.OpaqueAlpha"/>).
/// </summary>
public static class SpriteEffects
{
    /// <summary>✅ The map <c>'h'</c> (<c>0x68</c>): its objects have no shadow and their image is blended (<c>0x00a0d340</c>, case 7).</summary>
    public const int BlendedObjectsMap = 0x68;

    /// <summary>✅ <c>0x00b413b0(…, −1, 0x14)</c>: the alpha of the objects' image on <see cref="BlendedObjectsMap"/>.</summary>
    public const int BlendedObjectAlpha = 0x14;

    /// <summary>✅ <c>0x00b414d0(…, 0xa)</c>: the alpha of the objects' shadow on the maps of <see cref="UsesOtherModes"/>.</summary>
    public const int OtherModesObjectShadowAlpha = 0xa;

    /// <summary>
    /// ✅ How the queue draws a map object (<c>0x00a0d340</c>, case 7): with the <c>shadow</c> option and outside
    /// <see cref="BlendedObjectsMap"/>, first its shadow — plain without the <c>alpha</c> option (<c>0x00b41410(…, 0)</c>), mode 7 on the
    /// normal maps (<c>0x00b41410(…, 0x14)</c> → <c>0x00875a00(…, 7, …)</c>: the frame halved under it), blended at 10/32 on the
    /// other-mode maps (<c>0x00b414d0</c>); then its image, plain (<c>0x00b41370</c>), or blended at 20/32 on <see cref="BlendedObjectsMap"/>.
    /// </summary>
    public static ObjectLook ObjectLookOf(int mapId, SpriteOptions options)
    {
        int imageAlpha = mapId == BlendedObjectsMap ? BlendedObjectAlpha : DRPG.Engine.Rendering.IRenderer.OpaqueAlpha;
        if (!options.Shadow || mapId == BlendedObjectsMap)
            return new ObjectLook(ObjectShadow.None, imageAlpha);
        if (!options.Alpha)
            return new ObjectLook(ObjectShadow.Plain, imageAlpha);
        if (UsesOtherModes(mapId))
            return new ObjectLook(ObjectShadow.Blended, imageAlpha);
        return new ObjectLook(ObjectShadow.Halved, imageAlpha);
    }

    /// <summary>✅ <c>0x00b57460(…, 0xe)</c> and <c>0x00b57330</c>: the alpha of the shadow and of the edge on a normal map.</summary>
    public const int NormalAlpha = 0xe;

    /// <summary>✅ The digimon's shadow on the maps of <see cref="UsesOtherModes"/>.</summary>
    public const int OtherModesShadowAlpha = 4;

    /// <summary>✅ The digimon's edge on the maps of <see cref="UsesOtherModes"/>.</summary>
    public const int OtherModesEdgeAlpha = 6;

    /// <summary>✅ The maps whose sprites are drawn in other modes (<c>0x007e83d0</c>, <c>0x00b57330</c>, <c>0x0099fb90</c>).</summary>
    public static bool UsesOtherModes(int mapId) => mapId switch
    {
        0x61 or 0x66 or 0x67 or 0x6d => true,
        _ => false,
    };

    /// <summary>
    /// ✅ <c>0x007e83d0</c>: the digimon's shadow alpha, 4 on <c>0x6d</c> and on the other-mode maps. (The species <c>0x34</c>, <c>0x291</c>
    /// and <c>0x94</c> lose it in the animations 9/10, which the field does not play yet.)
    /// </summary>
    public static int DigimonShadowAlpha(int mapId) => UsesOtherModes(mapId) ? OtherModesShadowAlpha : NormalAlpha;

    /// <summary>✅ <c>0x007e83d0</c>: the digimon's edge is not drawn in the animations 9 and 10 (❓ their meaning in the <c>.ani</c>).</summary>
    public static bool DigimonHasEdge(int animation) => animation switch
    {
        9 or 10 => false,
        _ => true,
    };

    /// <summary>✅ <c>0x00b57330</c>: the digimon's edge alpha, 6 on the other-mode maps.</summary>
    public static int DigimonEdgeAlpha(int mapId) => UsesOtherModes(mapId) ? OtherModesEdgeAlpha : NormalAlpha;

    /// <summary>
    /// ✅ <c>0x0099fb90</c> passes a mode to <c>0x00b1d0f0</c>, which draws the body's shadow only in mode 0: mode 1 on the other-mode maps,
    /// 2 on <c>0x50</c>–<c>0x54</c> and <c>0x59</c>.
    /// </summary>
    public static bool TamerHasShadow(int mapId) => mapId switch
    {
        0x61 or 0x66 or 0x67 or 0x6d => false,
        >= 0x50 and <= 0x54 => false,
        0x59 => false,
        _ => true,
    };
}

/// <summary>How a map object's shadow is drawn (<see cref="SpriteEffects.ObjectLookOf"/>).</summary>
public enum ObjectShadow
{
    None,
    Plain,

    /// <summary>The frame under it halved (mode 7).</summary>
    Halved,

    /// <summary>Mixed at <see cref="SpriteEffects.OtherModesObjectShadowAlpha"/>/32.</summary>
    Blended,
}

/// <summary>The shadow of a map object and the alpha (of 32) of its image.</summary>
public readonly record struct ObjectLook(ObjectShadow Shadow, int ImageAlpha);
