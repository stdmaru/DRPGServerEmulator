using DRPG.Client.Data;
using DRPG.Client.Startup;
using DRPG.Shared.Formats.Map;

namespace DRPG.Client.World;

/// <summary>
/// One loaded map and what lives on it (✅ <c>0x00ad3730</c>): its collision and occupancy grid, the pathfinder on that grid, the
/// server's wild groups and the NPCs. A map change builds a new area; nothing of the old one is kept.
/// </summary>
public sealed class FieldArea
{
    public FieldArea(int mapId, MapFile map, NpcTable npcs, IFieldSprites sprites, Random random)
    {
        MapId = mapId;
        Map = map;
        Collision = new FieldCollision(map);
        Pathfinder = new FieldPathfinder(Collision);
        Wilds = new FieldWilds(Collision, Pathfinder, sprites, random);
        Npcs = new FieldNpcs(mapId, npcs, Collision, sprites, random);
    }

    public int MapId { get; }
    public MapFile Map { get; }
    public FieldCollision Collision { get; }
    public FieldPathfinder Pathfinder { get; }
    public FieldWilds Wilds { get; }
    public FieldNpcs Npcs { get; }

    /// <summary>True when <paramref name="cell"/> is inside the map.</summary>
    public bool Contains(Cell cell) => Collision.InBounds(cell.X, cell.Y);
}
