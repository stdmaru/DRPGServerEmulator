using DRPG.Client.Net;
using DRPG.Client.Ui;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Scenes;

/// <summary>
/// <c>CSceneServer</c>, the world selection (✅ v322 <c>0x00b504b0</c>, docs/cliente/cenas.md#csceneserver-seleção-de-mundo-):
/// the DIGITAL WORLD and BATTLE ZONE buttons, the digimon arts that slide in, the panel with the channel links, the
/// messages and the fade into the character selection. It also drives <see cref="AccountSession"/> like the original:
/// login <c>0x05</c> on entering, <c>0x03</c> on a channel click, <c>0x04</c> on the reply.
/// The state the original keeps in the bit flags <c>+0x26c0</c> is kept in named fields; the comments give the bit.
/// ❓ Not drawn: the login edit boxes of the old screen (<c>0x00b538f0</c>), the "no network" check (<c>IsNetworkAlive</c>),
/// the reply message <c>+0x2468</c> and the weak password dialog (<c>0x5fa..0x600</c>: only its first line is shown).
/// </summary>
public sealed class ServerScene(GameContext game) : IScene
{
    // ✅ positions (docs/cliente/cenas.md#init-0x00b53120)
    private const int CloseX = 693, CloseY = 545;
    private const int DigitalWorldX = 120, BattleZoneX = 430, WorldButtonY = 144;
    private const int PanelX = 363, PanelY = 72, LowerPanelY = 330;
    private const int BattleZonePanelX = 231, BattleZonePanelY = 147;

    // ✅ effect numbers registered by the init
    private const int ButtonSound = 2, OpenSound = 3, CloseSound = 4;
    private const string Music = "Sound\\title.ogg";

    // ✅ ids of the events of 0x00b51950
    private const int EventClose = 1, EventDigitalWorld = 100, EventBattleZone = 101, EventNetOcean = 102;
    private const int EventChannel1 = 200, EventChannel4 = 203, EventConsignmentStore = 210, EventChannelEvent = 213;

    // ✅ rows of the original's server table (docs/cliente/cenas.md#eventos-0x00b51950id)
    private const int BattleZoneLine = 7, ConsignmentStoreLine = 13, EventChannelLine = 16;

    // ✅ frame counts of the messages
    private const int MaintenanceFrames = 80, ErrorFrames = 20;

    private const int SlideStep = 80, FarSlideStep = 40, Far = 800;
    private const int FramesPerSecond = 30;
    private const ulong FadeDelayMs = 100;                         // ✅ Sleep(100) before the fade

    private ITexture _background = null!;                           // set in Init
    private ITexture[] _brackets = null!, _panel = null!, _lowerPanel = null!, _battleZonePanel = null!;
    private ITexture[] _digitalWorldArt = null!, _battleZoneArt = null!, _battleZoneArt2 = null!;
    private Button _close = null!, _digitalWorld = null!, _battleZone = null!;
    private MessageBox _box = null!;
    private TextLink[] _digitalWorldLinks = null!, _channelLinks = null!, _battleZoneLinks = null!;

    // bit 0 `0x1`: the panel is open and the arts slide in; zero: they slide out
    private bool _panelOpen;
    // bit 1 `0x2`, bit 2 `0x4`: the world whose art and panel are shown
    private bool _digitalWorldShown, _battleZoneShown;
    // bit 3 `0x8`: a world was chosen and the two big buttons are gone
    private bool _worldChosen;
    // bit 15 `0x8000`: the Net Ocean channel list is open
    private bool _channelListOpen;
    // `+0x78d`: frame of the DIGITAL WORLD panel (0 short, 2 tall with the channel list)
    private int _panelFrame;
    private int _hovered = -1;

    private (int X, int Y) _digitalWorldPosition = (-Far, Far);    // `+0x8d4/+0x8d8`
    private (int X, int Y) _battleZonePosition = (-Far, Far);      // `+0x8e4/+0x8e8`
    private (int X, int Y) _battleZonePosition2 = (Far, -Far);     // `+0x8dc/+0x8e0`
    private bool _lastPanelOpen;

    private string? _message;
    private int _messageFrames;
    private int _messageY = 260;

    private bool _started, _failureShown, _returning;

    /// <summary>✅ <c>+0x2480</c> after the key was refused: drawn at (400, 260) until the click (<c>0x00b538f0</c>).</summary>
    private string? _refusal;

    private const int RefusalY = 260;
    private ulong _authenticatedAtMs;
    private FadeOut? _fade;

    public int FrameIntervalMs => 1000 / FramesPerSecond;

    public void Init()
    {
        var sprites = game.Sprites;
        _background = sprites.Gui("dr2_in_002_005")[0];
        _close = new Button(sprites.Gui("dr2_in_000_006"), CloseX, CloseY);
        _digitalWorld = new Button(sprites.Gui("dr2_in_002_000"), DigitalWorldX, WorldButtonY);
        _battleZone = new Button(sprites.Gui("dr2_in_002_001"), BattleZoneX, WorldButtonY);
        _digitalWorldArt = sprites.Gui("dr2_in_002_002");
        _battleZoneArt2 = sprites.Gui("dr2_in_002_003");
        _battleZoneArt = sprites.Gui("dr2_in_002_004");
        _panel = sprites.Gui("dr2_in_002_006");
        _battleZonePanel = sprites.Gui("dr2_in_002_007");
        _brackets = sprites.Gui("dr2_in_002_008");
        _lowerPanel = sprites.Gui("dr2_in_002_010");
        _box = new MessageBox(sprites.Load("gui\\dr2_in_000_003.spr"), game.Text);

        // ✅ links, arguments of the 0x00b53db0 calls in 0x00b52b30: (text id, x, y, bracket indent, event id)
        _digitalWorldLinks =
        [
            new(0x5da, 448, 166, 30, EventNetOcean),
            new(0x5dd, 430, 366, 8, EventConsignmentStore),
            new(0x5e8, 430, 398, 8, EventChannelEvent),
        ];
        _channelLinks =
        [
            new(0x5e0, 430, 191, 9, EventChannel1 + 1),
            new(0x5e1, 430, 216, 9, EventChannel1 + 2),
            new(0x5e2, 430, 241, 9, EventChannel1 + 3),
            new(0x5e3, 430, 166, 9, EventChannel1),
        ];
        _battleZoneLinks = [new(0x5da, 326, 315, 26, EventNetOcean)];

        game.Sounds.Register(ButtonSound, "sound\\menu-bt.wav");
        game.Sounds.Register(OpenSound, "sound\\menu-1.wav");
        game.Sounds.Register(CloseSound, "sound\\menu-dark.wav");
        game.Sounds.PlayMusic(Music);

        game.Account ??= new AccountSession(game.Settings, game.Command, game.Log);
        game.Account.Reset();
    }

    public void Handle(InputEvent input)
    {
        if (input is KeyEvent { Key: GameKey.Escape } && _worldChosen && _fade is null)
        {
            // ✅ 0x00b537a0: Escape with a world chosen closes the panel; the arts leave and the big buttons come back
            _panelOpen = false;
            _worldChosen = false;
        }
    }

    public void Frame(IRenderer renderer, ulong nowMs)
    {
        var account = game.Account!;                               // created in Init
        if (!_started)
        {
            _started = true;
            account.StartLogin(nowMs);                             // ✅ 0x05 on entering the scene
        }
        account.Update(nowMs);
        FollowAccount(account, nowMs);

        renderer.Draw(_background, 0, 0);
        bool channelAccepted = _refusal is not null || (account.Step >= AccountStep.ChannelSelected && account.Step != AccountStep.Failed);
        bool clickable = _fade is null && account.Step == AccountStep.LoggedIn && _message is null;
        if (!channelAccepted)
        {
            _close.Update(game.Mouse, _fade is null);
            _close.Draw(renderer);
            if (_close.Clicked && _fade is null)
                Event(EventClose, nowMs);
            if (!_worldChosen)
                WorldButtons(renderer, account, clickable, nowMs);
        }
        ArtsAndPanels(renderer, nowMs, clickable);
        Messages(renderer);

        game.DrawVersion(renderer, 1);
        game.Text.Draw(renderer, game.Texts[0x5eb], 5, 585);       // ✅ copyright, 0x5eb
        _lastPanelOpen = _panelOpen;
        if (_fade is { } fade)
        {
            if (nowMs - _authenticatedAtMs >= FadeDelayMs)
                fade.Frame(renderer, 1f / FramesPerSecond);
            if (fade.Done)
                game.Scenes.Switch(SceneId.CharacterSelect);
        }
    }

    /// <summary>Reads what the network did since the last frame and turns it into the original's reactions.</summary>
    private void FollowAccount(AccountSession account, ulong nowMs)
    {
        switch (account.Step)
        {
            case AccountStep.ChannelSelected:
                // ✅ the reply to 0x03 is the cue for 0x04; the panel closes (bit 0 off, bit 6 on)
                _panelOpen = false;
                account.Authenticate(nowMs);
                break;
            case AccountStep.Authenticated when _fade is null:
                _authenticatedAtMs = nowMs;
                _fade = new FadeOut();
                break;
            case AccountStep.LoggedIn when account.AuthenticationRefusal is 14:
                // ❓ the weak password warning is a dialog of its own (bit 14) without disconnecting; only its first line is shown
                ShowMessage(RefusalText(14), ErrorFrames);
                _panelOpen = _worldChosen = false;
                account.Reset();
                account.StartLogin(nowMs);
                break;
            case AccountStep.LoggedIn when account.AuthenticationRefusal is { } code:
                // ✅ 0x00b524b0: the text goes to +0x2480 and the connection is closed (0x00b3f8a0). The scene stays
                // "channel accepted" (bit 6: no Close, no world buttons, the panel already closed), and 0x00b538f0 draws
                // the text every frame while there is no connection: it stays until the click that goes back to scene 2.
                _refusal = RefusalText(code);
                account.Reset();
                break;
            case AccountStep.Failed:
                if (!_failureShown)
                {
                    _failureShown = true;
                    ShowMessage(game.Texts[0x5ee], MaintenanceFrames);   // ✅ bit 13: connection failed
                }
                break;
        }
    }

    /// <summary>✅ <c>0x00b524b0</c>, packet 4 with result 3: the text of each error code; 14 is the weak password warning.</summary>
    private string RefusalText(int code) => code switch
    {
        11 => game.Texts[0x5f2],
        10 => game.Texts[0x5f3],
        16 => game.Texts[0x5f4],
        12 => game.Texts[0x5f5],
        13 => game.Texts[0x5f6],
        15 => game.Texts[0x5f7],
        0x7d2 => game.Texts[0x5f8],
        14 => game.Texts[0x5fa],
        _ => Text.PrintfFormat.Format(game.Texts[0x5f9], code),
    };

    private void ShowMessage(string text, int frames)
    {
        _message = text;
        _messageFrames = frames;
    }

    /// <summary>✅ <c>0x00b528e0</c>: the two big buttons. The one under the mouse draws frame 1 and plays 2 when the mouse enters.</summary>
    private void WorldButtons(IRenderer renderer, AccountSession account, bool clickable, ulong nowMs)
    {
        _digitalWorld.Update(game.Mouse);
        _battleZone.Update(game.Mouse);
        _digitalWorld.DrawFrame(renderer, 0);
        _battleZone.DrawFrame(renderer, 0);
        int hovered = _digitalWorld.State != 0 ? 0 : _battleZone.State != 0 ? 1 : -1;
        if (hovered == 0)
            _digitalWorld.DrawFrame(renderer, 1);
        else if (hovered == 1)
            _battleZone.DrawFrame(renderer, 1);
        if (hovered != _hovered && hovered >= 0)
            game.Sounds.PlayEffect(ButtonSound);
        _hovered = hovered;

        if (!clickable)
            return;
        if (_digitalWorld.Clicked)
            Event(EventDigitalWorld, nowMs);
        else if (_battleZone.Clicked && account.BattleZoneEnabled)    // ✅ only when the login reply allows it
            Event(EventBattleZone, nowMs);
    }

    /// <summary>✅ <c>0x00b51950(id)</c>: the actions of the buttons and links. Every one plays 2 and is ignored without a network.</summary>
    private void Event(int id, ulong nowMs)
    {
        game.Sounds.PlayEffect(ButtonSound);
        var account = game.Account!;
        if (id == EventClose)
        {
            game.QuitRequested = true;
            return;
        }
        if (account.Step != AccountStep.LoggedIn)
            return;
        switch (id)
        {
            case EventDigitalWorld:
                _panelOpen = _digitalWorldShown = _worldChosen = true;     // flags (f & ~0x4) | 0xb
                _battleZoneShown = false;
                break;
            case EventBattleZone:
                _panelOpen = _battleZoneShown = _worldChosen = true;       // flags (f & ~0x2) | 0xd
                _digitalWorldShown = false;
                // ❓ the original also logs in again here (and builds an unused scene, a bug); one login is enough
                break;
            case EventNetOcean when _digitalWorldShown:
                _panelFrame = 2;                                           // flags (f & ~0x4) | 0x800a
                _channelListOpen = true;
                _battleZoneShown = false;
                break;
            case EventNetOcean:
                Connect(BattleZoneLine, nowMs);
                break;
            case >= EventChannel1 and <= EventChannel4:
                Connect(id - EventChannel1, nowMs);                        // channels 1..4 = lines 0..3
                break;
            case EventConsignmentStore:
                Connect(ConsignmentStoreLine, nowMs);
                break;
            case EventChannelEvent:
                Connect(EventChannelLine, nowMs);
                break;
            default:
                // ❓ 204..215 (other worlds this version does not show) are not offered, so they never arrive
                break;
        }
    }

    /// <summary>✅ "Connect to line n": remembers the choice and sends <c>0x03</c> on the channel server.</summary>
    private void Connect(int line, ulong nowMs)
    {
        game.ChannelLine = line;
        game.Account!.SelectChannel(nowMs);
    }

    /// <summary>✅ <c>0x00b51550</c> and <c>0x00b52b30</c>: the arts that slide and the panels with the links.</summary>
    private void ArtsAndPanels(IRenderer renderer, ulong nowMs, bool clickable)
    {
        if (_panelOpen != _lastPanelOpen)
            game.Sounds.PlayEffect(_panelOpen ? OpenSound : CloseSound);   // ✅ 3 when it opens, 4 when it closes

        if (_digitalWorldShown)
        {
            _digitalWorldPosition = Slide(_digitalWorldPosition, (-Far, Far), SlideStep);
            renderer.Draw(_digitalWorldArt[0], _digitalWorldPosition.X, _digitalWorldPosition.Y);
            if (!_panelOpen && _digitalWorldPosition == (-Far, Far))
            {
                _digitalWorldShown = false;                                // bits 1 and 15 off
                _channelListOpen = false;
                _panelFrame = 0;
            }
        }
        if (_battleZoneShown)
        {
            _battleZonePosition = Slide(_battleZonePosition, (-Far, Far), SlideStep);
            _battleZonePosition2 = Slide(_battleZonePosition2, (Far, -Far), FarSlideStep);
            DrawBattleZoneArt(renderer, _battleZoneArt, _battleZonePosition);
            DrawBattleZoneArt(renderer, _battleZoneArt2, _battleZonePosition2);
            if (!_panelOpen && _battleZonePosition == (-Far, Far) && _battleZonePosition2 == (Far, -Far))
                _battleZoneShown = false;
        }
        if (!_panelOpen)
            return;

        if (_digitalWorldShown)
        {
            renderer.Draw(_panel[Math.Min(_panelFrame, _panel.Length - 1)], PanelX, PanelY);
            if (!_channelListOpen)
            {
                renderer.Draw(_lowerPanel[Math.Min(1, _lowerPanel.Length - 1)], PanelX, LowerPanelY);
                DrawLinks(renderer, _digitalWorldLinks, clickable, nowMs);
            }
            else
            {
                DrawLinks(renderer, _channelLinks, clickable, nowMs);
            }
        }
        else if (_battleZoneShown)
        {
            renderer.Draw(_battleZonePanel[0], BattleZonePanelX, BattleZonePanelY);
            DrawLinks(renderer, _battleZoneLinks, clickable, nowMs);
        }
    }

    /// <summary>
    /// ✅ Each art of BATTLE ZONE draws frame 1 (the silhouette) and then frame 0 (<c>FUN_00b569f0(1, …, 0xc)</c>).
    /// ❓ The blend mode <c>0xc</c> is not known: the silhouette is drawn plainly under the colour frame.
    /// </summary>
    private static void DrawBattleZoneArt(IRenderer renderer, ITexture[] art, (int X, int Y) position)
    {
        if (art.Length > 1)
            renderer.Draw(art[1], position.X, position.Y);
        renderer.Draw(art[0], position.X, position.Y);
    }

    /// <summary>
    /// ✅ The slide of an art: toward its open position while the panel is open, toward its closed one otherwise, by
    /// <paramref name="step"/> pixels per frame on each axis (80 for the digimon arts, 40 for the far one of BATTLE ZONE).
    /// </summary>
    private (int X, int Y) Slide((int X, int Y) position, (int X, int Y) closed, int step)
    {
        var target = _panelOpen ? (0, 0) : closed;
        return (Toward(position.X, target.Item1, step), Toward(position.Y, target.Item2, step));
    }

    private static int Toward(int value, int target, int step) =>
        value < target ? Math.Min(value + step, target) : Math.Max(value - step, target);

    private void DrawLinks(IRenderer renderer, TextLink[] links, bool clickable, ulong nowMs)
    {
        foreach (var link in links)
        {
            bool clicked = link.Frame(renderer, game.Text, game.Texts[link.TextId], _brackets, game.Mouse);
            if (clicked && clickable)
                Event(link.EventId, nowMs);
        }
    }

    /// <summary>✅ Centred message boxes: white text on <c>dr2_in_000_003</c> at (400, 260), for a number of frames, or until a click.</summary>
    private void Messages(IRenderer renderer)
    {
        if (_refusal is not null)
        {
            _box.Draw(renderer, _refusal, 400, RefusalY);
            // ❓ the handler of this click was not found in the dump; the owner saw the original wait for a click and then
            // show the 3 s loading screen of FUN_00b500a0(2) before the world selection
            if (game.Mouse.Pressed && !_returning)
            {
                _returning = true;
                game.Scenes.Switch(SceneId.Server);
            }
            return;
        }
        if (_message is null)
            return;
        _box.Draw(renderer, _message, 400, _messageY);
        if (--_messageFrames <= 0 || game.Mouse.Pressed)
            _message = null;
    }

    public void Dispose()
    {
    }
}
