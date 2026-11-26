using DRPG.Launcher.Assets;
using DRPG.Launcher.Platform;
using DRPG.Launcher.Rendering;

namespace DRPG.Launcher.Scenes;

/// <summary>
/// The old login screen rebuilt from its sprites. Positions and timings: docs/cliente/tela-login-launcher.md.
/// Text (fields and status) is not drawn yet: it needs the font renderer.
/// </summary>
public sealed class LoginScene
{
    private const int PanelX = 243, PanelY = 151;
    private const int StatusX = 278, StatusY = 392;
    private const ulong MascotFrameMs = 300;

    private readonly ITexture _background, _panel, _status;
    private readonly ITexture[] _mascotA, _mascotB;
    private readonly Button _ok, _cancel;

    public LoginScene(SpriteStore sprites)
    {
        _background = sprites.Gui("dr2_in_000_000")[0];
        _panel = sprites.Gui("dr2_in_003_000")[0];
        _mascotA = sprites.Gui("dr2_in_003_001");
        _mascotB = sprites.Gui("dr2_in_003_002");
        _status = sprites.Gui("dr_friend_016")[0];
        _ok = new Button(sprites.Gui("dr2_in_003_003"), PanelX + 148, PanelY + 170);
        _cancel = new Button(sprites.Gui("dr2_in_003_004"), PanelX + 206, PanelY + 170);
    }

    public bool CloseRequested { get; private set; }
    public bool SubmitRequested { get; private set; }

    public void Handle(InputEvent e)
    {
        switch (e)
        {
            case MouseMoveEvent m:
                _ok.Hover(m.X, m.Y);
                _cancel.Hover(m.X, m.Y);
                break;
            case MouseButtonEvent b:
                if (_ok.Press(b.X, b.Y, b.Down)) SubmitRequested = true;
                if (_cancel.Press(b.X, b.Y, b.Down)) CloseRequested = true;
                break;
            case KeyEvent { Key: LauncherKey.Escape }:
                CloseRequested = true;
                break;
            case KeyEvent { Key: LauncherKey.Enter }:
                SubmitRequested = true;
                break;
        }
    }

    public void Draw(IRenderer r, ulong nowMs)
    {
        int frame = (int)(nowMs / MascotFrameMs);
        r.Draw(_background, 0, 0);
        r.Draw(_panel, PanelX, PanelY);
        r.Draw(_mascotA[frame % _mascotA.Length], PanelX + 225, PanelY + 2);
        r.Draw(_mascotB[frame % _mascotB.Length], PanelX + 270, PanelY - 2);
        _ok.Draw(r);
        _cancel.Draw(r);
        r.Draw(_status, StatusX, StatusY);
    }

    /// <summary>Three-state button: normal, hover, pressed (frame order of the sprite; ❓ to confirm in CSceneServer).</summary>
    private sealed class Button(ITexture[] frames, int x, int y)
    {
        private bool _hover, _pressed;

        private bool Contains(int px, int py) => px >= x && py >= y && px < x + frames[0].Width && py < y + frames[0].Height;

        public void Hover(int px, int py) => _hover = Contains(px, py);

        /// <summary>True on release over the button after pressing it.</summary>
        public bool Press(int px, int py, bool down)
        {
            bool inside = Contains(px, py);
            if (down)
            {
                _pressed = inside;
                return false;
            }
            bool clicked = _pressed && inside;
            _pressed = false;
            return clicked;
        }

        public void Draw(IRenderer r) =>
            r.Draw(frames[Math.Min(_pressed ? 2 : _hover ? 1 : 0, frames.Length - 1)], x, y);
    }
}
