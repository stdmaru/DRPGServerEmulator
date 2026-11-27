using DRPG.Client.Features.Chat;
using DRPG.Client.Features.Exit;
using DRPG.Client.Features.Options;
using DRPG.Client.Net;
using DRPG.Client.Scenes;
using DRPG.Client.Ui;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Tests;

/// <summary>Moves, presses and releases the mouse the way the loop feeds it, one frame at a time.</summary>
internal static class MouseScript
{
    public static void Move(this MouseState mouse, int x, int y) => mouse.Handle(new MouseMoveEvent(x, y), 0);

    public static void Press(this MouseState mouse, int x, int y, ulong ms = 0) => mouse.Handle(new MouseButtonEvent(x, y, true), ms);

    public static void Release(this MouseState mouse, int x, int y, ulong ms = 0) => mouse.Handle(new MouseButtonEvent(x, y, false), ms);
}

public sealed class ButtonTests
{
    private static Button NewButton() => new([new FakeTexture(20, 10), new FakeTexture(20, 10), new FakeTexture(20, 10)], 100, 100);

    [Fact]
    public void A_press_and_release_inside_is_a_click_and_the_button_is_the_hot_control()
    {
        var mouse = new MouseState();
        var button = NewButton();
        mouse.Move(105, 105);
        button.Update(mouse);
        Assert.Equal(1, button.State);
        Assert.True(button.IsHot(mouse));
        mouse.EndFrame();

        mouse.Press(105, 105);
        button.Update(mouse);
        Assert.Equal(2, button.State);
        Assert.False(button.Clicked);
        mouse.EndFrame();

        mouse.Release(105, 105);
        button.Update(mouse);
        Assert.True(button.Clicked);
        mouse.EndFrame();
        button.Update(mouse);
        Assert.False(button.Clicked);
    }

    [Fact]
    public void Held_down_over_the_button_after_a_press_elsewhere_it_keeps_its_state_and_is_not_clicked()
    {
        var mouse = new MouseState();
        var button = NewButton();
        mouse.Press(10, 10);
        button.Update(mouse);
        mouse.EndFrame();
        mouse.Move(105, 105);
        button.Update(mouse);
        Assert.Equal(0, button.State);
        mouse.EndFrame();
        mouse.Release(105, 105);
        button.Update(mouse);
        Assert.False(button.Clicked);
        Assert.Equal(1, button.State);
    }

    [Fact]
    public void Released_outside_after_a_press_inside_is_not_a_click()
    {
        var mouse = new MouseState();
        var button = NewButton();
        mouse.Press(105, 105);
        button.Update(mouse);
        mouse.EndFrame();
        mouse.Release(300, 300);
        button.Update(mouse);
        Assert.False(button.Clicked);
        Assert.Equal(0, button.State);
    }

    [Fact]
    public void The_hot_control_is_cleared_after_the_frame()
    {
        var mouse = new MouseState();
        var button = NewButton();
        mouse.Move(105, 105);
        button.Update(mouse);
        mouse.EndFrame();
        Assert.Null(mouse.Hot);
        Assert.False(button.IsHot(mouse));
    }
}

public sealed class WindowManagerTests
{
    private sealed class TestWindow(int id) : GameWindow(id)
    {
        public int Frames { get; private set; }
        public List<GameKey> Keys { get; } = [];

        public void Place(int x, int y, int w, int h) => (X, Y, Width, Height) = (x, y, w, h);

        public override void Frame(IRenderer renderer, MouseState mouse, ulong nowMs) => Frames++;

        public override void Key(GameKey key) => Keys.Add(key);
    }

    [Fact]
    public void Opening_and_closing_play_the_window_sounds_and_the_opened_window_goes_on_top()
    {
        using var fixture = new SceneFixture();
        GameSceneSounds.Register(fixture.Sounds);
        var windows = fixture.Windows;
        var first = new TestWindow(1);
        var second = new TestWindow(2);
        windows.Register(first);
        windows.Register(second);

        windows.Open(1);
        Assert.True(windows.IsOpen(1));
        Assert.Equal(1, fixture.EffectsPlayed);
        Assert.Same(first, windows.Windows[^1]);

        windows.Open(1);                                                      // already open: nothing
        Assert.Equal(1, fixture.EffectsPlayed);

        windows.Close(1);
        Assert.False(windows.IsOpen(1));
        Assert.Equal(2, fixture.EffectsPlayed);
    }

    [Theory]
    [InlineData(0x55)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void An_id_without_a_window_does_nothing(int id)
    {
        using var fixture = new SceneFixture();
        GameSceneSounds.Register(fixture.Sounds);
        fixture.Windows.Open(id);
        fixture.Windows.Close(id);
        fixture.Windows.Toggle(id);
        Assert.False(fixture.Windows.IsOpen(id));
        Assert.Equal(0, fixture.EffectsPlayed);
    }

    [Fact]
    public void The_minimap_opens_and_closes_silently()
    {
        using var fixture = new SceneFixture();
        GameSceneSounds.Register(fixture.Sounds);
        fixture.Windows.Register(new TestWindow(WindowManager.MiniMapWindow));
        fixture.Windows.Open(WindowManager.MiniMapWindow);
        fixture.Windows.Close(WindowManager.MiniMapWindow);
        Assert.Equal(0, fixture.EffectsPlayed);
    }

    [Fact]
    public void A_window_registered_twice_is_refused()
    {
        using var fixture = new SceneFixture();
        fixture.Windows.Register(new TestWindow(3));
        Assert.Throws<InvalidOperationException>(() => fixture.Windows.Register(new TestWindow(3)));
    }

    [Fact]
    public void Escape_closes_the_topmost_window_it_may_close_and_skips_the_minimap()
    {
        using var fixture = new SceneFixture();
        var windows = fixture.Windows;
        var options = new TestWindow(0xf);
        var minimap = new TestWindow(WindowManager.MiniMapWindow);
        windows.Register(options);
        windows.Register(minimap);
        windows.Open(0xf);
        windows.Open(WindowManager.MiniMapWindow);                            // on top, but Escape never closes it

        Assert.True(windows.CloseTopmost());
        Assert.Equal([GameKey.Escape], options.Keys);
        Assert.False(windows.IsOpen(0xf));
        Assert.True(windows.IsOpen(WindowManager.MiniMapWindow));
        Assert.False(windows.CloseTopmost());
    }

    [Fact]
    public void While_locked_nothing_opens_and_only_the_exit_window_is_drawn()
    {
        using var fixture = new SceneFixture();
        var windows = fixture.Windows;
        var other = new TestWindow(1);
        var exit = new TestWindow(WindowManager.ExitWindow);
        windows.Register(other);
        windows.Register(exit);
        windows.Open(1);
        windows.Open(WindowManager.ExitWindow);
        windows.Locked = true;
        windows.Close(1);
        windows.Open(1);
        Assert.False(windows.IsOpen(1));

        windows.Open(1);
        windows.Frame(fixture.Renderer, fixture.Game.Mouse, 0);
        Assert.Equal(1, exit.Frames);
        Assert.Equal(0, other.Frames);
    }

    [Fact]
    public void A_visible_window_covers_the_ground_under_it_and_is_the_hot_control()
    {
        using var fixture = new SceneFixture();
        var window = new TestWindow(1);
        window.Place(100, 100, 50, 40);
        fixture.Windows.Register(window);
        Assert.False(fixture.Windows.Covers(120, 120));
        fixture.Windows.Open(1);
        Assert.True(fixture.Windows.Covers(120, 120));
        Assert.False(fixture.Windows.Covers(150, 120));                       // right edge excluded
        fixture.Game.Mouse.Move(120, 120);
        fixture.Windows.Frame(fixture.Renderer, fixture.Game.Mouse, 0);
        Assert.Same(window, fixture.Game.Mouse.Hot);
    }
}

public sealed class ExitWindowTests : IDisposable
{
    private readonly SceneFixture _fixture = NewFixture();
    private readonly WorldSession _session;

    private static SceneFixture NewFixture()
    {
        var fixture = new SceneFixture();
        GameSceneSounds.Register(fixture.Sounds);
        return fixture;
    }
    private readonly ExitWindow _exit;

    public ExitWindowTests()
    {
        _session = new WorldSession(ClientSettings.Local, _fixture.Game.Command, _fixture.Log.Add);
        var ui = new UiSprites(_fixture.Game);
        _exit = new ExitWindow(_fixture.Windows, new ExitActions(_session), _fixture.Game.Text, _fixture.Game.Texts,
            ui.Load("gui\\dr2_sub_007_005.spr"), ui.Gui("dr2_sub_007_001"));
        _fixture.Windows.Register(_exit);
    }

    private void Frame(ulong nowMs)
    {
        _fixture.Windows.Frame(_fixture.Renderer, _fixture.Game.Mouse, nowMs);
        _fixture.Game.Mouse.EndFrame();
    }

    private void Click(int x, int y, ulong nowMs)
    {
        _fixture.Game.Mouse.Press(x, y);
        Frame(nowMs);
        _fixture.Game.Mouse.Release(x, y);
        Frame(nowMs);
    }

    [Fact]
    public void It_opens_centred_with_close_and_cancel_where_the_original_puts_them()
    {
        _fixture.Windows.Open(WindowManager.ExitWindow);
        Assert.Equal((293, 242, 214, 117), (_exit.X, _exit.Y, _exit.Width, _exit.Height));
        Frame(0);

        var buttons = _fixture.Renderer.Draws.Where(d => (d.Texture.Width, d.Texture.Height) == (45, 23)).Select(d => (d.X, d.Y)).ToList();
        Assert.Equal([(293 + 0x34, 242 + 0x49), (293 + 0x73, 242 + 0x49)], buttons);
    }

    [Fact]
    public void Cancel_closes_it_without_a_sound()
    {
        _fixture.Windows.Open(WindowManager.ExitWindow);
        int sounds = _fixture.EffectsPlayed;
        Click(293 + 0x73 + 5, 242 + 0x49 + 5, 0);
        Assert.False(_exit.Visible);
        Assert.Equal(sounds, _fixture.EffectsPlayed);
    }

    [Fact]
    public void Close_locks_the_field_counts_ten_seconds_and_asks_to_leave()
    {
        _fixture.Windows.Open(WindowManager.ExitWindow);
        Click(293 + 0x34 + 5, 242 + 0x49 + 5, 1000);
        Assert.True(_exit.Counting);
        Assert.True(_fixture.Windows.Locked);

        Frame(1000 + 9_999);
        Assert.False(_exit.LeaveRequested);
        Frame(1000 + 10_000);
        Assert.True(_exit.LeaveRequested);
        Assert.False(_exit.Visible);
        Assert.False(_fixture.Windows.Locked);
    }

    [Fact]
    public void Cancel_during_the_count_unlocks_the_field_and_stays_in_the_world()
    {
        _fixture.Windows.Open(WindowManager.ExitWindow);
        Click(293 + 0x34 + 5, 242 + 0x49 + 5, 0);
        Click(293 + 0x50 + 5, 242 + 0x49 + 5, 3000);
        Assert.False(_exit.Visible);
        Assert.False(_fixture.Windows.Locked);
        Frame(20_000);
        Assert.False(_exit.LeaveRequested);
    }

    [Fact]
    public void Escape_closes_it()
    {
        _fixture.Windows.Open(WindowManager.ExitWindow);
        Assert.True(_fixture.Windows.CloseTopmost());
        Assert.False(_exit.Visible);
    }

    public void Dispose()
    {
        _session.Dispose();
        _fixture.Dispose();
    }
}

public sealed class OptionsWindowTests : IDisposable
{
    private readonly SceneFixture _fixture = NewFixture();

    private static SceneFixture NewFixture()
    {
        var fixture = new SceneFixture();
        GameSceneSounds.Register(fixture.Sounds);
        return fixture;
    }
    private readonly ChatLog _chat = new();
    private readonly OptionsWindow _options;

    public OptionsWindowTests()
    {
        _options = new OptionsWindow(_fixture.Game, new UiSprites(_fixture.Game), _fixture.Windows, _chat);
        _fixture.Windows.Register(_options);
        _fixture.Windows.Open(OptionsWindow.WindowId);
    }

    private void Click(int left, int top)
    {
        var mouse = _fixture.Game.Mouse;
        int x = _options.X + left + 2, y = _options.Y + top + 2;
        mouse.Move(x, y);
        _fixture.Windows.Frame(_fixture.Renderer, mouse, 0);
        mouse.EndFrame();
        mouse.Press(x, y);
        _fixture.Windows.Frame(_fixture.Renderer, mouse, 0);
        mouse.EndFrame();
        mouse.Release(x, y);
        _fixture.Windows.Frame(_fixture.Renderer, mouse, 0);
        mouse.EndFrame();
    }

    [Fact]
    public void It_sits_centred_twenty_pixels_above_the_middle()
    {
        Assert.Equal((294, 37, 212, 486), (_options.X, _options.Y, _options.Width, _options.Height));
    }

    [Fact]
    public void The_sprite_check_boxes_change_the_drawing_at_once()
    {
        Click(0x3c, 0x3b);
        Assert.False(_fixture.Game.SpriteOptions.Shadow);
        Click(0x3c, 0x49);
        Assert.False(_fixture.Game.SpriteOptions.Alpha);
        Click(0x3c, 0x59);
        Assert.False(_fixture.Game.SpriteOptions.Card);
        Click(0x3c, 0x3b);
        Assert.True(_fixture.Game.SpriteOptions.Shadow);
    }

    [Fact]
    public void The_trade_refusal_prints_its_line_in_the_chat()
    {
        Click(0x6e, 0xff);
        Assert.True(_fixture.Game.Options.RefuseTrade);
        var line = Assert.Single(_chat.Lines);
        Assert.Equal((0xff, 0xff, 0), (line.Red, line.Green, line.Blue));
        Click(0x6e, 0xff);
        Assert.False(_fixture.Game.Options.RefuseTrade);
        Assert.Equal(2, _chat.Lines.Count);
    }

    [Fact]
    public void The_volume_radios_set_the_levels()
    {
        Click(0x46 + 4 * 0x1e, 0x93);
        Assert.Equal(4, _fixture.Game.Options.MusicLevel);
        Click(0x46, 0xb3);
        Assert.Equal(0, _fixture.Game.Options.EffectLevel);
    }

    [Fact]
    public void Window_mode_switches_the_display_to_full_screen_and_back()
    {
        Click(0x1a, 0x3b);
        Assert.False(_fixture.Game.Options.WindowMode);
        Click(0x1a, 0x3b);
        Assert.Equal([true, false], _fixture.Display.Switches);
    }

    [Fact]
    public void The_close_button_closes_it_without_a_sound()
    {
        int sounds = _fixture.EffectsPlayed;
        Click(0xbe, 6);
        Assert.False(_options.Visible);
        Assert.Equal(sounds, _fixture.EffectsPlayed);
    }

    [Fact]
    public void Exit_game_opens_the_exit_window_and_closes_the_options()
    {
        using var session = new WorldSession(ClientSettings.Local, _fixture.Game.Command, _fixture.Log.Add);
        var ui = new UiSprites(_fixture.Game);
        var exit = new ExitWindow(_fixture.Windows, new ExitActions(session), _fixture.Game.Text, _fixture.Game.Texts,
            ui.Load("gui\\dr2_sub_007_005.spr"), ui.Gui("dr2_sub_007_001"));
        _fixture.Windows.Register(exit);
        Click(0x6f, 0x1c1);
        Assert.True(exit.Visible);
        Assert.False(_options.Visible);
    }

    [Fact]
    public void A_click_beside_every_control_changes_nothing()
    {
        var before = (_fixture.Game.SpriteOptions, _fixture.Game.Options.MusicLevel, _fixture.Game.Options.EffectLevel, _fixture.Game.Options.Simplified);
        Click(0x90, 0x30);
        Click(0x2, 0x1e0);
        Assert.Equal(before, (_fixture.Game.SpriteOptions, _fixture.Game.Options.MusicLevel, _fixture.Game.Options.EffectLevel, _fixture.Game.Options.Simplified));
        Assert.True(_options.Visible);
    }

    public void Dispose() => _fixture.Dispose();
}

public sealed class FieldHudInteractionTests
{
    private static HudValues Values(DRPG.Shared.Packets.Map.DigimonStatus? leader = null) =>
        new("Marcus", 1, 1, 5, "Net Ocean", 1, "Plains", 5, 6, leader, new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void The_options_menu_button_opens_and_closes_the_options_window()
    {
        using var fixture = new SceneFixture();
        var options = new OptionsWindow(fixture.Game, new UiSprites(fixture.Game), fixture.Windows, new ChatLog());
        fixture.Windows.Register(options);
        var hud = fixture.Hud();
        var mouse = fixture.Game.Mouse;

        void Click()
        {
            mouse.Press(546 + 5, 548 + 5);
            hud.Frame(fixture.Renderer, Values(), mouse);
            mouse.EndFrame();
            mouse.Release(546 + 5, 548 + 5);
            hud.Frame(fixture.Renderer, Values(), mouse);
            mouse.EndFrame();
        }

        Click();
        Assert.True(options.Visible);
        fixture.Renderer.Draws.Clear();
        hud.Frame(fixture.Renderer, Values(), mouse);
        var button = Assert.Single(fixture.Renderer.Draws, d => (d.Texture.Width, d.X, d.Y) == (29, 546, 548));
        Click();
        Assert.False(options.Visible);
    }

    [Fact]
    public void The_shop_tooltip_is_drawn_only_while_the_mouse_is_on_the_button()
    {
        using var fixture = new SceneFixture();
        fixture.Game.ChannelLine = 0;
        var hud = fixture.Hud();
        var mouse = fixture.Game.Mouse;
        hud.Frame(fixture.Renderer, Values(), mouse);
        Assert.DoesNotContain(fixture.Renderer.Draws, d => d.Texture.Width == 14 && d.Texture.Height == 14);

        mouse.Move(516 + 3, 462 + 3);
        fixture.Renderer.Draws.Clear();
        hud.Frame(fixture.Renderer, Values(), mouse);
        Assert.Contains(fixture.Renderer.Draws, d => d.Texture.Width == 14 && d.Texture.Height == 14 && d.Y == 462 - 0x1a);
    }
}

public sealed class FieldHudChatInteractionTests
{
    private static HudValues Values(int tamerClass = 1) =>
        new("Marcus", 1, tamerClass, 5, "Net Ocean", 1, "Plains", 5, 6, null, new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

    private static void Click(SceneFixture fixture, FieldHud hud, int x, int y, int tamerClass = 1)
    {
        var mouse = fixture.Game.Mouse;
        mouse.Move(x, y);
        hud.Frame(fixture.Renderer, Values(tamerClass), mouse);
        mouse.EndFrame();
        mouse.Press(x, y);
        hud.Frame(fixture.Renderer, Values(tamerClass), mouse);
        mouse.EndFrame();
        mouse.Release(x, y);
        hud.Frame(fixture.Renderer, Values(tamerClass), mouse);
        mouse.EndFrame();
    }

    [Fact]
    public void The_size_buttons_move_the_chat_frame_and_what_it_covers()
    {
        using var fixture = new SceneFixture();
        var hud = fixture.Hud();
        Assert.False(hud.Covers(100, 400));
        Click(fixture, hud, 428 + 3, 549 + 3);                                // max
        Assert.Equal(1, hud.ChatSize);
        Assert.True(hud.Covers(100, 400));
        fixture.Renderer.Draws.Clear();
        hud.Frame(fixture.Renderer, Values(), fixture.Game.Mouse);
        Assert.Equal((28, 0x160), Assert.Single(fixture.Renderer.Draws, d => d.Texture.Width == 409) is var f ? (f.X, f.Y) : default);
        Click(fixture, hud, 398 + 3, 549 + 3);                                // min
        Assert.Equal(2, hud.ChatSize);
        Assert.False(hud.Covers(100, 470));
        Assert.True(hud.Covers(100, 492));
    }

    [Fact]
    public void The_battle_tab_is_chosen_and_the_watch_tab_cannot_be()
    {
        using var fixture = new SceneFixture();
        var hud = fixture.Hud();
        Click(fixture, hud, 317 + 3, 574 + 3);
        Assert.Equal(1, hud.ChatTab);
        Click(fixture, hud, 355 + 3, 574 + 3);                                // where the hidden tab would be
        Assert.Equal(1, hud.ChatTab);
        Click(fixture, hud, 280 + 3, 574 + 3);
        Assert.Equal(0, hud.ChatTab);
    }

    [Theory]
    [InlineData(1, 0)]                                                         // class 1: Shout is disabled
    [InlineData(5, 3)]
    public void Shout_needs_class_5(int tamerClass, int mode)
    {
        using var fixture = new SceneFixture();
        var hud = fixture.Hud();
        Click(fixture, hud, 157 + 3, 572 + 3, tamerClass);
        Assert.Equal(mode, hud.ChatMode);
    }
}

public sealed class GameOptionsSaveTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("drpg-options-").FullName;

    [Fact]
    public void The_options_are_written_back_with_the_original_keys_and_the_rest_of_the_file_is_kept()
    {
        var path = Path.Combine(_dir, "option.ini");
        File.WriteAllText(path, "[SPRITE]\r\nalpha=1\r\nanti=1\r\nshadow=1\r\ncard=1\r\nOptimization=0\r\n[BGM]\r\nbgm=0\r\nsound=0\r\n"
            + "[GAME]\r\nwhisper=0\r\nparty=0\r\npvp=0\r\ntrade=0\r\n[PATCH]\r\nmode=1\r\n[Member]\r\nid=1\r\n[LANGUAGE]\r\nlanguage=0\r\n");
        var options = GameOptions.From(DRPG.Engine.Assets.IniFile.Read(path));
        options.MusicLevel = 4;
        options.EffectLevel = 2;
        options.RefuseTrade = true;
        options.Simplified = 2;
        options.WindowMode = false;                                            // ✅ not written by 0x00b429f0

        options.Save(path, SpriteOptions.Default with { Shadow = false });

        var saved = DRPG.Engine.Assets.IniFile.Read(path);
        Assert.Equal("0", saved["sprite.shadow"]);
        Assert.Equal("2", saved["sprite.optimization"]);
        Assert.Equal("4", saved["bgm.bgm"]);
        Assert.Equal("2", saved["bgm.sound"]);
        Assert.Equal("1", saved["game.trade"]);
        Assert.Equal("1", saved["patch.mode"]);
        Assert.Equal("1", saved["member.id"]);
        Assert.False(saved.ContainsKey("sprite.windowmode"));
        var reread = GameOptions.From(saved);
        Assert.Equal((4, 2, true, 2), (reread.MusicLevel, reread.EffectLevel, reread.RefuseTrade, reread.Simplified));
    }

    [Fact]
    public void The_twelve_keys_follow_the_original_order()
    {
        var keys = GameOptions.From(new Dictionary<string, string>()).Entries(SpriteOptions.Default).Select(e => $"{e.Section}.{e.Key}");
        Assert.Equal(["SPRITE.alpha", "SPRITE.anti", "SPRITE.shadow", "SPRITE.card", "SPRITE.Optimization", "BGM.bgm", "GAME.whisper",
            "GAME.party", "GAME.pvp", "GAME.trade", "BGM.sound", "LANGUAGE.language"], keys);
    }

    [Fact]
    public void A_missing_file_is_created_with_the_sections()
    {
        var path = Path.Combine(_dir, "new.ini");
        GameOptions.From(new Dictionary<string, string>()).Save(path, SpriteOptions.Default);
        Assert.Equal("1", DRPG.Engine.Assets.IniFile.Read(path)["bgm.bgm"]);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
