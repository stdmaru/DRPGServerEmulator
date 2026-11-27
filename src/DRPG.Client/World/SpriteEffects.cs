namespace DRPG.Client.World;

/// <summary>
/// The blended layers drawn with a character's sprite, by map (✅ docs/cliente/cena-jogo.md#tamer-e-digimon-no-campo): the shadow
/// (<c>0x00b57460</c>, option <c>shadow</c>) and the soft edge (<c>0x00b57330</c>, option <c>anti</c>), each the matching layer of the same
/// frame at the same corner as the sprite, before it. Alphas are of 32 (<see cref="DRPG.Engine.Rendering.IRenderer.OpaqueAlpha"/>).
/// </summary>
public static class SpriteEffects
{
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
