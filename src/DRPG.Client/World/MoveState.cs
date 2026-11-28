using DRPG.Shared.Protocol;

namespace DRPG.Client.World;

/// <summary>
/// ✅ The movement state of a field entity (<c>+0x149</c>, <c>0x0085e5b0</c>, <c>0x008671c0</c>, <c>0x007f3df0</c>). The
/// <c>0x11</c> state byte is this value; ❓ its 8 (defeated) and 11 (in battle) are not movement states and are not read by
/// the command queue (<c>0x00859340</c>), so they leave the entity as it is.
/// </summary>
public enum MoveState : byte
{
    Standing = 1,
    IdleA = 2,
    IdleB = 3,
    Walk = 4,
    Run = 5,
    Fast = 0x0f,
}

/// <summary>
/// ✅ One entry of an entity's command queue (<c>0x007f3b30</c>, <c>+0x154..+0x160</c>, 0x18 B).
/// <paramref name="Direction"/> is null when the command carries none (an out-of-range value from the server).
/// </summary>
public readonly record struct MoveCommand(MoveState State, Cell From, Cell Destination, Direction? Direction);

/// <summary>✅ <c>0x00859340</c>: how a standing command is read, by the entity type at <c>+5</c>.</summary>
public enum CommandRule
{
    /// <summary><c>+5 == 4</c>, the partner digimon: only a mismatch of more than 1 cell with the command's origin runs to its destination.</summary>
    Partner,

    /// <summary><c>+5 == 5</c>, a wild digimon: any mismatch with the command's origin runs to its destination.</summary>
    Wild,
}
