using DRPG.Client.Data;
using DRPG.Client.Ui;
using DRPG.Client.World;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Features.Field;

/// <summary>
/// Draws <see cref="FieldState"/> (✅ <c>0x00b4b550</c>): the camera on the tamer, the ground and the ground portals, then the
/// y-sorted queue with the map objects, the wilds, the partner, the tamer and the NPCs, then the overlay portals and the warp
/// effect. It changes nothing in the state; the portal swirls are its own animation.
/// </summary>
public sealed class FieldView(IFieldSprites sprites, SpriteOptions options)
{
    private readonly FieldPortals _portals = new(sprites);
    private readonly EntityDrawer _drawer = new(options, mapId: 0);
    private readonly List<DrawItem> _queue = [];

    /// <summary>✅ <c>0x00a121f0</c>: one logic step of the portal swirls.</summary>
    public void Step() => _portals.Step();

    /// <summary>The cell under the screen point (<paramref name="screenX"/>, <paramref name="screenY"/>), or null outside the map.</summary>
    public static Cell? CellAt(FieldMap map, FieldState state, int screenX, int screenY)
    {
        var tamer = state.Party.Tamer;
        var (cameraX, cameraY) = map.Camera(tamer.WorldX, tamer.WorldY);
        var cell = Cell.AtPixel(screenX + cameraX, screenY + cameraY);
        return state.Area.Contains(cell) ? cell : null;
    }

    /// <summary>✅ <c>0x00b4e550</c> lines 604-606: true when <paramref name="cell"/> shows the door cursor.</summary>
    public static bool IsPortal(FieldState state, Cell cell) => FieldPortals.IsPortalCursorType(state.Area.Map[cell.X, cell.Y].Type);

    public void Draw(IRenderer renderer, FieldMap map, FieldState state, SpriteOptions options)
    {
        int mapId = state.Area.MapId;
        var tamer = state.Party.Tamer;
        var (cameraX, cameraY) = map.Camera(tamer.WorldX, tamer.WorldY);
        map.DrawGround(renderer, cameraX, cameraY);
        _portals.DrawGround(renderer, map, cameraX, cameraY, options);

        _drawer.Begin(options, mapId);
        _queue.Clear();
        map.QueueObjects(_queue, cameraX, cameraY, SpriteEffects.ObjectLookOf(mapId, options));
        state.Area.Wilds.QueueDraw(_queue, cameraX, cameraY, _drawer);
        state.Party.QueueDraw(_queue, cameraX, cameraY, _drawer);
        state.Area.Npcs.QueueDraw(_queue, cameraX, cameraY, _drawer);
        FieldMap.Sort(_queue);
        FieldMap.DrawQueue(renderer, _queue);

        _portals.DrawOverlay(renderer, map, cameraX, cameraY, options);
        state.Warp.Draw(renderer, tamer.WorldX - cameraX, tamer.WorldY - cameraY, options);
    }
}
