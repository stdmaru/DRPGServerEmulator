using DRPG.Shared.Formats.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.World;

/// <summary>
/// Field collision and entity occupancy grid (✅ v322 <c>0x00b35f80</c>, <c>0x00b35f40</c>, <c>0x00b35b30</c>,
/// <c>0x00b36550</c>, <c>0x00b365c0</c>, <c>0x00b432d0</c>, docs/cliente/campo.md).
/// Static walkability comes strictly from byte <c>+0x14</c> of each map cell (object footprints are pre-baked in the
/// <c>.map</c>). Dynamic occupancy tracks a 16-byte <see cref="Serial"/> per cell, covering <c>(x, y)</c> and
/// <c>(x, y − 1)</c> when unoccupied.
/// </summary>
public sealed class FieldCollision
{
    private readonly MapFile _map;
    private readonly Serial[] _occupied;

    public FieldCollision(MapFile map)
    {
        _map = map;
        _occupied = new Serial[map.Width * map.Height];
    }

    public int Width => _map.Width;

    public int Height => _map.Height;

    /// <summary>✅ <c>0x00b35f80</c>: true when (<paramref name="x"/>, <paramref name="y"/>) lies inside the map.</summary>
    public bool InBounds(int x, int y) => (uint)x < (uint)_map.Width && (uint)y < (uint)_map.Height;

    /// <summary>✅ <c>0x00b35f40</c>: true when the cell is in bounds and byte <c>+0x14</c> is non-zero.</summary>
    public bool IsWalkable(int x, int y) => InBounds(x, y) && (_map[x, y].Flags & 0xFF) != 0;

    /// <summary>✅ <c>0x00b35e10</c>: the serial occupying (<paramref name="x"/>, <paramref name="y"/>), or <c>default</c> when empty/out of bounds.</summary>
    public Serial OccupantAt(int x, int y) => InBounds(x, y) ? _occupied[y * _map.Width + x] : default;

    /// <summary>True when (<paramref name="x"/>, <paramref name="y"/>) holds a non-zero entity serial.</summary>
    public bool IsOccupied(int x, int y) => OccupantAt(x, y) != default;

    /// <summary>✅ <c>0x00b35b30</c>: true when the cell is statically walkable AND has no occupant serial.</summary>
    public bool IsFree(int x, int y) => IsWalkable(x, y) && !IsOccupied(x, y);

    /// <summary>
    /// ✅ <c>0x00b432d0</c>: pathfinder passability check. Intermediate cells must be statically walkable
    /// (<see cref="IsWalkable"/>); the target cell itself is always passable so the entity can walk up to an obstacle or NPC.
    /// </summary>
    public bool CanPathThrough(int x, int y, int destX, int destY) =>
        (x == destX && y == destY) || IsWalkable(x, y);

    /// <summary>
    /// ✅ <c>0x00b4d290</c> / <c>0x008671c0</c> / <c>0x007f4b10</c>: true if an entity may actually step onto
    /// (<paramref name="nextX"/>, <paramref name="nextY"/>). When that cell is the final destination, it must be free
    /// (<see cref="IsFree"/>); otherwise it must be statically walkable (<see cref="IsWalkable"/>).
    /// </summary>
    public bool CanStepTo(int nextX, int nextY, int destX, int destY)
    {
        if (nextX == destX && nextY == destY)
            return IsFree(nextX, nextY);
        return IsWalkable(nextX, nextY);
    }

    /// <summary>
    /// ✅ <c>0x00b36550</c>: registers <paramref name="serial"/> on (<paramref name="x"/>, <paramref name="y"/>) and also
    /// on (<paramref name="x"/>, <paramref name="y"/> − 1) when that upper cell is in bounds and currently empty.
    /// </summary>
    public void Occupy(int x, int y, Serial serial)
    {
        if (!InBounds(x, y) || serial == default)
            return;
        _occupied[y * _map.Width + x] = serial;
        if (InBounds(x, y - 1) && _occupied[(y - 1) * _map.Width + x] == default)
            _occupied[(y - 1) * _map.Width + x] = serial;
    }

    /// <summary>
    /// ✅ <c>0x00b365c0</c>: clears (<paramref name="x"/>, <paramref name="y"/>) and clears (<paramref name="x"/>,
    /// <paramref name="y"/> − 1) when it holds the same serial.
    /// </summary>
    public void Vacate(int x, int y)
    {
        if (!InBounds(x, y))
            return;
        int index = y * _map.Width + x;
        var serial = _occupied[index];
        if (InBounds(x, y - 1) && _occupied[(y - 1) * _map.Width + x] == serial)
            _occupied[(y - 1) * _map.Width + x] = default;
        _occupied[index] = default;
    }

    /// <summary>
    /// ✅ <c>0x0085e940</c>: an entity's step on the grid: the old cell is freed when it changed, and the current cell is occupied
    /// again (every step, like the cell update of the original).
    /// </summary>
    public void Move(Cell from, Cell to, Serial serial)
    {
        if (from != to)
            Vacate(from.X, from.Y);
        Occupy(to.X, to.Y, serial);
    }
}
