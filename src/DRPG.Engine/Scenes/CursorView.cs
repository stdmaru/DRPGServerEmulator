using DRPG.Engine.Assets;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;

namespace DRPG.Engine.Scenes;

/// <summary>Cursor appearance on the field (✅ v322 <c>0x00b3e3d0</c>, <c>0x00b3ebf0</c>).</summary>
public enum CursorKind
{
    /// <summary>Default arrow (<c>Interface\cursor_normal.spr</c> / <c>cursor_click.spr</c>).</summary>
    Normal,

    /// <summary>✅ <c>0x00b4e550</c> + <c>0x00b3e3d0</c> case 10: hovering a portal cell (<c>1 &lt;= type &lt;= 8999</c>), <c>Interface\cursor_door.spr</c>.</summary>
    Portal,
}

/// <summary>
/// The game's mouse cursor, drawn as a sprite like the original (v322 <c>0x00b3ebf0</c> loads
/// <c>Interface\cursor_*.spr</c>): the purple arrow with the spinning digivice (<c>cursor_normal</c>, 8 frames,
/// 36×36), <c>cursor_click</c> while the button is held, and <c>cursor_door</c> over portal cells (case 10).
/// The tip of the arrow, the hot spot, is pixel (0, 0).
/// </summary>
public sealed class CursorView(SpriteStore sprites)
{
    private const ulong NormalFrameMs = 100;

    /// <summary>✅ <c>0x00b3e3d0</c> case 10: <c>cursor_door</c> advances every 8 frames at 30 FPS (~267 ms).</summary>
    private const ulong DoorFrameMs = 267;

    private readonly ITexture[] _normal = sprites.Load("interface\\cursor_normal.spr");
    private readonly ITexture _click = sprites.Load("interface\\cursor_click.spr")[0];
    private ITexture[]? _door;                                  // loaded on first use: the launcher never shows it
    private int _x = -1, _y = -1;
    private bool _down;

    public void Handle(InputEvent e)
    {
        switch (e)
        {
            case MouseMoveEvent m:
                (_x, _y) = (m.X, m.Y);
                break;
            case MouseButtonEvent b:
                (_x, _y, _down) = (b.X, b.Y, b.Down);
                break;
            case MouseLeaveEvent:
                (_x, _y) = (-1, -1);
                break;
        }
    }

    /// <summary>Last thing drawn in a frame, so it stays on top. Nothing while the mouse is outside the frame.</summary>
    public void Draw(IRenderer r, ulong nowMs, CursorKind kind = CursorKind.Normal)
    {
        if (_x < 0 || _y < 0 || _x >= IRenderer.FrameWidth || _y >= IRenderer.FrameHeight)
            return;
        ITexture texture;
        if (kind == CursorKind.Portal && (_door ??= sprites.Load("interface\\cursor_door.spr")) is { Length: > 0 } door)
            texture = door[(int)(nowMs / DoorFrameMs % (ulong)door.Length)];
        else
            texture = _down ? _click : _normal[(int)(nowMs / NormalFrameMs % (ulong)_normal.Length)];
        r.Draw(texture, _x, _y);
    }
}
