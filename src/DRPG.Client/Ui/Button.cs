using DRPG.Engine.Rendering;

namespace DRPG.Client.Ui;

/// <summary>
/// A button of the original GUI (✅ class <c>0x009b6fc0</c>, docs/cliente/cenas.md#desenho-e-gui-o-que-as-cenas-usam):
/// a sprite whose frames are picked by state, or just a rectangle (the text links). State 0 is normal, 1 under the mouse,
/// 2 pressed; <see cref="FrameMap"/> says which sprite frame each state draws (default 0, 1, 2, 3; a frame the sprite
/// does not have draws nothing, like the original). ❓ The original's press/release pair (<c>+0x18</c> of the mouse
/// handler) is read here as: pressed while the button is held after going down inside, clicked when it comes up inside.
/// </summary>
public sealed class Button
{
    public static readonly int[] DefaultFrameMap = [0, 1, 2, 3];

    private readonly ITexture[]? _frames;
    private bool _pressedInside;

    /// <summary>A button drawn from <paramref name="frames"/>, its size that of frame 0 (✅ <c>+0x10/+0x14</c>).</summary>
    public Button(ITexture[] frames, int x, int y, int[]? frameMap = null)
    {
        _frames = frames;
        X = x;
        Y = y;
        Width = frames.Length > 0 ? frames[0].Width : 0;
        Height = frames.Length > 0 ? frames[0].Height : 0;
        FrameMap = frameMap ?? DefaultFrameMap;
    }

    /// <summary>A rectangle with no sprite (✅ <c>0x009b7610</c>).</summary>
    public Button(int x, int y, int width, int height)
    {
        (X, Y, Width, Height) = (x, y, width, height);
        FrameMap = DefaultFrameMap;
    }

    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>The sprite frame of each state (normal, under the mouse, pressed, ❓ the fourth is unused here).</summary>
    public int[] FrameMap { get; }

    /// <summary>0 normal, 1 under the mouse, 2 pressed.</summary>
    public int State { get; private set; }

    /// <summary>The button was clicked in the last <see cref="Update"/>.</summary>
    public bool Clicked { get; private set; }

    /// <summary>The click was the second of a double click.</summary>
    public bool DoubleClicked { get; private set; }

    /// <summary>✅ <c>0x009b7040</c>: the rectangle is [x, x + w) × [y, y + h).</summary>
    public bool Contains(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;

    public bool IsUnderMouse(MouseState mouse) => Contains(mouse.X, mouse.Y);

    /// <summary>
    /// ✅ <c>0x009b7140</c>: reads the mouse and sets <see cref="State"/>, <see cref="Clicked"/> and
    /// <see cref="DoubleClicked"/>. A disabled button stays at state 0.
    /// </summary>
    public void Update(MouseState mouse, bool enabled = true)
    {
        Clicked = false;
        DoubleClicked = false;
        bool inside = enabled && IsUnderMouse(mouse);
        if (inside && mouse.Pressed)
            _pressedInside = true;
        if (!mouse.Down && !mouse.Released)
            _pressedInside = false;
        if (!inside)
        {
            State = 0;
            return;
        }
        State = mouse.Down && _pressedInside ? 2 : 1;
        // the second press of a double click is the event, as with WM_LBUTTONDBLCLK
        DoubleClicked = mouse.Pressed && mouse.DoubleClicked;
        if (mouse.Released && _pressedInside)
        {
            Clicked = true;
            State = 2;
            _pressedInside = false;
        }
    }

    /// <summary>✅ <c>0x009b7260</c>: the frame of the current state.</summary>
    public void Draw(IRenderer renderer) => DrawFrame(renderer, State < FrameMap.Length ? FrameMap[State] : -1);

    /// <summary>✅ <c>0x009b71e0</c>: a given frame whatever the state.</summary>
    public void DrawFrame(IRenderer renderer, int frame)
    {
        if (_frames is null || frame < 0 || frame >= _frames.Length)
            return;
        renderer.Draw(_frames[frame], X, Y);
    }
}
