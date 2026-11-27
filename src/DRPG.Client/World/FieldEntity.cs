using DRPG.Shared.Formats.Map;

namespace DRPG.Client.World;

/// <summary>✅ Entity movement/animation states on the field (<c>+0x149</c>, <c>0x0085e5b0</c>, <c>0x008671c0</c>, <c>0x007f3df0</c>).</summary>
public enum MoveState : byte
{
    Standing = 1,
    IdleA = 2,
    IdleB = 3,
    Walk = 4,
    Run = 5,
    Fast = 0x0f,
}

/// <summary>✅ One entry of an entity's command queue (<c>0x007f3b30</c>, <c>+0x154..+0x160</c>, 0x18 B).</summary>
public readonly record struct MoveCommand(MoveState State, int FromX, int FromY, int DestX, int DestY, int Direction);

/// <summary>
/// ✅ 8-way direction math of the field entities (<c>0x007f3880</c>, <c>0x0085e5b0</c>, <c>0x008671c0</c>):
/// 0 = S, 1 = SW, 2 = W, 3 = NW, 4 = N, 5 = NE, 6 = E, 7 = SE.
/// </summary>
public static class FieldDirection
{
    /// <summary>✅ <c>0x007f3880</c>: direction from (<paramref name="fromX"/>, <paramref name="fromY"/>) to (<paramref name="toX"/>, <paramref name="toY"/>), or −1 when equal.</summary>
    public static int FromTo(int fromX, int fromY, int toX, int toY)
    {
        if (toX < fromX)
        {
            if (toY < fromY) return 3;
            return toY > fromY ? 1 : 2;
        }
        if (toX == fromX)
        {
            if (toY < fromY) return 4;
            return toY > fromY ? 0 : -1;
        }
        if (toY < fromY) return 5;
        return toY > fromY ? 7 : 6;
    }

    /// <summary>✅ <c>0x0085e5b0</c>: base pixel step per frame before the state multiplier (±4 in X, ±2 in Y).</summary>
    public static (int Dx, int Dy) BaseVelocity(int direction) => direction switch
    {
        0 => (0, 2),
        1 => (-4, 2),
        2 => (-4, 0),
        3 => (-4, -2),
        4 => (0, -2),
        5 => (4, -2),
        6 => (4, 0),
        7 => (4, 2),
        _ => (0, 0),
    };

    /// <summary>
    /// ✅ <c>0x008671c0</c>: cell one step behind (<paramref name="destX"/>, <paramref name="destY"/>) along
    /// <paramref name="direction"/>, where the partner digimon aims when following the tamer.
    /// </summary>
    public static (int X, int Y) CellBehind(int destX, int destY, int direction) => direction switch
    {
        0 => (destX, destY - 1),
        1 => (destX + 1, destY - 1),
        2 => (destX + 1, destY),
        3 => (destX + 1, destY + 1),
        4 => (destX, destY + 1),
        5 => (destX - 1, destY + 1),
        6 => (destX - 1, destY),
        7 => (destX - 1, destY - 1),
        _ => (destX, destY),
    };

    /// <summary>Chebyshev distance between two cells (matching the per-axis checks in <c>0x008671c0</c> and <c>0x00859340</c>).</summary>
    public static int Chebyshev(int ax, int ay, int bx, int by) => Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));
}

/// <summary>
/// A moving entity on the field map (✅ v322 base <c>0x0085e1c0</c>, physics <c>0x0085e5b0</c>, cell update <c>0x0085e940</c>,
/// queue <c>0x007f3b30</c> / <c>0x00859340</c>, tamer + partner <c>0x008671c0</c>). Runs at 30 logic steps a second with exact
/// integer pixel steps so it always lands on cell centres (<c>cx · 64 + 32</c>, <c>cy · 32 + 16</c>).
/// </summary>
public sealed class FieldEntity
{
    /// <summary>✅ <c>0x008671c0</c>: partner digimon teleports to (tamer − 1, tamer − 1) when farther than 5 cells.</summary>
    public const int PartnerTeleportDistance = 5;

    /// <summary>✅ <c>0x00859340</c>: a queued move command farther than 6 cells teleports the entity instead of walking.</summary>
    public const int CommandTeleportDistance = 6;

    private readonly Queue<MoveCommand> _commands = new();

    public FieldEntity(int cellX, int cellY, int direction = 0)
    {
        Direction = direction;
        PlaceAt(cellX, cellY);
    }

    public MoveState State { get; private set; } = MoveState.Standing;

    public int Direction { get; private set; }

    public int WorldX { get; private set; }

    public int WorldY { get; private set; }

    public int CellX { get; private set; }

    public int CellY { get; private set; }

    public int DestX { get; private set; }

    public int DestY { get; private set; }

    public bool IsMoving => State is MoveState.Walk or MoveState.Run or MoveState.Fast;

    public int PendingCommands => _commands.Count;

    /// <summary>✅ <c>0x00b35f00</c>: true when the pixel position is exactly at the centre of a 64×32 cell.</summary>
    public bool IsAtCellCenter =>
        WorldX % MapFile.CellWidth == MapFile.CellWidth / 2 && WorldY % MapFile.CellHeight == MapFile.CellHeight / 2;

    /// <summary>✅ <c>0x0085e890</c> / <c>0x0085e8e0</c>: places the entity on a cell centre and stops it.</summary>
    public void PlaceAt(int cellX, int cellY)
    {
        int x = Math.Max(0, cellX);
        int y = Math.Max(0, cellY);
        CellX = DestX = x;
        CellY = DestY = y;
        WorldX = x * MapFile.CellWidth + MapFile.CellWidth / 2;
        WorldY = y * MapFile.CellHeight + MapFile.CellHeight / 2;
        State = MoveState.Standing;
        _commands.Clear();
    }

    /// <summary>✅ <c>0x008656b0</c>: sets a new destination and enters the walking/running state.</summary>
    public void SetDestination(int destX, int destY, MoveState state = MoveState.Run)
    {
        DestX = Math.Max(0, destX);
        DestY = Math.Max(0, destY);
        if (CellX != DestX || CellY != DestY)
            State = state;
    }

    /// <summary>✅ <c>0x007f3b30</c>: appends a command to the entity's queue (read at cell centres).</summary>
    public void EnqueueCommand(MoveCommand command) => _commands.Enqueue(command);

    /// <summary>
    /// ✅ <c>0x008671c0</c> / <c>0x007f3df0</c> + <c>0x0085e5b0</c> + <c>0x0085e940</c>: one logic step of movement physics.
    /// <paramref name="nextStep"/> (used in stages 2–3 for collision and pathfinding) picks the next adjacent cell from
    /// (<see cref="CellX"/>, <see cref="CellY"/>) toward (<see cref="DestX"/>, <see cref="DestY"/>); when null, steps directly
    /// toward the destination.
    /// </summary>
    public void StepPhysics(Func<int, int, int, int, (int NextX, int NextY)?>? nextStep = null)
    {
        if (State == MoveState.Standing && (CellX != DestX || CellY != DestY))
            State = MoveState.Run;

        if (!IsMoving)
            return;

        if (IsAtCellCenter)
        {
            if (CellX != DestX || CellY != DestY)
            {
                var next = nextStep is not null
                    ? nextStep(CellX, CellY, DestX, DestY)
                    : (CellX + Math.Sign(DestX - CellX), CellY + Math.Sign(DestY - CellY));
                if (next is null)
                {
                    DestX = CellX;
                    DestY = CellY;
                }
                else
                {
                    int dir = FieldDirection.FromTo(CellX, CellY, next.Value.NextX, next.Value.NextY);
                    if (dir != -1)
                        Direction = dir;
                }
            }
        }
        else if (State == MoveState.Fast)
        {
            // ✅ 0x008671c0: align half-step pixels when switching to Fast (×4) so it still hits the cell centre
            if (WorldX % 16 == 8)
                WorldX += 8;
            if (WorldY % 8 == 4)
                WorldY += 4;
        }

        // ✅ 0x0085e5b0: stop on arrival; otherwise apply base velocity × multiplier
        if (CellX == DestX && CellY == DestY)
        {
            State = MoveState.Standing;
            return;
        }

        int multiplier = State switch
        {
            MoveState.Walk => 1,
            MoveState.Run => 2,
            MoveState.Fast => 4,
            _ => 0,
        };
        var (dx, dy) = FieldDirection.BaseVelocity(Direction);
        WorldX += dx * multiplier;
        WorldY += dy * multiplier;

        // ✅ 0x0085e940 / 0x00b36c20: update cell coordinates when landing on a cell centre
        if (IsAtCellCenter)
        {
            CellX = WorldX / MapFile.CellWidth;
            CellY = WorldY / MapFile.CellHeight;
        }
    }

    /// <summary>✅ <c>0x00859340</c>: consumes one command from the queue (caller checks <see cref="IsAtCellCenter"/>).</summary>
    public void ProcessNextCommand()
    {
        if (!_commands.TryDequeue(out var cmd))
            return;
        switch (cmd.State)
        {
            case MoveState.Standing:
            case MoveState.IdleA:
            case MoveState.IdleB:
                if (State == MoveState.Run && (CellX != cmd.FromX || CellY != cmd.FromY))
                {
                    DestX = cmd.DestX;
                    DestY = cmd.DestY;
                    State = MoveState.Run;
                }
                else if (State == MoveState.Walk && FieldDirection.Chebyshev(CellX, CellY, cmd.FromX, cmd.FromY) > 1)
                {
                    DestX = cmd.DestX;
                    DestY = cmd.DestY;
                    State = MoveState.Run;
                }
                if (cmd.Direction is >= 0 and < 8)
                    Direction = cmd.Direction;
                break;

            case MoveState.Walk:
            case MoveState.Run:
            case MoveState.Fast:
                if (FieldDirection.Chebyshev(CellX, CellY, cmd.FromX, cmd.FromY) > CommandTeleportDistance)
                {
                    PlaceAt(cmd.FromX, cmd.FromY);
                    return;
                }
                DestX = Math.Max(0, cmd.DestX);
                DestY = Math.Max(0, cmd.DestY);
                State = cmd.State;
                break;
        }
    }

    /// <summary>
    /// ✅ <c>0x008671c0</c> (lines 93–165) + <c>0x007f3df0</c>: updates the partner digimon following
    /// <paramref name="leader"/> for one logic step.
    /// </summary>
    public void FollowLeader(FieldEntity leader, Func<int, int, int, int, (int NextX, int NextY)?>? nextStep = null)
    {
        if (leader.IsMoving && leader.IsAtCellCenter)
        {
            if (FieldDirection.Chebyshev(leader.CellX, leader.CellY, CellX, CellY) <= 1)
            {
                if (!IsMoving)
                    Direction = leader.Direction;
            }
            else
            {
                int dir = FieldDirection.FromTo(leader.CellX, leader.CellY, leader.DestX, leader.DestY);
                if (dir != -1)
                {
                    var (targetX, targetY) = FieldDirection.CellBehind(leader.DestX, leader.DestY, dir);
                    if (targetX != DestX || targetY != DestY)
                        EnqueueCommand(new MoveCommand(leader.State, leader.CellX, leader.CellY, targetX, targetY, 0));
                }
            }
        }

        if (FieldDirection.Chebyshev(leader.CellX, leader.CellY, CellX, CellY) > PartnerTeleportDistance)
            PlaceAt(leader.CellX - 1, leader.CellY - 1);

        if (IsAtCellCenter)
            ProcessNextCommand();
        StepPhysics(nextStep);
    }
}

/// <summary>
/// ✅ <c>0x00b4b360</c> / <c>0x00b4d290</c>: throttles outgoing <c>0x20</c> movement packets to at most one every
/// <see cref="CooldownMs"/> ms (800 ms, matching the 0.867 s median in captures.db at 30 FPS). When multiple clicks arrive
/// during the cooldown, only the latest destination is sent when it expires.
/// </summary>
public sealed class MovePacketQueue
{
    /// <summary>✅ <c>0x00b4b360</c>: <c>800 &lt; GetTickCount() - lastSent</c>.</summary>
    public const ulong CooldownMs = 800;

    private bool _hasPending;
    private short _pendingX, _pendingY;
    private bool _cooling;
    private ulong _lastSentMs;

    public bool HasPending => _hasPending;

    public void Enqueue(short x, short y)
    {
        _pendingX = x;
        _pendingY = y;
        _hasPending = true;
    }

    public bool TryDequeue(ulong nowMs, out short x, out short y)
    {
        if (_cooling && nowMs - _lastSentMs > CooldownMs)
            _cooling = false;

        if (_hasPending && !_cooling)
        {
            x = _pendingX;
            y = _pendingY;
            _hasPending = false;
            _cooling = true;
            _lastSentMs = nowMs;
            return true;
        }
        x = 0;
        y = 0;
        return false;
    }

    public void Reset()
    {
        _hasPending = false;
        _cooling = false;
    }
}
