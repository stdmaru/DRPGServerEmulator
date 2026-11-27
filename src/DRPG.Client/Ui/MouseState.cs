using DRPG.Engine.Platform;

namespace DRPG.Client.Ui;

/// <summary>
/// The mouse as the scenes read it (✅ v322 <c>DAT_0135a1e4</c>): where it is, whether the button is down, and what
/// happened since the last frame. The events arrive between frames; the scene calls <see cref="EndFrame"/> when it is done.
/// ❓ The original gets the double click from Windows (500 ms, a few pixels); the same numbers are used here.
/// </summary>
public sealed class MouseState
{
    /// <summary>Windows default double click time.</summary>
    public const ulong DoubleClickMs = 500;

    /// <summary>Windows default: both clicks inside a box of this many pixels around the first.</summary>
    public const int DoubleClickRadius = 2;

    private ulong? _lastPressMs;
    private int _lastPressX, _lastPressY;

    public int X { get; private set; } = -1;
    public int Y { get; private set; } = -1;
    public bool Down { get; private set; }

    /// <summary>The button went down since the last frame.</summary>
    public bool Pressed { get; private set; }

    /// <summary>The button went up since the last frame.</summary>
    public bool Released { get; private set; }

    /// <summary>The press of this frame was the second of a double click.</summary>
    public bool DoubleClicked { get; private set; }

    public void Handle(InputEvent input, ulong nowMs)
    {
        switch (input)
        {
            case MouseMoveEvent move:
                (X, Y) = (move.X, move.Y);
                break;
            case MouseLeaveEvent:
                (X, Y) = (-1, -1);
                Down = false;
                break;
            case MouseButtonEvent { Down: true } press:
                (X, Y) = (press.X, press.Y);
                Down = true;
                Pressed = true;
                DoubleClicked = _lastPressMs is { } last && nowMs - last <= DoubleClickMs
                    && Math.Abs(press.X - _lastPressX) <= DoubleClickRadius && Math.Abs(press.Y - _lastPressY) <= DoubleClickRadius;
                // a third press starts again instead of chaining double clicks
                _lastPressMs = DoubleClicked ? null : nowMs;
                (_lastPressX, _lastPressY) = (press.X, press.Y);
                break;
            case MouseButtonEvent release:
                (X, Y) = (release.X, release.Y);
                Down = false;
                Released = true;
                break;
        }
    }

    public void EndFrame()
    {
        Pressed = false;
        Released = false;
        DoubleClicked = false;
    }
}
