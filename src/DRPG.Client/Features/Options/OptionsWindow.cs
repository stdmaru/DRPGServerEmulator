using DRPG.Client.Features.Chat;
using DRPG.Client.Scenes;
using DRPG.Client.Ui;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Features.Options;

/// <summary>
/// The options window, window 0xf (✅ ctor <c>0x00b161b0</c>, vtable <c>0x0102d3f8</c>: load <c>0x00b18540</c>, open <c>0x00b181c0</c>,
/// frame <c>0x00b16980</c> + <c>0x00b18000</c>; docs/cliente/janelas.md#opções-janela-0xf). The fourth menu button opens it. Video
/// (window mode, shadow, translucency, card slash), the music and effect volumes, the four refusals, "View My Item", "Simplified", the
/// language, and the Hot Key, Location Sync and Exit Game buttons. Every choice takes effect at once.
/// </summary>
public sealed class OptionsWindow : GameWindow
{
    public const int WindowId = 0xf;

    /// <summary>✅ The window that "Hot Key (H)" opens and closes (<c>0x00b16b2c</c>).</summary>
    public const int HotKeyWindow = 0x55;

    /// <summary>✅ The chat lines of the options: the trade refusal on (<c>0xef7</c>) and off (<c>0xef8</c>), Location Sync in the BATTLE ZONE (<c>0xef6</c>).</summary>
    public const int TradeRefusedText = 0xef7, TradeAcceptedText = 0xef8, SyncInBattleZoneText = 0xef6;

    /// <summary>✅ The background's frame 1 is mixed at 16/32 over frame 0 (<c>0x00b569f0(1, …, −1, 0x10, …)</c>).</summary>
    public const int GlowAlpha = 0x10;

    /// <summary>✅ <c>0x00b18540</c>: centred, 20 px above the middle: <c>((0x320 − w) / 2, (0x258 − h) / 2 − 20)</c>.</summary>
    private const int RaisedBy = 20;

    // ✅ 0x00b18540 / 0x00b181c0: every control's corner from the window's
    private const int CloseLeft = 0xbe, CloseTop = 6;
    private const int VideoLeft = 0x3c, ShadowTop = 0x3b, AlphaTop = 0x49, CardTop = 0x59;
    private const int WindowModeLeft = 0x1a, WindowModeTop = 0x3b;
    private const int RefuseLeft = 0x15, RefuseRight = 0x6e, RefuseFirstRow = 0xe9, RefuseSecondRow = 0xff, ViewItemTop = 0x115;
    private const int VolumeLeft = 0x46, VolumeStep = 0x1e, MusicTop = 0x93, EffectTop = 0xb3;
    private const int SimplifiedLeft = 0x1b, SimplifiedTop = 0x150, RadioStep = 0x13;
    private const int LanguageLeft = 0x7c, LanguageTop = 0x195;
    private const int HotKeyLeft = 0xb, ExitLeft = 0x6f, BottomButtonsTop = 0x1c1, SyncLeft = 0xb, SyncTop = 0x196;

    private const string CheckSprite = "dr2_sub_001_001", RadioSprite = "dr2_sub_001_002";

    private readonly GameContext _game;
    private readonly WindowManager _windows;
    private readonly ChatLog _chat;
    private readonly ITexture[] _background, _check, _radio;
    private readonly Button _close, _hotKey, _exit, _sync;
    private readonly Button _shadow, _alpha, _card, _windowMode, _whisper, _party, _fight, _trade, _viewItem;
    private readonly Button[] _music, _effect, _simplified, _language;

    public OptionsWindow(GameContext game, UiSprites sprites, WindowManager windows, ChatLog chat) : base(WindowId)
    {
        _game = game;
        _windows = windows;
        _chat = chat;
        _background = sprites.Gui("dr2_sub_001_000");
        _check = sprites.Gui(CheckSprite);
        _radio = sprites.Gui(RadioSprite);
        _close = new Button(sprites.Gui("dr_friend_002"), 0, 0);
        _hotKey = new Button(sprites.Gui("dr2_sub_001_003"), 0, 0);
        _exit = new Button(sprites.Gui("dr2_sub_001_004"), 0, 0);
        _sync = new Button(sprites.Gui("dr2_sub_001_005"), 0, 0);
        _shadow = CheckBox();
        _alpha = CheckBox();
        _card = CheckBox();
        _whisper = CheckBox();
        _party = CheckBox();
        _fight = CheckBox();
        _trade = CheckBox();
        _viewItem = CheckBox();
        _windowMode = Radio();                                         // ✅ the window mode's area is the radio mark's size
        _music = [.. Enumerable.Range(0, GameOptions.VolumeLevels).Select(_ => Radio())];
        _effect = [.. Enumerable.Range(0, GameOptions.VolumeLevels).Select(_ => Radio())];
        _simplified = [.. Enumerable.Range(0, GameOptions.SimplifiedChoices).Select(_ => Radio())];
        _language = [.. Enumerable.Range(0, GameOptions.Languages).Select(_ => Radio())];
        Width = _background.Length > 0 ? _background[0].Width : 0;
        Height = _background.Length > 0 ? _background[0].Height : 0;
        X = (IRenderer.FrameWidth - Width) / 2;
        Y = (IRenderer.FrameHeight - Height) / 2 - RaisedBy;
        Place();
    }

    private Button CheckBox() => new(0, 0, Size(_check).Width, Size(_check).Height);

    private Button Radio() => new(0, 0, Size(_radio).Width, Size(_radio).Height);

    private static (int Width, int Height) Size(ITexture[] frames) => frames.Length > 0 ? (frames[0].Width, frames[0].Height) : (0, 0);

    /// <summary>✅ <c>0x00b181c0</c>: every control goes back to its place from the window's corner when it opens.</summary>
    protected override void OnOpen() => Place();

    private void Place()
    {
        Move(_close, CloseLeft, CloseTop);
        Move(_shadow, VideoLeft, ShadowTop);
        Move(_alpha, VideoLeft, AlphaTop);
        Move(_card, VideoLeft, CardTop);
        Move(_windowMode, WindowModeLeft, WindowModeTop);
        Move(_whisper, RefuseLeft, RefuseFirstRow);
        Move(_party, RefuseRight, RefuseFirstRow);
        Move(_fight, RefuseLeft, RefuseSecondRow);
        Move(_trade, RefuseRight, RefuseSecondRow);
        Move(_viewItem, RefuseLeft, ViewItemTop);
        for (int i = 0; i < _music.Length; i++)
        {
            Move(_music[i], VolumeLeft + i * VolumeStep, MusicTop);
            Move(_effect[i], VolumeLeft + i * VolumeStep, EffectTop);
        }
        for (int i = 0; i < _simplified.Length; i++)
            Move(_simplified[i], SimplifiedLeft, SimplifiedTop + i * RadioStep);
        for (int i = 0; i < _language.Length; i++)
            Move(_language[i], LanguageLeft, LanguageTop + i * RadioStep);
        Move(_hotKey, HotKeyLeft, BottomButtonsTop);
        Move(_exit, ExitLeft, BottomButtonsTop);
        Move(_sync, SyncLeft, SyncTop);
    }

    private void Move(Button button, int left, int top) => (button.X, button.Y) = (X + left, Y + top);

    /// <summary>✅ <c>0x00b16980</c>, in its order. The bottom buttons are drawn before they read the mouse, as in the original.</summary>
    public override void Frame(IRenderer renderer, MouseState mouse, ulong nowMs)
    {
        if (_background.Length > 0)
        {
            renderer.Draw(_background[0], X, Y);
            if (_background.Length > 1)
                _game.SpriteOptions.DrawBlended(renderer, _background[1], X, Y, GlowAlpha);
        }
        if (DrawThenRead(renderer, _close, mouse))
        {
            Close();                                                   // ✅ the vtable's close: no sound
            return;
        }
        if (DrawThenRead(renderer, _hotKey, mouse))
            _windows.Toggle(HotKeyWindow);
        if (DrawThenRead(renderer, _exit, mouse))
        {
            // ✅ outside a battle the exit window opens with its question; the options close either way
            _windows.Open(WindowManager.ExitWindow);
            Close();
            return;
        }
        if (DrawThenRead(renderer, _sync, mouse))
            LocationSync();

        var sprite = _game.SpriteOptions;
        if (Toggle(renderer, _shadow, mouse, sprite.Shadow) is { } shadow)
            _game.SpriteOptions = _game.SpriteOptions with { Shadow = shadow };
        if (Toggle(renderer, _alpha, mouse, _game.SpriteOptions.Alpha) is { } alpha)
            _game.SpriteOptions = _game.SpriteOptions with { Alpha = alpha };
        if (Toggle(renderer, _card, mouse, _game.SpriteOptions.Card) is { } card)
            _game.SpriteOptions = _game.SpriteOptions with { Card = card };
        var options = _game.Options;
        if (Toggle(renderer, _whisper, mouse, options.RefuseWhisper) is { } whisper)
            options.RefuseWhisper = whisper;
        if (Toggle(renderer, _party, mouse, options.RefuseParty) is { } party)
            options.RefuseParty = party;
        if (Toggle(renderer, _fight, mouse, options.RefuseFight) is { } fight)
            options.RefuseFight = fight;
        if (Toggle(renderer, _trade, mouse, options.RefuseTrade) is { } trade)
        {
            options.RefuseTrade = trade;
            _chat.AddSystem(_game.Texts[trade ? TradeRefusedText : TradeAcceptedText], 0xff, 0xff, 0);
        }
        if (Toggle(renderer, _windowMode, mouse, options.WindowMode) is { } windowMode)
        {
            options.WindowMode = windowMode;
            _game.Display.SetFullscreen(!windowMode);                 // ✅ 0x00aa4440: DirectDraw again, windowed or full screen
        }

        Radios(renderer, _music, mouse, options.MusicLevel, level =>
        {
            options.MusicLevel = level;
            _game.Sounds.SetMusicLevel(level);
        });
        Radios(renderer, _effect, mouse, options.EffectLevel, level =>
        {
            options.EffectLevel = level;
            _game.Sounds.SetEffectLevel(level);
        });
        // ✅ "View My Item" toggles +0xb and sends a C2S 0x11b (0x00ae50c0). ❓ Its layout is in no capture and the emulator has no handler:
        // the choice is kept, the request waits for its feature.
        if (Toggle(renderer, _viewItem, mouse, options.ViewMyItem) is { } viewItem)
            options.ViewMyItem = viewItem;
        Radios(renderer, _simplified, mouse, options.Simplified, choice => options.Simplified = choice);
        // ✅ 0x00b18000: choosing the other language asks first (0x00b176d0, buttons +0x880/+0x8c0). ❓ That question and the change it makes
        // wait for the language switch; the mark shows the current language.
        Radios(renderer, _language, mouse, options.Language, _ => { });
    }

    /// <summary>✅ The window's key (the second vtable's <c>+4</c>): Escape closes it.</summary>
    public override void Key(GameKey key)
    {
        if (key == GameKey.Escape && Visible)
            Close();
    }

    private static bool DrawThenRead(IRenderer renderer, Button button, MouseState mouse)
    {
        button.Draw(renderer);
        button.Update(mouse);
        return button.Clicked;
    }

    /// <summary>✅ A check box: a click flips the value; the check (<c>dr2_sub_001_001</c>) is drawn on the box while it is on.</summary>
    private bool? Toggle(IRenderer renderer, Button box, MouseState mouse, bool value)
    {
        box.Update(mouse);
        bool? changed = box.Clicked ? !value : null;
        if (changed ?? value)
            DrawFrame(renderer, _check, box);
        return changed;
    }

    /// <summary>✅ A row of radio buttons: the mark (<c>dr2_sub_001_002</c>) on the chosen one, drawn before the click is handled.</summary>
    private void Radios(IRenderer renderer, Button[] radios, MouseState mouse, int chosen, Action<int> choose)
    {
        for (int i = 0; i < radios.Length; i++)
        {
            radios[i].Update(mouse);
            if (chosen == i)
                DrawFrame(renderer, _radio, radios[i]);
            if (radios[i].Clicked)
                choose(i);
        }
    }

    private static void DrawFrame(IRenderer renderer, ITexture[] frames, Button at)
    {
        if (frames.Length > 0)
            renderer.Draw(frames[0], at.X, at.Y);
    }

    /// <summary>
    /// ✅ "Location Sync" (<c>0x00b16bdb</c>): in the BATTLE ZONE a chat line (<c>0xef6</c>); in the field the request <c>0x00ae5b30</c>
    /// (C2S <c>0x78</c>), then a 5 minute wait shown as "m : ss" under the button. ❓ The request's layout is in no capture: it waits for its
    /// feature, so the button only shows the press. The battle case (<c>0xef5</c>) comes with the battle.
    /// </summary>
    private void LocationSync()
    {
        if (_game.IsBattleZone)
            _chat.AddSystem(_game.Texts[SyncInBattleZoneText], 0xff, 0xff, 0);
    }
}
