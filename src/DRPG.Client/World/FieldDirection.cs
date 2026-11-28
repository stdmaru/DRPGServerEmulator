using DRPG.Shared.Protocol;

namespace DRPG.Client.World;

/// <summary>The pixels an entity moves in one logic step before the state's multiplier.</summary>
public readonly record struct PixelStep(int Dx, int Dy);

/// <summary>✅ The 8-way direction math of the field entities (<c>0x007f3880</c>, <c>0x0085e5b0</c>, <c>0x008671c0</c>).</summary>
public static class FieldDirection
{
    /// <summary>✅ <c>0x007f3880</c>: the direction from <paramref name="from"/> to <paramref name="to"/>, or null when they are the same cell.</summary>
    public static Direction? FromTo(Cell from, Cell to)
    {
        if (to.X < from.X)
        {
            if (to.Y < from.Y)
                return Direction.NorthWest;
            return to.Y > from.Y ? Direction.SouthWest : Direction.West;
        }
        if (to.X == from.X)
        {
            if (to.Y < from.Y)
                return Direction.North;
            return to.Y > from.Y ? Direction.South : null;
        }
        if (to.Y < from.Y)
            return Direction.NorthEast;
        return to.Y > from.Y ? Direction.SouthEast : Direction.East;
    }

    /// <summary>✅ <c>0x0085e5b0</c>: the base pixel step per logic step before the state multiplier (±4 in X, ±2 in Y).</summary>
    public static PixelStep BaseVelocity(Direction direction) => direction switch
    {
        Direction.South => new(0, 2),
        Direction.SouthWest => new(-4, 2),
        Direction.West => new(-4, 0),
        Direction.NorthWest => new(-4, -2),
        Direction.North => new(0, -2),
        Direction.NorthEast => new(4, -2),
        Direction.East => new(4, 0),
        Direction.SouthEast => new(4, 2),
        _ => new(0, 0),
    };

    /// <summary>
    /// ✅ <c>0x008671c0</c>: the cell one step behind <paramref name="destination"/> along <paramref name="direction"/>, where
    /// the partner digimon aims when it follows the tamer.
    /// </summary>
    public static Cell CellBehind(Cell destination, Direction direction) => direction switch
    {
        Direction.South => new(destination.X, destination.Y - 1),
        Direction.SouthWest => new(destination.X + 1, destination.Y - 1),
        Direction.West => new(destination.X + 1, destination.Y),
        Direction.NorthWest => new(destination.X + 1, destination.Y + 1),
        Direction.North => new(destination.X, destination.Y + 1),
        Direction.NorthEast => new(destination.X - 1, destination.Y + 1),
        Direction.East => new(destination.X - 1, destination.Y),
        Direction.SouthEast => new(destination.X - 1, destination.Y - 1),
        _ => destination,
    };
}
