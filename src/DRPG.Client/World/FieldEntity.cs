using DRPG.Shared.Formats.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.World;

/// <summary>
/// A moving entity on the field map (✅ v322 base <c>0x0085e1c0</c>, physics <c>0x0085e5b0</c>, cell update <c>0x0085e940</c>,
/// queue <c>0x007f3b30</c> / <c>0x00859340</c>, tamer + partner <c>0x008671c0</c>). Runs at 30 logic steps a second with exact
/// integer pixel steps so it always lands on cell centres (<see cref="Cell.CenterX"/>, <see cref="Cell.CenterY"/>).
/// </summary>
public sealed class FieldEntity
{
    /// <summary>✅ <c>0x008671c0</c>: the partner digimon teleports to (tamer − 1, tamer − 1) when farther than 5 cells.</summary>
    public const int PartnerTeleportDistance = 5;

    /// <summary>✅ <c>0x00859340</c>: a queued move command farther than 6 cells teleports the entity instead of walking.</summary>
    public const int CommandTeleportDistance = 6;

    /// <summary>
    /// ✅ <c>0x008671c0</c>: switching to <see cref="MoveState.Fast"/> (×4 = 16 px in X, 8 in Y) half way through a ×2 step
    /// moves the 8 px (X) or 4 px (Y) left over first, so the entity still lands on the cell centre.
    /// </summary>
    private const int FastStepX = 16, FastStepY = 8;

    private readonly Queue<MoveCommand> _commands = new();

    public FieldEntity(Cell cell)
        : this(cell, Direction.South)
    {
    }

    public FieldEntity(Cell cell, Direction direction)
    {
        Direction = direction;
        PlaceAt(cell);
    }

    public MoveState State { get; private set; } = MoveState.Standing;

    public Direction Direction { get; private set; }

    public int WorldX { get; private set; }

    public int WorldY { get; private set; }

    public Cell Cell { get; private set; }

    public Cell Destination { get; private set; }

    public bool IsMoving => State is MoveState.Walk or MoveState.Run or MoveState.Fast;

    /// <summary>✅ <c>0x00b35f00</c>: true when the pixel position is exactly at the centre of a 64×32 cell.</summary>
    public bool IsAtCellCenter =>
        WorldX % MapFile.CellWidth == MapFile.CellWidth / 2 && WorldY % MapFile.CellHeight == MapFile.CellHeight / 2;

    /// <summary>✅ <c>0x0085e890</c> / <c>0x0085e8e0</c>: places the entity on a cell centre, stops it and drops its queue.</summary>
    public void PlaceAt(Cell cell)
    {
        Cell = Destination = cell.NotNegative();
        WorldX = Cell.CenterX;
        WorldY = Cell.CenterY;
        State = MoveState.Standing;
        _commands.Clear();
    }

    /// <summary>✅ <c>0x007f3df0</c> lines 34-55: syncs the idle variation (<c>Standing</c>/<c>IdleA</c>/<c>IdleB</c>) into <c>+0x149</c>.</summary>
    public void SetIdleState(MoveState state)
    {
        if (!IsMoving && state is MoveState.Standing or MoveState.IdleA or MoveState.IdleB)
            State = state;
    }

    /// <summary>✅ <c>0x008656b0</c>: sets a new destination and enters the walking/running state.</summary>
    public void SetDestination(Cell destination, MoveState state = MoveState.Run)
    {
        Destination = destination.NotNegative();
        if (Cell != Destination)
            State = state;
    }

    /// <summary>✅ <c>0x007f3b30</c>: appends a command to the entity's queue (read at cell centres).</summary>
    public void EnqueueCommand(MoveCommand command) => _commands.Enqueue(command);

    /// <summary>
    /// ✅ <c>0x008671c0</c> / <c>0x007f3df0</c> + <c>0x007f4b10</c> + <c>0x0085e5b0</c> + <c>0x0085e940</c>: one logic step of
    /// movement physics. <paramref name="steps"/> picks the next adjacent cell from <see cref="Cell"/> toward
    /// <see cref="Destination"/>.
    /// </summary>
    public void StepPhysics(IStepResolver steps)
    {
        if (State == MoveState.Standing && Cell != Destination)
            State = MoveState.Run;

        if (!IsMoving)
            return;

        if (IsAtCellCenter)
            TurnToNextStep(steps);
        else if (State == MoveState.Fast)
            AlignForFastStep();

        // ✅ 0x0085e5b0: stop on arrival; otherwise apply base velocity × multiplier
        if (Cell == Destination)
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
        var velocity = FieldDirection.BaseVelocity(Direction);
        WorldX += velocity.Dx * multiplier;
        WorldY += velocity.Dy * multiplier;

        // ✅ 0x0085e940 / 0x00b36c20: update the cell when landing on a cell centre
        if (IsAtCellCenter)
            Cell = Cell.AtPixel(WorldX, WorldY);
    }

    /// <summary>
    /// ✅ <c>0x00859340</c>: consumes one command from the queue (the caller checks <see cref="IsAtCellCenter"/>).
    /// <paramref name="rule"/> is how a standing command is read for this entity type.
    /// </summary>
    public void ProcessNextCommand(CommandRule rule)
    {
        if (!_commands.TryDequeue(out var command))
            return;
        switch (command.State)
        {
            case MoveState.Standing:
            case MoveState.IdleA:
            case MoveState.IdleB:
                ApplyStandingCommand(command, rule);
                break;

            case MoveState.Walk:
            case MoveState.Run:
            case MoveState.Fast:
                if (Cell.DistanceTo(command.From) > CommandTeleportDistance)
                {
                    PlaceAt(command.From);
                    return;
                }
                Destination = command.Destination.NotNegative();
                State = command.State;
                break;
        }
    }

    /// <summary>
    /// ✅ <c>0x008671c0</c> (lines 93–165) + <c>0x007f3df0</c>: updates the partner digimon following <paramref name="leader"/>
    /// for one logic step.
    /// </summary>
    public void FollowLeader(FieldEntity leader, IStepResolver steps)
    {
        if (leader.IsMoving && leader.IsAtCellCenter)
        {
            if (leader.Cell.DistanceTo(Cell) <= 1)
            {
                if (!IsMoving)
                    Direction = leader.Direction;
            }
            else if (FieldDirection.FromTo(leader.Cell, leader.Destination) is { } direction)
            {
                var target = FieldDirection.CellBehind(leader.Destination, direction);
                if (target != Destination)
                    EnqueueCommand(new MoveCommand(leader.State, leader.Cell, target, Direction.South));
            }
        }

        if (leader.Cell.DistanceTo(Cell) > PartnerTeleportDistance)
            PlaceAt(new Cell(leader.Cell.X - 1, leader.Cell.Y - 1));

        if (IsAtCellCenter)
            ProcessNextCommand(CommandRule.Partner);
        StepPhysics(steps);
    }

    private void TurnToNextStep(IStepResolver steps)
    {
        if (Cell == Destination)
            return;
        if (steps.ResolveStep(Cell, Destination) is not { } step)
        {
            Destination = Cell;
            return;
        }
        // ✅ 0x007f4b10 lines 53-61 / 0x008671c0 lines 75-84: if the next step IS the destination and fails 0x00b35b30
        // (blocked/occupied), stop (Dest = Cell) AND still turn to face that cell.
        if (step.BlockedAtTarget)
            Destination = Cell;
        if (FieldDirection.FromTo(Cell, step.Next) is { } direction)
            Direction = direction;
    }

    private void AlignForFastStep()
    {
        if (WorldX % FastStepX == FastStepX / 2)
            WorldX += FastStepX / 2;
        if (WorldY % FastStepY == FastStepY / 2)
            WorldY += FastStepY / 2;
    }

    private void ApplyStandingCommand(MoveCommand command, CommandRule rule)
    {
        var nextState = command.State;
        bool runsToDestination = rule switch
        {
            CommandRule.Wild => Cell != command.From,
            CommandRule.Partner => Cell.DistanceTo(command.From) > 1,
            _ => false,
        };
        if (runsToDestination)
        {
            Destination = command.Destination.NotNegative();
            nextState = MoveState.Run;
        }
        else if (rule == CommandRule.Wild)
        {
            Destination = Cell;
        }

        // ✅ 0x00859340 lines 81-86 & 2320-2323: keep an idle state already below 4, always apply the direction, then the state
        if ((byte)State < (byte)MoveState.Walk)
            nextState = State;
        if (command.Direction is { } direction)
            Direction = direction;
        if (nextState != MoveState.Standing || State is not (MoveState.IdleA or MoveState.IdleB))
            State = nextState;
    }
}
