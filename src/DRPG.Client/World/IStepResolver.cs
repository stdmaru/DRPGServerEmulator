namespace DRPG.Client.World;

/// <summary>
/// ✅ <c>0x007f4b10</c> / <c>0x008671c0</c>: one step toward a destination. When <see cref="BlockedAtTarget"/> is true, the next
/// cell IS the destination and is not free (<see cref="FieldCollision.IsFree"/>), so the entity stops (<c>Dest = Cell</c>) while
/// still turning to face <see cref="Next"/>.
/// </summary>
public readonly record struct PathStep(Cell Next, bool BlockedAtTarget);

/// <summary>Picks the next adjacent cell of an entity's walk: the map's pathfinder (<see cref="FieldPathfinder"/>).</summary>
public interface IStepResolver
{
    /// <summary>The next step from <paramref name="from"/> toward <paramref name="destination"/>, or null when there is none.</summary>
    PathStep? ResolveStep(Cell from, Cell destination);
}
