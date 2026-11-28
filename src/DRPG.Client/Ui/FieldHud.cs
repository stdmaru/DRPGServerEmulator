using DRPG.Client.Scenes;
using DRPG.Client.Text;
using DRPG.Engine.Rendering;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Formats.Sprite;
using DRPG.Shared.Packets.Map;

namespace DRPG.Client.Ui;

/// <summary>What the basic HUD shows (the real values of the <c>0x02</c> packet and of the map).</summary>
public sealed record HudValues(
    string TamerName, int TamerId, int TamerClass, int TamerLevel, string ChannelName, int MapId, string MapName, int CellX, int CellY,
    DigimonStatus? Leader, DateTime ClockUtc, bool MiniMapOpen = false);

/// <summary>
/// The basic UI of the field (✅ v322 main bar <c>0x00b8a850</c>, docs/cliente/cena-jogo.md#ui-do-campo-hud): the tamer panel in the
/// top left corner and the bar at the bottom, with the digimon's gauges, the menu buttons, the side buttons and the chat frame. The
/// original draws and reads the mouse in the same pass, in this order, and draws each tooltip the moment its control is hot. The menu
/// buttons open their window through <see cref="WindowManager"/> (a window the client does not have yet does nothing); the side buttons
/// only show the press (their windows and requests come with their features). ❓ Not drawn yet (they come with their own step): the
/// teleport buttons, the quest panels, the EXP bonus box, the bell and the W/E icons.
/// </summary>
public sealed class FieldHud(GameContext game, UiSprites sprites, WindowManager windows, Tooltips tooltips)
{
    /// <summary>✅ The text colours are COLORREF: <c>0x80ffff</c> is a light yellow.</summary>
    public const uint Yellow = 0x80FFFF;

    /// <summary>✅ <c>TextAttach 0x1118</c>.</summary>
    public const int ClockText = 0x1118;
    public const string ClockFallback = "PST : %2d:%02d:%02d %d-%d-%d";

    /// <summary>✅ The maps where the tamer panel shows only the name of the map, without the cell (<c>0x00b8a964</c>).</summary>
    public static bool HidesCell(int map) =>
        map is 0x33 or 0x34 or 0x35 or 0x6a or 0x6c or 0x6d or 0x75 or 0x77 or 0x81 or 0x82 or 0x85
            or (>= 0x87 and <= 0x95) or (>= 0x98 and <= 0x9c);

    /// <summary>✅ <c>0x007e35e0</c>: the width of a gauge is <c>(width of frame 0 − 1) · percent / 100 + 1</c>.</summary>
    public static int GaugeWidth(int frameWidth, int percent) => (frameWidth - 1) * Math.Clamp(percent, 0, 100) / 100 + 1;

    /// <summary>✅ <c>0x00b31c00</c>: current / maximum · 100, 0 when the maximum is 0 (❓ truncated).</summary>
    public static int Percent(long current, long maximum) =>
        maximum <= 0 ? 0 : (int)Math.Clamp(current * 100 / maximum, 0, 100);

    // ✅ the buttons of the menu (dr2_main_000_010..017) and where they sit
    private static readonly (int Sprite, int X, int Y)[] MenuButtons =
    [
        (10, 450, 515), (11, 482, 515), (12, 546, 514), (13, 546, 548), (14, 514, 515), (15, 514, 548), (16, 482, 548), (17, 450, 548),
    ];

    /// <summary>✅ <c>0x00b91720</c>: the window each menu button opens and closes, in the order of <see cref="MenuButtons"/>.</summary>
    public static readonly int[] MenuWindows = [1, 3, 4, 0xf, 2, 0xcc, 0xa8, 0xb];

    /// <summary>✅ <c>0x00b91720</c> case 0: the first menu button closes window 2 before its own.</summary>
    private const int ClosedByFirstMenuButton = 2;

    /// <summary>✅ Frame 3 for state 0 while the button's window is closed, 0 while it is open.</summary>
    private const int ClosedMenuFrame = 3, OpenMenuFrame = 0;

    // ✅ the side buttons in the order the bar reads them (0x00b8d4..): SHOP (+0x1cd8), DEPOSITORY (+0x773f8), SEAL (+0x775e0), WORLD (+0x1d18)
    private static readonly (int Sprite, int X, int Y)[] SideButtons = [(32, 516, 462), (41, 516, 485), (42, 516, 439), (31, 451, 485)];
    private const int Shop = 0, Depository = 1, World = 3;

    /// <summary>✅ The texts of the side tooltips (<c>0x00b8d6aa</c>…): REWARD, WORLD, SHOP, DEPOSITORY.</summary>
    public const int RewardHint = 0x886, WorldHint = 0x888, ShopHint = 0x10ee, DepositoryHint = 0x887;

    /// <summary>✅ The box of every side tooltip is 7 px high: no middle row (<c>(7 − 28) / 14 + 1 = 0</c>).</summary>
    private const int HintBoxHeight = 7;

    /// <summary>✅ <c>0x00b8cf05</c>…: the gauge tooltips, and the EXP one (<c>TextAttach 0x885</c>), 12 px above the gauge.</summary>
    public const string HpHint = "HP %d/%d", VpHint = "VP %d/%d", EvpHint = "EVP %d/%d";
    public const int ExpHint = 0x885;
    private const int GaugeHintGap = 12;

    // ✅ the chat mode buttons (000_023..027), mode 0 = 000_025 shows frame 0, and the tabs 000_037..040
    private static readonly (int Sprite, int X, int Y)[] ChatButtons = [(23, 179, 572), (24, 201, 572), (25, 222, 572), (26, 244, 572), (27, 157, 572)];
    private static readonly (int Sprite, int X, int Y)[] ChatTabs = [(37, 280, 574), (38, 317, 574), (39, 355, 574), (40, 391, 574)];

    /// <summary>✅ <c>0x00b91a80</c>, case 2: the third tab ("Watch", <c>000_039</c>) is never drawn.</summary>
    private const int HiddenChatTab = 2;

    /// <summary>
    /// ✅ The chat size buttons (<c>0x00b8e2a0</c>, set up in <c>0x00b90570</c>): frames 2, 0 and 1 of <c>dr2_main_000_001</c> (13×13) at
    /// x = 398, 400 + w(2) and that + w(0) + 2, y = 549; drawn plain, last of the bar.
    /// </summary>
    private const int ChatSizeSprite = 1, ChatSizeY = 0x225, ChatSizeFirstX = 0x18e, ChatSizeSecondX = 0x190, ChatSizeGap = 2;

    private const int ChatX = 28, ChatY = 460, ChatWidth = 409, ChatHeight = 81;
    private const int BarY = 495;

    /// <summary>
    /// ✅ <c>0x00b91ec0</c> + bottom bar (<c>y &gt;= 495</c>): true when the point hits the HUD so a ground click is ignored. The chat's
    /// top follows its size; ❓ its height is the frame's drawn height (the sprite 0x00b91ec0 measures is not read).
    /// </summary>
    public bool Covers(int x, int y)
    {
        var (top, height) = ChatArea(ChatSize, Gui(18) is { Length: > 0 } chat ? chat[0].Height : ChatHeight);
        return y >= BarY || (x >= ChatX && x < ChatX + ChatWidth && y >= top && y < top + height);
    }

    /// <summary>
    /// ✅ The alphas (of 32) of the two blends of the bar with the <c>alpha</c> option on: the bar's frame 1 (<c>0x00b569f0(1, …, −1, 0x10, …)</c>)
    /// and the chat's background (<c>0x00b569f0(0, …, −1, 0x14, …, 81, 0)</c>, <c>0x00b8e6a0</c>).
    /// </summary>
    public const int BarGlowAlpha = 0x10, ChatAlpha = 0x14;

    /// <summary>✅ The class icons, 8 frames of 14×16 (<c>0x00a164f3</c> loads them into <c>+0x2698</c>).</summary>
    private const string TamerClassSprite = "interface\\draw_tamer_class.spr";

    /// <summary>✅ The class icon's corner (<c>0x00a14910(+0x12df, 33, 20)</c>).</summary>
    private const int TamerClassX = 33, TamerClassY = 20;

    /// <summary>✅ <c>0x00b231b0</c>: the portrait loaded when a species' own one is missing.</summary>
    private const string FallbackIcon = "sprite\\icon_065.spr";

    private const string RewardSprite = "gui\\dr2_sub_016_005.spr";
    private const int RewardX = 451, RewardY = 462;

    private Button[]? _menu, _side;
    private Button? _reward;

    /// <summary>
    /// ✅ <c>0x00b8a850</c>: draws the bar and reads its buttons for this frame. Call it once per frame, after the field and before the
    /// windows.
    /// </summary>
    public void Frame(IRenderer renderer, HudValues v, MouseState mouse)
    {
        CreateButtons();
        DrawBar(renderer);
        DrawTamerPanel(renderer, v);
        DrawDigimonPanel(renderer, v.Leader, mouse);
        DrawSideButtons(renderer, mouse);
        DrawTeleportButtons(renderer, mouse, v);
        DrawMenuHints(renderer);
        TamerClass = (byte)v.TamerClass;
        DrawChat(renderer, mouse);
        DrawChatSizeButtons(renderer, mouse);
    }

    /// <summary>✅ <c>0x00b90570</c>: the buttons and where they sit (<c>0x009b7570</c>, frame map 0, 1, 2, 3).</summary>
    private void CreateButtons()
    {
        if (_menu is not null)
            return;
        _menu = [.. MenuButtons.Select(b => new Button(Gui(b.Sprite), b.X, b.Y))];
        _side = [.. SideButtons.Select(b => new Button(Gui(b.Sprite), b.X, b.Y))];
        _reward = new Button(Load(RewardSprite, gui: true), RewardX, RewardY);
    }

    /// <summary>
    /// ✅ <c>0x00b8e2a0</c>, the last call of <c>0x00b8a850</c>: the three size buttons (normal, max, min) read the mouse, the hot one shows
    /// its name (<c>0x007de1b0</c>, mixed at 16/32) and a click sets the size; then they are drawn with frames 0, 1 and 2 whatever their
    /// state. ✅ <c>0x00b90570</c>: min (frame 2) at x = 398, normal at 400 + its width, max 2 px after that, y = 549.
    /// </summary>
    private void DrawChatSizeButtons(IRenderer renderer, MouseState mouse)
    {
        var frames = Gui(ChatSizeSprite);
        if (frames.Length < 3)
            return;
        if (_chatSizes is null)
        {
            int normalX = ChatSizeSecondX + frames[2].Width;
            _chatSizes = [new Button(frames, normalX, ChatSizeY), new Button(frames, normalX + frames[0].Width + ChatSizeGap, ChatSizeY),
                new Button(frames, ChatSizeFirstX, ChatSizeY)];
        }
        for (int i = 0; i < _chatSizes.Length; i++)
        {
            var button = _chatSizes[i];
            button.Update(mouse);
            if (button.IsHot(mouse))
                tooltips.DrawPanel(renderer, game.SpriteOptions, game.Texts[ChatSizeHints[i]], button.X, button.Y - ChatSizeHintUp,
                    alpha: SizeHintAlpha);
            if (button.Clicked)
                ChatSize = i;
        }
        for (int i = 0; i < _chatSizes.Length; i++)
            _chatSizes[i].DrawFrame(renderer, i);
    }

    private const int SizeHintAlpha = 0x10;

    /// <summary>
    /// ✅ 1. the bar's background (<c>0x00b90520</c>): frame 0 at (0, 495), then frame 1 at the same place, blended at
    /// <see cref="BarGlowAlpha"/> with the <c>alpha</c> option and drawn plain without it.
    /// </summary>
    private void DrawBar(IRenderer renderer)
    {
        var bar = Gui(0);
        Frame(renderer, bar, 0, 0, 495);
        if (bar.Length < 2)
            return;
        game.SpriteOptions.DrawBlended(renderer, bar[1], 0, 495, BarGlowAlpha);
    }

    /// <summary>✅ 2. the panel of the tamer.</summary>
    private void DrawTamerPanel(IRenderer renderer, HudValues v)
    {
        var text = game.Text;
        Frame(renderer, Load("interface\\w_unitframe.spr"), 0, 0, 0);
        Portrait(renderer, v.TamerId, 1, 2, 2);                                               // ✅ 0x00b1ec40(tamer) frame 1 at (2, 2)
        text.Draw(renderer, v.TamerName, 33, 4, Yellow);
        DrawTamerClass(renderer, v.TamerClass);
        text.Draw(renderer, "Level ", 50, 22, UiText.White, shadow: false);
        text.Draw(renderer, v.TamerLevel.ToString(), 90, 22, Yellow, shadow: false);
        text.Draw(renderer, GameContext.VersionText, 130, 1);
        text.Draw(renderer, v.ChannelName, 130, 14);                                          // ❓ the name of the server (the channel text, provisional)

        var clock = v.ClockUtc.AddHours(-7);                                                  // ✅ UTC − 7 h ("PST")
        var format = game.Texts.Contains(ClockText) ? game.Texts[ClockText] : ClockFallback;
        var clockText = PrintfFormat.Format(format, clock.Hour, clock.Minute, clock.Second, clock.Year, clock.Month, clock.Day);
        text.Draw(renderer, clockText, 640 - text.Measure(renderer, clockText).Width, 1);

        var place = HidesCell(v.MapId) ? v.MapName : $"{v.MapName} {PrintfFormat.Format("(%d,%d)", v.CellX, v.CellY)}";
        text.Draw(renderer, place, 130, 27);
    }

    /// <summary>
    /// ✅ 4. the digimon's panel: portrait, level, the HP, VP and EVP gauges, the menu buttons (<c>0x00b91720</c>, between EVP and EXP)
    /// and the EXP gauge; each gauge draws its tooltip at once when the mouse is on it. Without a leader only the menu buttons.
    /// </summary>
    private void DrawDigimonPanel(IRenderer renderer, DigimonStatus? leader, MouseState mouse)
    {
        if (leader is null || leader.IsEmpty)
        {
            DrawMenuButtons(renderer, mouse);
            return;
        }
        Portrait(renderer, leader.SpeciesId, 0, 594, 509);                                    // ✅ 0x00b1ec40(species) frame 0
        game.Text.Draw(renderer, $"Lv.{leader.Level}", 670, 508);
        Gauge(renderer, mouse, HpGauge, HpGauge, 692, 525, leader.Hp, leader.MaxHp, PrintfFormat.Format(HpHint, leader.Hp, leader.MaxHp));
        Gauge(renderer, mouse, VpGauge, HpGauge, 692, 545, leader.Vp, leader.MaxVp, PrintfFormat.Format(VpHint, leader.Vp, leader.MaxVp));
        Gauge(renderer, mouse, EvpGauge, HpGauge, 692, 564, leader.Evp, leader.MaxEvp,
            PrintfFormat.Format(EvpHint, leader.Evp, leader.MaxEvp));
        DrawMenuButtons(renderer, mouse);
        Gauge(renderer, mouse, ExpGauge, ExpGauge, 488, 585, leader.Exp, leader.ExpToNextLevel, PrintfFormat.Format(game.Texts[ExpHint],
            leader.Exp, leader.ExpToNextLevel, ExpPercent(leader.Exp, leader.ExpToNextLevel)));
    }

    /// <summary>
    /// ✅ <c>0x00b31c00</c> as the EXP tooltip prints it: <c>(float)current / (float)maximum · 100</c> of the low 32 bits the call gets,
    /// 0 when the maximum is 0.
    /// </summary>
    public static float ExpPercent(long current, long maximum)
    {
        int low = unchecked((int)current), lowMaximum = unchecked((int)maximum);
        return lowMaximum == 0 ? 0f : (float)low / lowMaximum * 100f;
    }

    /// <summary>
    /// ✅ <c>0x00b91720</c>: each menu button reads the mouse with frame 3 for state 0 while its window is closed (0 while open), and
    /// a click closes or opens its window. ❓ The battle zone message of the sixth button (0x89e) and its window's load wait for those
    /// windows.
    /// </summary>
    private void DrawMenuButtons(IRenderer renderer, MouseState mouse)
    {
        for (int i = 0; i < _menu!.Length; i++)
        {
            var button = _menu[i];
            button.Update(mouse);
            button.SetFrame(0, windows.IsOpen(MenuWindows[i]) ? OpenMenuFrame : ClosedMenuFrame);
            button.Draw(renderer);
        }
        for (int i = 0; i < _menu.Length; i++)
        {
            if (!_menu[i].Clicked)
                continue;
            if (i == 0)
                windows.Close(ClosedByFirstMenuButton);
            windows.Toggle(MenuWindows[i]);
        }
    }

    /// <summary>
    /// ✅ <c>0x00a14910</c>: the class (the low byte of <c>0x02</c>+0x2a, kept in <c>+0x12df</c>) is frame <c>class − 1</c> of
    /// <see cref="TamerClassSprite"/>; 0 draws nothing, and a frame the sprite lacks draws nothing either (<c>0x00b56ff0</c>).
    /// </summary>
    private void DrawTamerClass(IRenderer renderer, int tamerClass)
    {
        int frame = (byte)tamerClass - 1;
        if (frame < 0)
            return;
        Frame(renderer, Load(TamerClassSprite), frame, TamerClassX, TamerClassY);
    }

    /// <summary>
    /// ✅ <c>0x00b1ec40</c>: the portrait <c>Sprite\icon_%03d.spr</c> of <see cref="Data.SpeciesTables.IconId"/>; a missing file is
    /// <c>icon_065</c> (✅ <c>0x00b231b0</c>).
    /// </summary>
    private void Portrait(IRenderer renderer, int id, int frame, int x, int y)
    {
        if (Data.SpeciesTables.IconId(id) is not { } icon)
            return;
        var path = $"sprite\\icon_{icon:000}.spr";
        var frames = game.Resource.Contains(path) ? Load(path) : Load(FallbackIcon);
        Frame(renderer, frames, frame, x, y);
    }

    // ✅ the gauges' sprites (dr2_main_000_0NN): HP +0x1ac, VP +0x200, EVP +0x254, EXP +0x2a8
    private const int HpGauge = 2, VpGauge = 5, EvpGauge = 4, ExpGauge = 3;

    /// <summary>
    /// ✅ <c>0x007e3560</c>: frame 0 clipped to the gauge's width, then frame 1 whole over it. ✅ With the mouse on the gauge
    /// (<c>0x00b31aa0</c>: x ≤ mouse ≤ x + w and y ≤ mouse ≤ y + h, edges included) the tooltip at (x, y − h − 12). ✅ w and h are
    /// frame 0 of <paramref name="sizeSprite"/>: HP, VP and EVP all measure the HP gauge's sprite (<c>+0x1b0</c>, <c>0x00b8d05a</c>,
    /// <c>0x00b8d16c</c>), EXP its own (<c>+0x2ac</c>).
    /// </summary>
    private void Gauge(IRenderer renderer, MouseState mouse, int sprite, int sizeSprite, int x, int y, long current, long maximum,
        string hint)
    {
        var frames = Gui(sprite);
        if (frames.Length == 0)
            return;
        renderer.DrawPart(frames[0], x, y, 0, Math.Min(frames[0].Width, GaugeWidth(frames[0].Width, Percent(current, maximum))));
        Frame(renderer, frames, 1, x, y);
        var size = Gui(sizeSprite);
        if (size.Length == 0)
            return;
        int width = size[0].Width, height = size[0].Height;
        if (mouse.X >= x && mouse.X <= x + width && mouse.Y >= y && mouse.Y <= y + height)
            tooltips.DrawPanel(renderer, game.SpriteOptions, hint, x, y - height - GaugeHintGap);
    }

    /// <summary>
    /// ✅ 5. REWARD (<c>GUI\dr2_sub_016_005</c>, not in the BATTLE ZONE channels, <c>0x00b8d399</c>), then SHOP, DEPOSITORY, SEAL and
    /// WORLD, each read and drawn by state; then the tooltip of the hot one among REWARD, WORLD, SHOP and DEPOSITORY (<c>0x00b8d6aa</c>).
    /// ✅ WORLD's tooltip sits on SHOP's corner: the hot test is on <c>+0x1d18</c> but the box and text read <c>+0x1ce0/+0x1ce4</c>
    /// (SHOP, <c>0x00b8d7a1</c>).
    /// ❓ The original blinks REWARD when there is a reward (+0x7743c: who sets it is not read); the clicks open windows and send
    /// requests the client does not have yet, so they only show the press.
    /// </summary>
    private void DrawSideButtons(IRenderer renderer, MouseState mouse)
    {
        if (game.IsBattleZone)
            return;
        _reward!.Update(mouse);
        _reward.Draw(renderer);
        foreach (var button in _side!)
        {
            button.Update(mouse);
            button.Draw(renderer);
        }

        if (_reward.IsHot(mouse))
            SizedHint(renderer, RewardHint, _reward, 0x1f, 0x17);
        else if (_side[World].IsHot(mouse))
            FixedHint(renderer, WorldHint, _side[Shop], 0x32, 0x1b, 0xc8, 0x28, 0x12);
        else if (_side[Shop].IsHot(mouse))
            SizedHint(renderer, ShopHint, _side[Shop], 0x1a, 0x12);
        else if (_side[Depository].IsHot(mouse))
            FixedHint(renderer, DepositoryHint, _side[Depository], 0x1e, 0x1a, 0x12c, 0x14, 0x12);
    }

    /// <summary>
    /// ✅ A side tooltip sized on its text (REWARD, SHOP): box at (x − w/3 − 10, y − <paramref name="boxUp"/>), w + 10 wide; the text at
    /// (x − w/3, y − <paramref name="textUp"/>).
    /// </summary>
    private void SizedHint(IRenderer renderer, int textId, Button button, int boxUp, int textUp)
    {
        var text = game.Texts[textId];
        int third = tooltips.Measure(renderer, text) / 3;
        tooltips.DrawBox(renderer, button.X - third - 10, button.Y - boxUp, tooltips.Measure(renderer, text) + 10, HintBoxHeight);
        tooltips.DrawBoxText(renderer, text, button.X - third, button.Y - textUp);
    }

    /// <summary>✅ A side tooltip of fixed width (WORLD, DEPOSITORY): box and text at fixed offsets from <paramref name="anchor"/>.</summary>
    private void FixedHint(IRenderer renderer, int textId, Button anchor, int boxLeft, int boxUp, int boxWidth, int textLeft, int textUp)
    {
        tooltips.DrawBox(renderer, anchor.X - boxLeft, anchor.Y - boxUp, boxWidth, HintBoxHeight);
        tooltips.DrawBoxText(renderer, game.Texts[textId], anchor.X - textLeft, anchor.Y - textUp);
    }

    /// <summary>
    /// ✅ The three round buttons left of the minimap (<c>0x00b8db44</c>, <c>GUI\TeleportEasy_Icon24.spr</c>, set in <c>0x00b91610</c>):
    /// frames 0, 1 and 2 whatever their state (<c>0x009b71e0</c>), only while the minimap shows its picture, outside the BATTLE ZONE and
    /// the maps 0x58, 0x76, 0x78..0x7b. ❓ A click uses a teleport item (<c>0x238</c>, <c>0x23a..0x23c</c>): it comes with the inventory.
    /// </summary>
    private void DrawTeleportButtons(IRenderer renderer, MouseState mouse, HudValues v)
    {
        if (!v.MiniMapOpen || game.IsBattleZone || v.MapId is 0x58 or 0x76 or (>= 0x78 and <= 0x7b))
            return;
        _teleport ??= [.. TeleportButtonYs.Select(y => new Button(Load(TeleportSprite), TeleportX, y))];
        for (int i = 0; i < _teleport.Length; i++)
        {
            _teleport[i].Update(mouse);
            _teleport[i].DrawFrame(renderer, i);
        }
    }

    /// <summary>
    /// ✅ <c>0x00b8e220</c>, after the side and teleport buttons and before the chat tabs: each menu button whose state is not 0 (the
    /// mouse inside, <c>+0x20 &gt; 0</c>; the hot control is not asked) shows its name (<c>0x007de3f0</c>, the wide <c>0x007de1b0</c>)
    /// at (x, y − 20), 3 px of margin, mixed at 16/32. ✅ The names are texts <c>0x7db + i</c>, loaded by <c>0x00b90570</c> into
    /// <c>+0x771a0</c> (a wide string every 0x18 bytes); an empty text draws nothing.
    /// </summary>
    private void DrawMenuHints(IRenderer renderer)
    {
        for (int i = 0; i < _menu!.Length; i++)
        {
            var button = _menu[i];
            var text = game.Texts[FirstMenuHint + i];
            if (button.State <= 0 || text.Length == 0)
                continue;
            tooltips.DrawPanel(renderer, game.SpriteOptions, text, button.X, button.Y - MenuHintUp, alpha: MenuHintAlpha);
        }
    }

    /// <summary>✅ <c>0x00b90570</c> / <c>0x00b8e220</c>: the first menu name, how far above the button and the mix.</summary>
    public const int FirstMenuHint = 0x7db;
    private const int MenuHintUp = 0x14, MenuHintAlpha = 0x10;

    /// <summary>✅ <c>0x00b91610</c>: the teleport buttons at (615, 65), (615, 92), (615, 119).</summary>
    private const string TeleportSprite = "gui\\TeleportEasy_Icon24.spr";
    private const int TeleportX = 0x267;
    private static readonly int[] TeleportButtonYs = [0x41, 0x5c, 0x77];
    private Button[]? _teleport;

    /// <summary>✅ <c>+0x4b7b0</c>: the chat tab shown, 0 General or 1 Battle (<c>0x00b91a80</c>).</summary>
    public int ChatTab { get; private set; }

    /// <summary>✅ <c>+0x770a4</c>: the chat's size, 0 normal, 1 max, 2 min (<c>0x00b8e2a0</c>).</summary>
    public int ChatSize { get; private set; }

    /// <summary>✅ <c>+0x270c</c>: the chat mode, 0 General, 1 Whisper, 2 Party, 3 Shout, 4 Guild (<c>0x00b8e6a0</c>).</summary>
    public int ChatMode { get; private set; }

    /// <summary>✅ <c>0x00b91a80</c>: the window the "Setting" tab opens.</summary>
    public const int ChatSettingWindow = 0x10;

    /// <summary>
    /// ✅ The chat (<c>0x00b91a80</c> tabs, then <c>0x00b8e6a0</c>: the frame and the mode buttons). ❓ The lines, the scroll bar and
    /// the input box of <c>0x00b8e6a0</c> come with the chat packets.
    /// </summary>
    private void DrawChat(IRenderer renderer, MouseState mouse)
    {
        DrawChatTabs(renderer, mouse);
        DrawChatFrame(renderer);
        DrawChatModeButtons(renderer, mouse);
    }

    /// <summary>
    /// ✅ <c>0x00b91a80</c>: General and Battle draw frame 2 while they are the tab shown and 0 otherwise, "Setting" frame 2 while its
    /// window (0x10) is open; the third tab ("Watch Battle") is never read nor drawn. The frame does not follow the mouse
    /// (<c>0x009b71e0</c>). A click shows General or Battle, or opens the setting window.
    /// </summary>
    private void DrawChatTabs(IRenderer renderer, MouseState mouse)
    {
        _chatTabs ??= [.. ChatTabs.Select(t => new Button(Gui(t.Sprite), t.X, t.Y))];
        for (int i = 0; i < _chatTabs.Length; i++)
        {
            if (i == HiddenChatTab)
                continue;
            int frame = i switch
            {
                0 => ChatTab == 0 ? 2 : 0,
                1 => ChatTab == 1 ? 2 : 0,
                _ => windows.IsOpen(ChatSettingWindow) ? 2 : 0,
            };
            _chatTabs[i].Update(mouse);
            _chatTabs[i].DrawFrame(renderer, frame);
            if (!_chatTabs[i].Clicked)
                continue;
            switch (i)
            {
                case 0:
                    ChatTab = 0;
                    break;
                case 1:
                    ChatTab = 1;
                    break;
                default:
                    windows.Open(ChatSettingWindow);
                    break;
            }
        }
    }

    /// <summary>
    /// ✅ <c>0x00b8f04d</c>: the chat's frame at x = 28 by size: (460, 81 px high), (352, whole) or (491, 50 px high), mixed at
    /// <see cref="ChatAlpha"/> with the <c>alpha</c> option.
    /// </summary>
    private void DrawChatFrame(IRenderer renderer)
    {
        if (Gui(18) is not { Length: > 0 } chat)
            return;
        var (top, height) = ChatArea(ChatSize, chat[0].Height);
        game.SpriteOptions.DrawRegionBlended(renderer, chat[0], ChatX, top, chat[0].Width, height, ChatAlpha);
    }

    /// <summary>✅ The chat frame's top and height for a size (<c>0x00b8f04d</c>; the top is also <c>0x00b91ec0</c>'s).</summary>
    public static (int Top, int Height) ChatArea(int size, int frameHeight) => size switch
    {
        1 => (MaxChatY, frameHeight),
        2 => (MinChatY, Math.Min(frameHeight, MinChatHeight)),
        _ => (ChatY, Math.Min(frameHeight, ChatHeight)),
    };

    private const int MaxChatY = 0x160, MinChatY = 0x1eb, MinChatHeight = 0x32;

    /// <summary>
    /// ✅ <c>0x00b8fd80</c>: the five mode buttons, each frame 3 for state 0 and 0 on the current mode. Shout (<c>000_027</c>) reads the
    /// mouse only for a GM, a tamer class of 5 or more, or in the BATTLE ZONE; otherwise it is disabled (<c>0x009b7660</c>). A click
    /// chooses the mode; a hot button shows its name in a message box whose corner is 30 px above it (<c>0x007dd610(…, x, y − 30, 1,
    /// 0xffffff)</c>: mode 1 does not centre). ❓ The click also focuses the
    /// input box, which comes with the chat packets.
    /// </summary>
    private void DrawChatModeButtons(IRenderer renderer, MouseState mouse)
    {
        _chatModes ??= [.. ChatButtons.Select(b => new Button(Gui(b.Sprite), b.X, b.Y))];
        bool shoutEnabled = ShoutEnabled();
        foreach (int i in ChatModeOrder)
            if (i != Shout || shoutEnabled)
                _chatModes[i].Update(mouse);
        for (int i = 0; i < _chatModes.Length; i++)
            _chatModes[i].SetFrame(0, ClosedMenuFrame);
        if (ChatMode != ShoutMode || shoutEnabled)
            _chatModes[ModeButton(ChatMode)].SetFrame(0, OpenMenuFrame);
        foreach (int i in ChatModeOrder)
        {
            if (i == Shout && !shoutEnabled)
                _chatModes[i].Disable();
            _chatModes[i].Draw(renderer);
            if (_chatModes[i].Clicked && (i != Shout || shoutEnabled))
                ChatMode = ButtonMode(i);
        }
        foreach (int i in ChatModeOrder)
        {
            if (i == Shout && !shoutEnabled)
                continue;
            if (_chatModes[i].IsHot(mouse))
                tooltips.DrawMessageAt(renderer, game.Texts[ChatModeHints[ButtonMode(i)]], _chatModes[i].X, _chatModes[i].Y - ChatHintUp);
        }
    }

    /// <summary>✅ Shout needs a GM (<c>[DAT_01158504]+0x82</c>), a tamer class of 5 or more (<c>+0x12df</c>) or the BATTLE ZONE.</summary>
    private bool ShoutEnabled() => TamerClass >= 5 || game.IsBattleZone;

    /// <summary>The tamer's class of the frame (the low byte of the <c>0x02</c>'s, as the panel shows it).</summary>
    private int TamerClass { get; set; }

    // ✅ ChatButtons index → mode: 000_023 Guild (4), 000_024 Party (2), 000_025 General (0), 000_026 Whisper (1), 000_027 Shout (3)
    private static readonly int[] ButtonModes = [4, 2, 0, 1, 3];
    private static readonly int[] ChatModeOrder = [2, 3, 1, 0, 4];                     // ✅ the order they are read and drawn
    private const int Shout = 4, ShoutMode = 3, ChatHintUp = 0x1e;
    private static readonly int[] ChatModeHints = [0x88b, 0x88c, 0x88d, 0x88f, 0x88e];  // by mode

    private static int ButtonMode(int button) => ButtonModes[button];

    private static int ModeButton(int mode) => Array.IndexOf(ButtonModes, mode);

    /// <summary>✅ <c>0x00b8e2a0</c>: the size buttons' names in the tooltip (<c>0x890..0x892</c>), 20 px above the button.</summary>
    private static readonly int[] ChatSizeHints = [0x890, 0x891, 0x892];
    private const int ChatSizeHintUp = 0x14;

    private Button[]? _chatTabs, _chatModes, _chatSizes;

    private ITexture[] Gui(int number) => sprites.Gui($"dr2_main_000_{number:000}");

    private ITexture[] Load(string path, bool gui = false) =>
        gui ? sprites.Gui(path["gui\\".Length..^".spr".Length]) : sprites.Load(path);

    private static void Frame(IRenderer renderer, ITexture[] frames, int index, int x, int y)
    {
        if (index >= 0 && index < frames.Length)
            renderer.Draw(frames[index], x, y);
    }

    private static void Frame(IRenderer renderer, ITexture[] frames, int x, int y)
    {
        if (frames.Length > 0)
            renderer.Draw(frames[0], x, y);
    }
}
