using DRPG.Client.Data;
using DRPG.Client.Ui;
using DRPG.Engine.Rendering;

namespace DRPG.Client.World;

/// <summary>
/// Queues the entities of the field on the y-sorted draw queue, each kind the one way the original draws it: the <c>.ani</c>
/// sprites (the partner, the wilds, the table NPCs, ✅ <c>0x007e83d0</c>) by the anchor of their frame with the shadow and edge
/// layers under them, and the tamer paper dolls (the player, the avatar NPCs, ✅ <c>0x00862650</c>) by their top-left corner.
/// </summary>
public sealed class EntityDrawer(SpriteOptions options, int mapId)
{
    /// <summary>✅ <c>0x008669e0</c>: the species whose sprite is drawn 32 px lower (<c>0x149</c>, <c>0x3e9</c>).</summary>
    private static readonly HashSet<int> LowerSpecies = [0x149, 0x3e9];

    private const int LowerOffset = 32;

    /// <summary>✅ The paper doll of the character selection is drawn by its top-left corner at (x − 120, y − 162), no anchor.</summary>
    private const int TamerOffsetX = 120, TamerOffsetY = 162;

    private SpriteOptions _options = options;
    private int _mapId = mapId;

    /// <summary>The options and the map of the frame being drawn (they change in game: the options window, a map change).</summary>
    public void Begin(SpriteOptions options, int mapId)
    {
        _options = options;
        _mapId = mapId;
    }

    /// <summary>Adds the tamer <paramref name="doll"/> at the screen point (<paramref name="x"/>, <paramref name="y"/>) of its entity to the y-sorted queue.</summary>
    public void QueueTamer(List<DrawItem> queue, TamerDoll doll, int x, int y)
    {
        var options = _options;
        bool shadow = SpriteEffects.TamerHasShadow(_mapId);
        queue.Add(new DrawItem(y, null, x - TamerOffsetX, y - TamerOffsetY, (r, drawX, drawY) => doll.Draw(r, drawX, drawY, options, shadow)));
    }

    /// <summary>
    /// Adds <paramref name="sprite"/> at the screen point (<paramref name="x"/>, <paramref name="y"/>) of its entity to the
    /// y-sorted queue. <paramref name="species"/> is 0 for a sprite that is not a digimon.
    /// </summary>
    public void QueueSprite(List<DrawItem> queue, SpriteAnimator sprite, int species, int x, int y)
    {
        if (sprite.Texture is not { } frame)
            return;
        var (anchorX, anchorY) = sprite.Anchor;
        int lower = LowerSpecies.Contains(species) ? LowerOffset : 0;
        queue.Add(new DrawItem(y, frame, x - anchorX, y - anchorY + lower, (r, drawX, drawY) => Draw(r, sprite, drawX, drawY)));
    }

    /// <summary>
    /// ✅ <c>0x007e83d0</c>: with the <c>shadow</c> option the frame's shadow layer, then with the <c>anti</c> option (outside the
    /// animations 9/10) its edge layer, both blended at the same corner as the sprite (<see cref="SpriteEffects"/>), then the sprite.
    /// </summary>
    private void Draw(IRenderer renderer, SpriteAnimator sprite, int x, int y)
    {
        if (_options.Shadow && sprite.ShadowTexture is { } shadow)
            _options.DrawBlended(renderer, shadow, x, y, SpriteEffects.DigimonShadowAlpha(_mapId));
        if (_options.Edge && SpriteEffects.DigimonHasEdge(sprite.Animation) && sprite.EdgeTexture is { } edge)
            _options.DrawBlended(renderer, edge, x, y, SpriteEffects.DigimonEdgeAlpha(_mapId));
        if (sprite.Texture is { } frame)
            renderer.Draw(frame, x, y);
    }
}
