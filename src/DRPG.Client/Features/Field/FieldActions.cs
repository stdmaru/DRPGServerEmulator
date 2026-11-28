using DRPG.Client.Net;
using DRPG.Client.World;

namespace DRPG.Client.Features.Field;

/// <summary>
/// What the player asks of the field, and the only place of the feature that sends packets (✅ <c>0x00b4c1a0</c> /
/// <c>0x00b4d290</c> / <c>0x00b4b360</c>): a ground click predicts the walk locally (<see cref="FieldState.TryWalkTo"/>) and queues the
/// <c>0x20</c> with the clicked cell, sent at most every 800 ms.
/// </summary>
public sealed class FieldActions(WorldSession session, FieldState state)
{
    private readonly MovePacketQueue _moves = new();
    private Cell? _held;

    /// <summary>True while a <c>0x20</c> waits for the cooldown.</summary>
    public bool HasPendingMove => _moves.HasPending;

    /// <summary>
    /// ✅ <c>0x00b4c1a0</c>: the mouse is pressed or held over <paramref name="target"/>. Holding it on the same cell asks nothing more.
    /// </summary>
    public void PressGround(Cell target)
    {
        if (_held == target)
            return;
        _held = target;
        if (state.TryWalkTo(target))
            _moves.Enqueue(target);
    }

    /// <summary>The mouse button is up: the next press asks again, even on the same cell.</summary>
    public void ReleaseGround() => _held = null;

    /// <summary>Drops the queued <c>0x20</c> and the held cell (a <c>0xB0</c> placed the tamer somewhere else).</summary>
    public void CancelMoves()
    {
        _moves.Reset();
        _held = null;
    }

    /// <summary>✅ <c>0x00b4b360</c>: sends the queued <c>0x20</c> when its cooldown is over; call every frame.</summary>
    public void Update(ulong nowMs)
    {
        // the cell is inside the map (TryWalkTo), and a map side is at most MapFile.MaxSide: it fits an i16
        if (_moves.TryDequeue(nowMs, out var destination))
            session.SendMove((short)destination.X, (short)destination.Y);
    }

    /// <summary>✅ <c>0x00aba410</c>: the loading screen of a map change ended: <c>0x04</c> and <c>0xB0</c>.</summary>
    public void CompleteMapTransition() => session.CompleteMapTransition();
}
