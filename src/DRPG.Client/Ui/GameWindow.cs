using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Ui;

/// <summary>
/// A window of the field (✅ base class ctor <c>0x00b613f0</c>, docs/cliente/janelas.md): an id (<c>+0x64</c>), visible (<c>+8</c>), the
/// corner (<c>+0x20</c>, <c>+0x24</c>) and the size (<c>+0x28</c>, <c>+0x2c</c>) in the original 800×600 screen. The vtable methods the
/// manager calls: open (<c>+0x18</c>), close (<c>+0x1c</c>), the frame that draws and reads the mouse (<c>+0x28</c>) and the key of the
/// second vtable (<c>+4</c>).
/// </summary>
public abstract class GameWindow(int id)
{
    /// <summary>✅ <c>+0x64</c>: the number the manager finds the window by (<c>0x00b99f70</c>).</summary>
    public int Id { get; } = id;

    /// <summary>✅ <c>+8</c>.</summary>
    public bool Visible { get; private set; }

    public int X { get; protected set; }
    public int Y { get; protected set; }
    public int Width { get; protected set; }
    public int Height { get; protected set; }

    /// <summary>✅ vtable <c>+0x18</c>: the base (<c>0x00b63b10</c>) sets <c>+8</c>; each window then places and loads what it draws.</summary>
    public void Open()
    {
        Visible = true;
        OnOpen();
    }

    /// <summary>✅ vtable <c>+0x1c</c>: the base (<c>0x00b62190</c>) clears <c>+8</c>; a window may reset its own state too.</summary>
    public void Close()
    {
        Visible = false;
        OnClose();
    }

    protected virtual void OnOpen()
    {
    }

    protected virtual void OnClose()
    {
    }

    /// <summary>✅ vtable <c>+0x28</c>, through <c>0x00b641b0</c> only while visible: draws the window and reads its buttons.</summary>
    public abstract void Frame(IRenderer renderer, MouseState mouse, ulong nowMs);

    /// <summary>✅ the second vtable's <c>+4</c>: a key sent to the window (<c>0x00b93030</c> sends Escape).</summary>
    public virtual void Key(GameKey key)
    {
    }

    /// <summary>✅ <c>0x00b626b0</c>: the mouse is inside the window's rectangle.</summary>
    public bool Contains(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}
