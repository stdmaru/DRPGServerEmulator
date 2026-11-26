using DRPG.Launcher.Assets;
using DRPG.Launcher.Platform;
using DRPG.Launcher.Rendering;

namespace DRPG.Launcher.Scenes;

/// <summary>
/// The old login screen rebuilt from its sprites. Positions and timings: docs/cliente/tela-login-launcher.md.
/// Field and status text is not drawn yet. While patching, the Loading box
/// (<see cref="PatchView"/>) takes the panel's place.
/// </summary>
public sealed class LoginScene
{
    private const int PanelX = 243, PanelY = 151;
    private const int StatusX = 278, StatusY = 392;
    // Each mascot runs its own clock, so they never move in step (as in the original).
    private const ulong MascotAFrameMs = 300, MascotBFrameMs = 200;

    // "Remember ID": the panel's 13×13 square at (24, 164); the check is dr2_sub_001_001 (13×13) drawn over it.
    // ❓ Original check sprite, label text and colour not found (no draw code for this screen survives).
    private const int CheckX = PanelX + 24, CheckY = PanelY + 164, CheckSize = 13;
    private const int LabelX = CheckX + CheckSize + 4;
    private const string RememberLabel = "Remember ID";
    private const uint LabelColor = 0xFFFFFFFF;

    private readonly ITexture _background, _panel, _status, _check;
    private readonly ITexture[] _mascotA, _mascotB;
    private readonly ITexture? _rememberLabel;
    private readonly Button _ok, _cancel;
    private readonly RememberedLogin _remember;

    public LoginScene(SpriteStore sprites, RememberedLogin remember, IFont? font, IRenderer renderer)
    {
        _remember = remember;
        _check = sprites.Gui("dr2_sub_001_001")[0];
        _rememberLabel = font?.Render(renderer, RememberLabel, LabelColor);
        _background = sprites.Gui("dr2_in_000_000")[0];
        _panel = sprites.Gui("dr2_in_003_000")[0];
        _mascotA = sprites.Gui("dr2_in_003_001");
        _mascotB = sprites.Gui("dr2_in_003_002");
        _status = sprites.Gui("dr_friend_016")[0];
        _ok = new Button(sprites.Gui("dr2_in_003_003"), PanelX + 148, PanelY + 170);
        _cancel = new Button(sprites.Gui("dr2_in_003_004"), PanelX + 206, PanelY + 170);
    }

    public bool CloseRequested { get; private set; }
    public bool SubmitRequested { get; set; }

    /// <summary>False while patching and connecting: the panel is shown frozen and ignores mouse and keys (Esc and
    /// closing the window still quit).</summary>
    public bool Interactive { get; set; }

    public void Handle(InputEvent e)
    {
        if (e is KeyEvent { Key: LauncherKey.Escape })
        {
            CloseRequested = true;
            return;
        }
        if (!Interactive)
        {
            _ok.Reset();
            _cancel.Reset();
            return;
        }
        switch (e)
        {
            case MouseMoveEvent m:
                _ok.Hover(m.X, m.Y);
                _cancel.Hover(m.X, m.Y);
                break;
            case MouseButtonEvent b:
                if (b.Down && OnRemember(b.X, b.Y)) _remember.Toggle();
                if (_ok.Press(b.X, b.Y, b.Down)) SubmitRequested = true;
                if (_cancel.Press(b.X, b.Y, b.Down)) CloseRequested = true;
                break;
            case KeyEvent { Key: LauncherKey.Enter }:
                SubmitRequested = true;
                break;
        }
    }

    public void DrawBackground(IRenderer r) => r.Draw(_background, 0, 0);

    /// <summary>Only while there is something to say (connecting, errors, login progress).</summary>
    public void DrawStatusBox(IRenderer r) => r.Draw(_status, StatusX, StatusY);

    public void DrawPanel(IRenderer r, ulong nowMs)
    {
        r.Draw(_panel, PanelX, PanelY);
        r.Draw(_mascotA[(int)(nowMs / MascotAFrameMs % (ulong)_mascotA.Length)], PanelX + 225, PanelY + 2);
        r.Draw(_mascotB[(int)(nowMs / MascotBFrameMs % (ulong)_mascotB.Length)], PanelX + 270, PanelY - 2);
        if (_remember.Remember)
            r.Draw(_check, CheckX, CheckY);
        if (_rememberLabel is { } label)
            r.Draw(label, LabelX, CheckY + (CheckSize - label.Height + 1) / 2);
        _ok.Draw(r);
        _cancel.Draw(r);
    }

    /// <summary>The square or its label.</summary>
    private bool OnRemember(int x, int y) =>
        y >= CheckY && y < CheckY + CheckSize && x >= CheckX && x < LabelX + (_rememberLabel?.Width ?? 0);

    /// <summary>Three-state button: normal, hover, pressed (frame order of the sprite; ❓ to confirm in CSceneServer).</summary>
    private sealed class Button(ITexture[] frames, int x, int y)
    {
        private bool _hover, _pressed;

        private bool Contains(int px, int py) => px >= x && py >= y && px < x + frames[0].Width && py < y + frames[0].Height;

        public void Hover(int px, int py) => _hover = Contains(px, py);

        public void Reset() => _hover = _pressed = false;

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
