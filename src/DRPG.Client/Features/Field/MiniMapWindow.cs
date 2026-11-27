using DRPG.Client.Scenes;
using DRPG.Client.Startup;
using DRPG.Client.Ui;
using DRPG.Engine.Rendering;
using DRPG.Shared.Formats.Map;

namespace DRPG.Client.Features.Field;

/// <summary>
/// The minimap, window 5 (✅ <c>WindowMiniMap</c>, vtable <c>0x01033008</c>: ctor <c>0x00b9ae90</c>, load <c>0x00b9d310</c>, frame
/// <c>0x00b9c600</c>; docs/cliente/cena-jogo.md#minimapa-windowminimap-janela-5): the map's picture in the top right corner with the
/// NPC and portal markers, the player's point and the names under the mouse; the button on the title collapses it. It reads the field's
/// state; a click on the picture (walking there, C2S <c>0xE3</c>, <c>0x00b9d120</c>) comes with its feature.
/// </summary>
public sealed class MiniMapWindow : GameWindow
{
    public const int WindowId = WindowManager.MiniMapWindow;

    /// <summary>✅ <c>0x00b9d310</c>: the window's corner, and the collapse button's.</summary>
    public const int Left = 0x281, Top = 0, CollapseX = 0x30d, CollapseY = 5;

    /// <summary>✅ The picture's corner from the window's (<c>X + 4</c>, <c>Y + 0x16</c>), and its width when the frame has none.</summary>
    private const int PictureLeft = 4, PictureTop = 0x16, DefaultPictureWidth = 0x96;

    /// <summary>✅ The frame (<c>dr2_sub_007_021</c>) under the title: 20 px down with the <c>alpha</c> option, 15 without.</summary>
    private const int FrameTopBlended = 0x14, FrameTopPlain = 0xf;

    /// <summary>✅ The picture is mixed at 24/32 with the <c>alpha</c> option (<c>0x00b569f0(…, −1, 0x18, …)</c>).</summary>
    public const int PictureAlpha = 0x18;

    /// <summary>✅ <c>0x00b9c4b0</c>: a name box is 17 px high, 3 px wider than the name, mixed at 16/32; the name 2 px in.</summary>
    private const int NameHeight = 0x11, NameExtraWidth = 3, NameAlpha = 0x10, NameTextLeft = 2, NameBoxUp = 0x10, NameTextUp = 0xd;

    private const string TitleSprite = "gui\\dr2_sub_007_007.spr", FrameSprite = "gui\\dr2_sub_007_021.spr";
    private const string OpenButtonSprite = "gui\\dr2_sub_007_022.spr", CollapsedButtonSprite = "gui\\dr2_sub_007_023.spr";
    private const string MarkerSprite = "interface\\Map_icon_01.spr", PointSprite = "interface\\w_minimap_point.spr";

    private readonly GameContext _game;
    private readonly UiSprites _sprites;
    private readonly Tooltips _tooltips;
    private readonly FieldState _field;
    private readonly NpcTable _npcs;
    private readonly Func<int, IReadOnlyList<MapPortalInfo>> _portals;
    private readonly List<string> _names = [];
    private Button _collapse;
    private int _pictureWidth, _pictureHeight;

    public MiniMapWindow(GameContext game, UiSprites sprites, Tooltips tooltips, FieldState field, NpcTable npcs,
        Func<int, IReadOnlyList<MapPortalInfo>> portals) : base(WindowId)
    {
        _game = game;
        _sprites = sprites;
        _tooltips = tooltips;
        _field = field;
        _npcs = npcs;
        _portals = portals;
        _collapse = new Button(sprites.Load(OpenButtonSprite), CollapseX, CollapseY);
        X = Left;
        Y = Top;
    }

    /// <summary>✅ <c>+0x180</c>: only the title and its button are drawn.</summary>
    public bool Collapsed { get; private set; }

    /// <summary>✅ <c>0x00b9d0f0</c>: open, on a map that shows it and has a picture — what the teleport buttons of the bar wait for.</summary>
    public bool ShowsMap => Visible && ShowsOn(_field.Area.MapId) && Picture(_field.Area.MapId) is not null;

    /// <summary>✅ <c>0x00b9cfe0</c>: the maps without a minimap (the tutorial, 0x4f, among them).</summary>
    public static bool ShowsOn(int mapId) => mapId switch
    {
        0x22 or 0x25 or 0x2e or (>= 0x33 and <= 0x35) or (>= 0x4f and <= 0x57) or 0x59 or 0x5e or (>= 0x64 and <= 0x6e) or 0x75 or 0x77
            or 0x85 or (>= 0x88 and <= 0x8d) or 0x8f or (>= 0x98 and <= 0x9c) or 0xca or 0xfa => false,
        _ => true,
    };

    /// <summary>✅ <c>0x00b9c600</c>: the picture of a map, a frame of one of the <c>Interface\w_minimap_N.spr</c>; null for none.</summary>
    public static (string Sprite, int Frame)? Picture(int mapId)
    {
        switch (mapId)
        {
            case >= 1 and <= 19:
                return ("interface\\w_minimap_1.spr", mapId - 1);
            case >= 0x33 and <= 0x35:
                return ("interface\\w_minimap_2.spr", mapId - 0x22 - 0xc);
            case 35:
                return ("interface\\w_minimap_2.spr", 9);
            case 36:
                return ("interface\\w_minimap_2.spr", 8);
            case >= 37 and <= 39:
                return ("interface\\w_minimap_2.spr", mapId - 20 - 0xc);
            case >= 20 and <= 34:
                return ("interface\\w_minimap_2.spr", mapId - 20);
            case >= 40 and <= 46:
                return ("interface\\w_minimap_3.spr", mapId - 40);
            case >= 60 and <= 65:
                return ("interface\\w_minimap_4.spr", mapId - 60);
            case >= 200 and <= 202:
                return ("interface\\w_minimap_5.spr", mapId - 200);
            case >= 70 and <= 77:
                return ("interface\\w_minimap_6.spr", mapId - 70);
            case 88:
                return ("interface\\w_minimap_7.spr", 0);
            case 118:
                return ("interface\\w_minimap_7.spr", 1);
            case >= 120 and <= 123:
                return ("interface\\w_minimap_7.spr", mapId - 118);
            default:
                return null;
        }
    }

    /// <summary>❓ The window's area for the mouse: the title, and the frame under it while open (the original's <c>+0x78</c> is not read).</summary>
    public override bool Contains(int x, int y)
    {
        if (!ShowsOn(_field.Area.MapId) || Picture(_field.Area.MapId) is null)
            return false;                                              // ✅ nothing is drawn on these maps
        int height = _sprites.Load(TitleSprite) is { Length: > 0 } title ? title[0].Height : 0;
        if (!Collapsed && _sprites.Load(FrameSprite) is { Length: > 0 } frame)
            height = FrameTopBlended + frame[0].Height;
        int width = _sprites.Load(TitleSprite) is { Length: > 0 } t ? t[0].Width : 0;
        return x >= X && x < X + width && y >= Y && y < Y + height;
    }

    /// <summary>✅ <c>0x00b9c600</c>.</summary>
    public override void Frame(IRenderer renderer, MouseState mouse, ulong nowMs)
    {
        int mapId = _field.Area.MapId;
        if (!ShowsOn(mapId) || Picture(mapId) is not { } picture)
            return;
        var image = _sprites.Load(picture.Sprite);
        if (picture.Frame < 0 || picture.Frame >= image.Length)
            return;
        var frame = image[picture.Frame];
        _pictureWidth = frame.Width == 0 ? DefaultPictureWidth : frame.Width;
        _pictureHeight = frame.Height;
        Width = _pictureWidth;
        Height = _pictureHeight;

        var options = _game.SpriteOptions;
        DrawFirst(renderer, _sprites.Load(TitleSprite), X, Y);
        if (!Collapsed)
        {
            DrawFirst(renderer, _sprites.Load(FrameSprite), X, Y + (options.Alpha ? FrameTopBlended : FrameTopPlain));
            if (options.Alpha)
                options.DrawBlended(renderer, frame, X + PictureLeft, Y + PictureTop, PictureAlpha);
            else
                renderer.Draw(frame, X + PictureLeft, Y + PictureTop);
        }

        _collapse.Update(mouse);
        _collapse.Draw(renderer);
        if (_collapse.Clicked)
        {
            Collapsed = !Collapsed;
            // ✅ the button shows 023 while collapsed and 022 while open (0x009b76b0)
            _collapse = new Button(_sprites.Load(Collapsed ? CollapsedButtonSprite : OpenButtonSprite), CollapseX, CollapseY);
        }
        if (Collapsed)
            return;

        _names.Clear();
        DrawMarkers(renderer, mouse, mapId);
        DrawPlayer(renderer);
        DrawNames(renderer, mouse);
    }

    /// <summary>
    /// ✅ <c>0x00b9b930</c>: every NPC of the map whose <c>npc_info_us</c> row has column 14 set, with the frame of column 13 at the row's
    /// cell (not the NPC's own); then the map's portals that are on, with frame 0. A marker under the mouse adds its name.
    /// </summary>
    private void DrawMarkers(IRenderer renderer, MouseState mouse, int mapId)
    {
        var markers = _sprites.Load(MarkerSprite);
        foreach (var npc in _field.Area.Npcs.All)
            if (_npcs.Find(npc.NpcId) is { OnMiniMap: true } row)
                DrawMarker(renderer, mouse, markers, row.MiniMapFrame, row.X, row.Y, row.Name);
        foreach (var portal in _portals(mapId))
            if (portal.Enabled)
                DrawMarker(renderer, mouse, markers, 0, portal.X, portal.Y, portal.Name);
    }

    /// <summary>
    /// ✅ A cell's place on the picture: <c>(int)((float)cell / (float)cells · (float)size)</c>, centred on the frame's size. The mouse is on
    /// it inside [x, x + w) × [y, y + h) (<c>PtInRect</c>).
    /// </summary>
    private void DrawMarker(IRenderer renderer, MouseState mouse, ITexture[] markers, int frameIndex, int cellX, int cellY, string name)
    {
        if (markers.Length == 0)
            return;
        var size = markers[0];
        int x = X + PictureLeft + Scale(cellX, _field.Area.Map.Width, _pictureWidth) - size.Width / 2;
        int y = Y + PictureTop + Scale(cellY, _field.Area.Map.Height, _pictureHeight) - size.Height / 2;
        if (frameIndex >= 0 && frameIndex < markers.Length)
            renderer.Draw(markers[frameIndex], x, y);
        if (mouse.X >= x && mouse.X < x + size.Width && mouse.Y >= y && mouse.Y < y + size.Height)
            _names.Add(name);
    }

    /// <summary>✅ The player's point, centred on the tamer's pixel position scaled to the picture (<c>+0x168</c> / map width in pixels).</summary>
    private void DrawPlayer(IRenderer renderer)
    {
        var point = _sprites.Load(PointSprite);
        if (point.Length == 0)
            return;
        var tamer = _field.Party.Tamer;
        int x = Scale(tamer.WorldX, _field.Area.Map.Width * MapFile.CellWidth, _pictureWidth);
        int y = Scale(tamer.WorldY, _field.Area.Map.Height * MapFile.CellHeight, _pictureHeight);
        renderer.Draw(point[0], x + PictureLeft + X - point[0].Width / 2, y + PictureTop + Y - point[0].Height / 2);
    }

    /// <summary>
    /// ✅ <c>0x00b9c4b0</c>: the names under the mouse, one box above the other, centred on the mouse and kept inside the screen's right
    /// edge; the name in white without shadow.
    /// </summary>
    private void DrawNames(IRenderer renderer, MouseState mouse)
    {
        for (int i = 0; i < _names.Count; i++)
        {
            int width = _tooltips.Measure(renderer, _names[i]) + NameExtraWidth;
            int x = mouse.X - width / 2;
            if (x + width > IRenderer.FrameWidth)
                x = IRenderer.FrameWidth - width;
            int offset = i * NameHeight;
            _tooltips.DrawPanelRect(renderer, _game.SpriteOptions, x, mouse.Y - offset - NameBoxUp, width, NameHeight, NameAlpha);
            _tooltips.DrawPlainText(renderer, _names[i], x + NameTextLeft, mouse.Y - offset - NameTextUp);
        }
    }

    private static int Scale(int value, int range, int size) => range <= 0 ? 0 : (int)((float)value / range * size);

    private static void DrawFirst(IRenderer renderer, ITexture[] frames, int x, int y)
    {
        if (frames.Length > 0)
            renderer.Draw(frames[0], x, y);
    }
}
