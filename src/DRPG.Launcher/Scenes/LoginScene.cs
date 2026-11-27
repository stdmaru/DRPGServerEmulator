using DRPG.Engine.Assets;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;

namespace DRPG.Launcher.Scenes;

/// <summary>
/// The old login screen rebuilt from its sprites. Positions and timings: docs/cliente/tela-login-launcher.md.
/// While patching, the Loading box (<see cref="PatchView"/>) takes the panel's place.
/// </summary>
public sealed class LoginScene : IDisposable
{
    private const int PanelX = 455, PanelY = 165;
    private const int StatusX = 490, StatusY = 392;

    // Each mascot runs its own clock, so they never move in step (as in the original).
    private const ulong MascotAFrameMs = 300, MascotBFrameMs = 200;

    // "Remember ID": the panel's 13×13 square at (24, 164); the check is dr2_sub_001_001 (13×13) drawn over it.
    // ❓ Original check sprite, label text and colour not found (no draw code for this screen survives).
    private const int CheckX = PanelX + 24, CheckY = PanelY + 164, CheckSize = 13;
    private const int LabelX = CheckX + CheckSize + 4;
    private const string RememberLabel = "Remember ID";
    private const uint TextColor = 0xFFFFFFFF;

    public const int MaxLoginLength = RememberedLogin.MaxLoginLength;
    public const int MaxPasswordLength = 64;

    private readonly ITexture _background, _panel, _panelDisabled, _status, _check;
    private readonly ITexture _fieldsDisabled, _checkDisabled;
    private readonly ITexture[] _mascotA, _mascotB, _mascotADisabled, _mascotBDisabled;
    private readonly ITexture? _rememberLabel, _rememberLabelDisabled;
    private readonly Button _ok, _cancel;
    private readonly TextField _id, _password;
    private readonly RememberedLogin _remember;
    private readonly IFont? _font;
    private readonly IRenderer _renderer;
    private ITexture? _statusText;
    private string? _statusTextValue;

    public LoginScene(SpriteStore sprites, RememberedLogin remember, IFont? font, IRenderer renderer)
    {
        _remember = remember;
        _font = font;
        _renderer = renderer;
        _background = sprites.Gui("dr2_in_000_000")[0];
        _panel = sprites.Gui("dr2_in_003_000")[0];
        _panelDisabled = sprites.GuiDisabled("dr2_in_003_000")[0];
        _mascotA = sprites.Gui("dr2_in_003_001");
        _mascotB = sprites.Gui("dr2_in_003_002");
        _mascotADisabled = sprites.GuiDisabled("dr2_in_003_001");
        _mascotBDisabled = sprites.GuiDisabled("dr2_in_003_002");
        _status = sprites.Gui("dr_friend_016")[0];
        _check = sprites.Gui("dr2_sub_001_001")[0];
        // the ID and password fields and the checkbox square, seeded at their centres (panel coordinates)
        _fieldsDisabled = sprites.GuiDisabledRegions("dr2_in_003_000", (211, 91), (211, 137), (30, 170));
        _checkDisabled = sprites.GuiDisabled("dr2_sub_001_001")[0];
        _rememberLabel = font?.Render(renderer, RememberLabel, TextColor);
        _rememberLabelDisabled = font?.Render(renderer, RememberLabel, TextField.DisabledTextColor);
        _ok = new Button(sprites.Gui("dr2_in_003_003"), sprites.GuiDisabled("dr2_in_003_003")[0], PanelX + 148, PanelY + 170);
        _cancel = new Button(sprites.Gui("dr2_in_003_004"), sprites.GuiDisabled("dr2_in_003_004")[0], PanelX + 206, PanelY + 170);
        // The dark inner part of the ID and PASSWORD bars, measured on dr2_in_003_000.
        _id = new TextField(PanelX + 138, PanelY + 83, 146, 18, MaxLoginLength, masked: false, allowSpace: false);
        _password = new TextField(PanelX + 138, PanelY + 129, 146, 18, MaxPasswordLength, masked: true, allowSpace: true);

        _id.SetText(remember.Login);
        Focus(_id.Text.Length > 0 ? _password : _id);
    }

    public bool CloseRequested { get; private set; }
    public bool SubmitRequested { get; set; }

    public string Login => _id.Text;
    public string Password => _password.Text;

    /// <summary>True only while connected to the server and idle (handshake done). Otherwise fields, checkbox and OK
    /// are drawn grey and ignore input; CANCEL, Esc and closing the window always work.</summary>
    public bool Interactive { get; set; }

    /// <summary>False while the auto-patcher has not finished: the entire login panel is drawn greyed out.</summary>
    public bool PatchComplete { get; set; }

    /// <summary>After a failed login: the password is typed again, the ID stays.</summary>
    public void ClearPassword()
    {
        _password.Clear();
        Focus(_id.Text.Length > 0 ? _password : _id);
    }

    public void Handle(InputEvent e)
    {
        if (e is KeyEvent { Key: GameKey.Escape })
        {
            CloseRequested = true;
            return;
        }
        // CANCEL is never disabled: the launcher can always be closed.
        switch (e)
        {
            case MouseMoveEvent m:
                _cancel.Hover(m.X, m.Y);
                break;
            case MouseButtonEvent b:
                if (_cancel.Press(b.X, b.Y, b.Down)) CloseRequested = true;
                break;
        }
        if (!Interactive)
        {
            _ok.Reset();
            return;
        }
        switch (e)
        {
            case MouseMoveEvent m:
                _ok.Hover(m.X, m.Y);
                break;
            case MouseButtonEvent b:
                if (b.Down && OnRemember(b.X, b.Y)) _remember.Toggle();
                if (b.Down && _id.Contains(b.X, b.Y)) Focus(_id);
                if (b.Down && _password.Contains(b.X, b.Y)) Focus(_password);
                if (_ok.Press(b.X, b.Y, b.Down)) SubmitRequested = true;
                break;
            case KeyEvent { Key: GameKey.Enter }:
                SubmitRequested = true;
                break;
            case KeyEvent { Key: GameKey.Tab }:
                Focus(_id.Focused ? _password : _id);
                break;
            case KeyEvent { Key: GameKey.Backspace }:
                FocusedField.Backspace();
                break;
            case TextEvent t:
                FocusedField.Type(t.Text);
                break;
        }
    }

    private TextField FocusedField => _password.Focused ? _password : _id;

    private void Focus(TextField field)
    {
        _id.Focused = field == _id;
        _password.Focused = field == _password;
    }

    public void DrawBackground(IRenderer r) => r.Draw(_background, 0, 0);

    /// <summary>Only while there is something to say (connecting, errors, login progress): the caller skips it
    /// otherwise. A text wider than the box shows its middle.</summary>
    public void DrawStatusBox(IRenderer r, string text)
    {
        r.Draw(_status, StatusX, StatusY);
        if (_font is null || text.Length == 0)
            return;
        if (_statusTextValue != text)
        {
            _statusText?.Dispose();
            _statusText = _font.Render(_renderer, text, TextColor);
            _statusTextValue = text;
        }
        var t = _statusText!;
        int inner = _status.Width - 8;
        int width = Math.Min(t.Width, inner);
        int x = StatusX + (_status.Width - width) / 2;
        int y = StatusY + (_status.Height - _font.LineHeight + 1) / 2;
        r.DrawPart(t, x, y, (t.Width - width) / 2, width);
    }

    public void DrawPanel(IRenderer r, ulong nowMs)
    {
        bool greyPanel = !PatchComplete;
        r.Draw(greyPanel ? _panelDisabled : _panel, PanelX, PanelY);
        var mascotA = greyPanel ? _mascotADisabled : _mascotA;
        var mascotB = greyPanel ? _mascotBDisabled : _mascotB;
        int frameA = greyPanel ? 0 : (int)(nowMs / MascotAFrameMs % (ulong)mascotA.Length);
        int frameB = greyPanel ? 0 : (int)(nowMs / MascotBFrameMs % (ulong)mascotB.Length);
        r.Draw(mascotA[frameA], PanelX + 225, PanelY + 2);
        r.Draw(mascotB[frameB], PanelX + 270, PanelY - 2);
        bool enabled = Interactive && PatchComplete;
        if (!enabled && !greyPanel)
            r.Draw(_fieldsDisabled, PanelX, PanelY);
        if (_remember.Remember)
            r.Draw(enabled ? _check : _checkDisabled, CheckX, CheckY);
        if ((enabled ? _rememberLabel : _rememberLabelDisabled) is { } label)
            r.Draw(label, LabelX, CheckY + (CheckSize - label.Height + 1) / 2);

        _id.Draw(r, _font, nowMs, enabled);
        _password.Draw(r, _font, nowMs, enabled);
        _ok.Draw(r, enabled);
        _cancel.Draw(r, enabled: true);
    }

    /// <summary>The square or its label.</summary>
    private bool OnRemember(int x, int y) =>
        y >= CheckY && y < CheckY + CheckSize && x >= CheckX && x < LabelX + (_rememberLabel?.Width ?? 0);

    public void Dispose()
    {
        _statusText?.Dispose();
        _rememberLabel?.Dispose();
        _rememberLabelDisabled?.Dispose();
    }

    /// <summary>Three-state button: normal, hover, pressed (frame order of the sprite; ❓ to confirm in CSceneServer),
    /// plus a grey copy of the normal frame when disabled.</summary>
    private sealed class Button(ITexture[] frames, ITexture disabled, int x, int y)
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

        public void Draw(IRenderer r, bool enabled)
        {
            if (!enabled)
            {
                r.Draw(disabled, x, y);
                return;
            }
            int frame = 0;
            if (_pressed)
                frame = 2;
            else if (_hover)
                frame = 1;
            r.Draw(frames[Math.Min(frame, frames.Length - 1)], x, y);
        }
    }
}
