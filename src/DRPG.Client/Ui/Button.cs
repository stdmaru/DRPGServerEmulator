using DRPG.Engine.Rendering;

namespace DRPG.Client.Ui;

/// <summary>
/// A button of the original GUI (✅ class <c>0x009b6fc0</c>, docs/cliente/cenas.md#desenho-e-gui-o-que-as-cenas-usam):
/// a sprite whose frames are picked by state, or just a rectangle (the text links and the check boxes). State 0 is normal,
/// 1 under the mouse, 2 pressed, 3 disabled; <see cref="FrameMap"/> says which sprite frame each state draws (✅ <c>+0x28</c>,
/// default 0, 1, 2, 3 by <c>0x009b7670</c>; a frame the sprite does not have draws nothing, like the original).
/// </summary>
public sealed class Button
{
    public static IReadOnlyList<int> DefaultFrameMap { get; } = [0, 1, 2, 3];

    private readonly ITexture[]? _frames;
    private readonly int[] _frameMap;
    private bool _pressedInside;

    /// <summary>A button drawn from <paramref name="frames"/>, its size that of frame 0 (✅ <c>+0x10/+0x14</c>, <c>0x009b76b0</c>).</summary>
    public Button(ITexture[] frames, int x, int y, IReadOnlyList<int>? frameMap = null)
    {
        _frames = frames;
        X = x;
        Y = y;
        Width = frames.Length > 0 ? frames[0].Width : 0;
        Height = frames.Length > 0 ? frames[0].Height : 0;
        _frameMap = [.. frameMap ?? DefaultFrameMap];
    }

    /// <summary>A rectangle with no sprite (✅ <c>0x009b7610</c>).</summary>
    public Button(int x, int y, int width, int height)
    {
        (X, Y, Width, Height) = (x, y, width, height);
        _frameMap = [.. DefaultFrameMap];
    }

    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>The sprite frame of each state (normal, under the mouse, pressed, disabled).</summary>
    public IReadOnlyList<int> FrameMap => _frameMap;

    /// <summary>
    /// ✅ The owner rewrites the frame of a state each frame: the menu buttons of the bar draw frame 3 for state 0 while their
    /// window is closed and frame 0 while it is open (<c>0x00b91720</c> writes <c>+0x28</c>).
    /// </summary>
    public void SetFrame(int state, int frame) => _frameMap[state] = frame;

    /// <summary>✅ <c>+0x20</c>: 0 normal, 1 under the mouse, 2 pressed, 3 disabled.</summary>
    public int State { get; private set; }

    /// <summary>✅ <c>+0x1e</c>: the button was clicked in the last <see cref="Update"/>.</summary>
    public bool Clicked { get; private set; }

    /// <summary>The click was the second of a double click.</summary>
    public bool DoubleClicked { get; private set; }

    /// <summary>✅ <c>0x009b7040</c>: the rectangle is [x, x + w) × [y, y + h).</summary>
    public bool Contains(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;

    public bool IsUnderMouse(MouseState mouse) => Contains(mouse.X, mouse.Y);

    /// <summary>
    /// ✅ <c>0x009b7400</c>: the mouse is over the button and the button is the mouse's hot control (the last one under the mouse this
    /// frame, <c>DAT_0135a1e4+4</c>). The tooltips of the original are drawn only then.
    /// </summary>
    public bool IsHot(MouseState mouse) => ReferenceEquals(mouse.Hot, this) && IsUnderMouse(mouse);

    /// <summary>✅ <c>0x009b7660</c>: state 3, which draws <see cref="FrameMap"/>[3]. The next <see cref="Update"/> sets it again.</summary>
    public void Disable() => State = 3;

    /// <summary>
    /// ✅ The press (<c>0x009b7460</c>, vtable <c>+8</c>) and the frame's update (<c>0x009b7040</c>): a button pressed inside is state 2
    /// while held and is clicked when the mouse comes up inside; held down over it after a press elsewhere it keeps its state. Under the
    /// mouse it becomes the mouse's hot control. A disabled button stays at state 0. ✅ The window procedure gives the press only to the
    /// hot control (<c>0x00a17790</c>: <c>hot→vtable+8</c>), the one under the mouse, so a press outside never reaches a button here.
    /// </summary>
    public void Update(MouseState mouse, bool enabled = true)
    {
        Clicked = false;
        DoubleClicked = false;
        bool inside = enabled && IsUnderMouse(mouse);
        if (inside)
            mouse.Hot = this;                                         // ✅ 0x00a17880 (every positioned button has +0x38 = 1)
        if (mouse.Pressed && inside)
        {
            _pressedInside = true;                                    // ✅ 0x009b7460, inside
            State = 2;
        }
        // the second press of a double click is the event, as with WM_LBUTTONDBLCLK
        DoubleClicked = inside && mouse.Pressed && mouse.DoubleClicked;

        if (!mouse.Down)
        {
            if (!inside)
                State = 0;
            else if (_pressedInside)
            {
                Clicked = true;
                State = 2;
            }
            else
                State = 1;
            _pressedInside = false;
            return;
        }
        if (!inside)
            State = 0;
        else if (_pressedInside)
            State = 2;
        // ✅ held down over the button after pressing elsewhere: the state stays as it was
    }

    /// <summary>✅ <c>0x009b7260</c>: the frame of the current state.</summary>
    public void Draw(IRenderer renderer) => DrawFrame(renderer, State < _frameMap.Length ? _frameMap[State] : -1);

    /// <summary>✅ <c>0x009b71e0</c>: a given frame whatever the state.</summary>
    public void DrawFrame(IRenderer renderer, int frame)
    {
        if (_frames is null || frame < 0 || frame >= _frames.Length)
            return;
        renderer.Draw(_frames[frame], X, Y);
    }
}
