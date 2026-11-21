namespace DRPGServer.Game.Entities;

public enum Direction : ushort
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

public enum Rank : byte
{
    GoldDigizoid = 1,
    BlackDigizoid = 2,
    BlueDigizoid = 3,
    RedDigizoid = 4,
    BrownDigizoid = 5,
    ChromeDigizoid = 6,
}

public enum StatKind : byte
{
    Str = 1,
    Agi = 2,
    Con = 3,
    Int = 4,
}
