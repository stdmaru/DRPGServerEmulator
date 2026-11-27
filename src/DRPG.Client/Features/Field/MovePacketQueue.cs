using DRPG.Client.World;

namespace DRPG.Client.Features.Field;

/// <summary>
/// ✅ <c>0x00b4b360</c> / <c>0x00b4d290</c>: throttles outgoing <c>0x20</c> movement packets to at most one every
/// <see cref="CooldownMs"/> ms (800 ms, matching the 0.867 s median in captures.db at 30 FPS). When several clicks arrive during
/// the cooldown, only the latest destination is sent when it expires.
/// </summary>
public sealed class MovePacketQueue
{
    /// <summary>✅ <c>0x00b4b360</c>: <c>800 &lt; GetTickCount() - lastSent</c>.</summary>
    public const ulong CooldownMs = 800;

    private Cell? _pending;
    private bool _cooling;
    private ulong _lastSentMs;

    public bool HasPending => _pending is not null;

    public void Enqueue(Cell destination) => _pending = destination;

    /// <summary>The destination to send now, when one is waiting and the cooldown is over.</summary>
    public bool TryDequeue(ulong nowMs, out Cell destination)
    {
        if (_cooling && nowMs - _lastSentMs > CooldownMs)
            _cooling = false;

        if (_pending is { } pending && !_cooling)
        {
            destination = pending;
            _pending = null;
            _cooling = true;
            _lastSentMs = nowMs;
            return true;
        }
        destination = default;
        return false;
    }

    public void Reset()
    {
        _pending = null;
        _cooling = false;
    }
}
