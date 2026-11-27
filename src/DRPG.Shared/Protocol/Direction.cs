namespace DRPG.Shared.Protocol;

/// <summary>
/// ✅ The 8 facing directions of the field, as the wire and the client use them (v322 <c>0x007f3880</c>, <c>0x0085e5b0</c>;
/// the <c>u16</c> direction of <c>0x11</c>): 0 = south, then clockwise.
/// </summary>
public enum Direction : byte
{
    South = 0,
    SouthWest = 1,
    West = 2,
    NorthWest = 3,
    North = 4,
    NorthEast = 5,
    East = 6,
    SouthEast = 7,
}

/// <summary>Checks a direction that came from outside (a packet or a table) before it is used.</summary>
public static class Directions
{
    /// <summary>Number of directions (<see cref="Direction.South"/> .. <see cref="Direction.SouthEast"/>).</summary>
    public const int Count = 8;

    /// <summary>True when <paramref name="value"/> is one of the 8 directions; <paramref name="direction"/> is then that direction.</summary>
    public static bool TryFrom(int value, out Direction direction)
    {
        direction = (Direction)value;
        return value is >= 0 and < Count;
    }
}
