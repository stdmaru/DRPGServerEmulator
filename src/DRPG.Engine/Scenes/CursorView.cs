using DRPG.Engine.Assets;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;

namespace DRPG.Engine.Scenes;

/// <summary>
/// The game's mouse cursor, drawn as a sprite like the original (v322 <c>0x00b3ebf0</c> loads
/// <c>Interface\cursor_*.spr</c>): the purple arrow with the spinning digivice (<c>cursor_normal</c>, 8 frames,
/// 36×36), and <c>cursor_click</c> while the button is held. The tip of the arrow, the hot spot, is pixel (0, 0).
/// ❓ Frame time and when the original shows <c>cursor_click</c> are not confirmed.
/// </summary>
public sealed class CursorView(SpriteStore sprites)
{
    private const ulong FrameMs = 100;

    private readonly ITexture[] _normal = sprites.Load("interface\\cursor_normal.spr");
    private readonly ITexture _click = sprites.Load("interface\\cursor_click.spr")[0];
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
    public void Draw(IRenderer r, ulong nowMs)
    {
        if (_x < 0 || _y < 0 || _x >= IRenderer.FrameWidth || _y >= IRenderer.FrameHeight)
            return;
        var texture = _down ? _click : _normal[(int)(nowMs / FrameMs % (ulong)_normal.Length)];
        r.Draw(texture, _x, _y);
    }
}
