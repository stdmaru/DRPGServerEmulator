using DRPG.Client.Scenes;
using DRPG.Client.Text;
using DRPG.Engine.Rendering;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Formats.Sprite;
using DRPG.Shared.Packets.Map;

namespace DRPG.Client.Ui;

/// <summary>What the basic HUD shows (the real values of the <c>0x02</c> packet and of the map).</summary>
public sealed record HudValues(
    string TamerName, int TamerId, int TamerLevel, string ChannelName, int MapId, string MapName, int CellX, int CellY,
    DigimonStatus? Leader, DateTime ClockUtc);

/// <summary>
/// The basic UI of the field (✅ v322 main bar <c>0x00b8a850</c>, docs/cliente/cena-jogo.md#ui-do-campo-hud): the tamer panel in the
/// top left corner and the bar at the bottom, with the digimon's gauges, the menu buttons, the side buttons and the chat frame.
/// Everything is only drawn: nothing reacts to the mouse in this milestone. ❓ Not drawn yet (they need a blend or come with their
/// own step): the glow frame 1 of the bar, the level icon, the minimap, the teleport buttons, the REWARD button, the quest panels, the
/// EXP bonus box, the bell and the W/E icons.
/// </summary>
public sealed class FieldHud(GameContext game)
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

    // ✅ SEAL, SHOP, DEPOSITORY, WORLD
    private static readonly (int Sprite, int X, int Y)[] SideButtons = [(42, 516, 439), (32, 516, 462), (41, 516, 485), (31, 451, 485)];

    // ✅ the chat mode buttons (000_023..027), mode 0 = 000_025 shows frame 0, and the tabs 000_037..040
    private static readonly (int Sprite, int X, int Y)[] ChatButtons = [(23, 179, 572), (24, 201, 572), (25, 222, 572), (26, 244, 572), (27, 157, 572)];
    private static readonly (int Sprite, int X, int Y)[] ChatTabs = [(37, 280, 574), (38, 317, 574), (39, 355, 574), (40, 391, 574)];

    private const int ChatX = 28, ChatY = 460, ChatHeight = 81;

    private readonly Dictionary<string, ITexture[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    public void Draw(IRenderer renderer, HudValues v)
    {
        DrawBar(renderer);
        DrawTamerPanel(renderer, v);
        DrawDigimonPanel(renderer, v.Leader);
        DrawButtons(renderer);
    }

    /// <summary>✅ 1. the bar's background, frame 0 at (0, 495) (❓ frame 1 is a glow drawn blended over it: not yet).</summary>
    private void DrawBar(IRenderer renderer) => Frame(renderer, Gui(0), 0, 495);

    /// <summary>✅ 2. the panel of the tamer.</summary>
    private void DrawTamerPanel(IRenderer renderer, HudValues v)
    {
        var text = game.Text;
        Frame(renderer, Load("interface\\w_unitframe.spr"), 0, 0, 0);
        Frame(renderer, Load($"sprite\\icon_{v.TamerId:000}.spr"), 1, 2, 2);                 // ❓ the portrait sprite is guessed from the table of icons
        text.Draw(renderer, v.TamerName, 33, 4, Yellow);
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

    /// <summary>✅ 4. the digimon's panel: portrait, level and the four gauges.</summary>
    private void DrawDigimonPanel(IRenderer renderer, DigimonStatus? leader)
    {
        if (leader is null || leader.IsEmpty)
            return;
        Frame(renderer, Load($"sprite\\icon_{Data.SpeciesTables.SpriteId(leader.SpeciesId):000}.spr"), 0, 594, 509);   // ❓ see the tamer's portrait
        game.Text.Draw(renderer, $"Lv.{leader.Level}", 670, 508);
        Gauge(renderer, 2, 692, 525, Percent(leader.Hp, leader.MaxHp));
        Gauge(renderer, 5, 692, 545, Percent(leader.Vp, leader.MaxVp));
        Gauge(renderer, 4, 692, 564, Percent(leader.Evp, leader.MaxEvp));
        Gauge(renderer, 3, 488, 585, Percent(leader.Exp, leader.ExpToNextLevel));
    }

    /// <summary>✅ <c>0x007e3560</c>: frame 0 clipped to the gauge's width, then frame 1 whole over it.</summary>
    private void Gauge(IRenderer renderer, int sprite, int x, int y, int percent)
    {
        var frames = Gui(sprite);
        if (frames.Length == 0)
            return;
        renderer.DrawPart(frames[0], x, y, 0, Math.Min(frames[0].Width, GaugeWidth(frames[0].Width, percent)));
        Frame(renderer, frames, 1, x, y);
    }

    /// <summary>✅ 4–5. the menu buttons, the side buttons and the chat, in their normal state.</summary>
    private void DrawButtons(IRenderer renderer)
    {
        foreach (var (sprite, x, y) in MenuButtons)
            Frame(renderer, Gui(sprite), 3, x, y);                                            // ✅ frame 3 = the window is closed
        foreach (var (sprite, x, y) in SideButtons)
            Frame(renderer, Gui(sprite), 0, x, y);

        if (Gui(18) is { Length: > 0 } chat)                                                  // ✅ the chat frame, clipped to 81 px (❓ blended with the option)
            renderer.DrawRegion(chat[0], ChatX, ChatY, 0, 0, chat[0].Width, Math.Min(chat[0].Height, ChatHeight));
        foreach (var (sprite, x, y) in ChatButtons)
        {
            bool current = sprite == 25;                                                       // ✅ mode 0 of the chat
            // ❓ the 000_027 button is disabled at level 4 or less: how the original greys it is not known, it is drawn as the others
            Frame(renderer, Gui(sprite), current ? 0 : 3, x, y);
        }
        for (int i = 0; i < ChatTabs.Length; i++)
            Frame(renderer, Gui(ChatTabs[i].Sprite), i == 0 ? 2 : 0, ChatTabs[i].X, ChatTabs[i].Y);   // ✅ the first tab is the current one
    }

    private ITexture[] Gui(int number) => Load($"gui\\dr2_main_000_{number:000}.spr", gui: true);

    /// <summary>A sprite's frames; none when the pack lacks the file or it is damaged (the original draws nothing then).</summary>
    private ITexture[] Load(string path, bool gui = false)
    {
        if (_cache.TryGetValue(path, out var frames))
            return frames;
        try
        {
            if (gui)
            {
                var name = path["gui\\".Length..^".spr".Length];
                frames = game.Resource.Contains(path) || game.Resource.Contains($"gui\\{name}_eng.spr") ? game.Sprites.Gui(name) : [];
            }
            else
            {
                frames = game.Resource.Contains(path) ? game.Sprites.Load(path) : [];
            }
        }
        catch (Exception e) when (e is SpriteFormatException or PackFormatException or InvalidDataException)
        {
            game.Log($"{path}: {e.Message}");
            frames = [];
        }
        _cache[path] = frames;
        return frames;
    }

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
