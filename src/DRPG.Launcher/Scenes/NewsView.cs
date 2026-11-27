using DRPG.Engine.Assets;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;
using DRPG.Launcher.Patching;

namespace DRPG.Launcher.Scenes;

/// <summary>
/// Left-column top panel of the launcher: shows the latest news items fetched from <c>MainUrl.NewsPageUrl</c> with
/// coloured <c>[Event]</c>, <c>[Info]</c>, and <c>[Update]</c> badges, right-aligned date, clickable URL rows, and
/// mouse-wheel scrolling.
/// </summary>
public sealed class NewsView : IDisposable
{
    public const int BoxX = 30, BoxY = 165, BoxWidth = 400, BoxHeight = 212;
    private const int TitleY = BoxY + 12;
    private const int ListX = BoxX + 16, ListY = BoxY + 38, RowHeight = 20, VisibleRows = 8;
    private const int InnerWidth = BoxWidth - 32;

    // RGBA8888 (R in low byte)
    private const uint EventBadgeColor = 0xFF40B4FF;   // warm orange/gold
    private const uint InfoBadgeColor = 0xFFFFC864;    // sky blue
    private const uint UpdateBadgeColor = 0xFF6EE66E;  // light green
    private const uint TitleColor = 0xFFFFFFFF;
    private const uint HoverTitleColor = 0xFF80E8FF;   // bright gold-tinted highlight for clickable hovered row
    private const uint DateColor = 0xFFD0C4B4;
    private const uint MutedColor = 0xFFB0B0B0;
    private const uint HoverRowFill = 0x26FFFFFF;      // subtle translucent hover band

    private readonly NineSliceBox _box;
    private readonly ITexture? _header;
    private readonly ITexture _hoverRow;
    private readonly IFont? _font;
    private readonly IRenderer _renderer;
    private readonly Dictionary<(string Text, uint Color), ITexture> _textCache = [];

    private IReadOnlyList<NewsItem> _items = [];
    private int _scrollIndex;
    private int _mouseX = -1, _mouseY = -1;
    private int _hoveredIndex = -1;
    private int _pressedIndex = -1;

    public NewsView(SpriteStore sprites, IFont? font, IRenderer renderer)
    {
        _box = new NineSliceBox(sprites.Load("gui\\dr2_in_000_003.spr"));
        var headerFrames = sprites.Gui("dr2_in_000_007");
        _header = headerFrames.Length > 0 ? headerFrames[0] : null;
        var hoverPixels = new uint[InnerWidth * RowHeight];
        Array.Fill(hoverPixels, HoverRowFill);
        _hoverRow = renderer.CreateTexture(InnerWidth, RowHeight, hoverPixels);
        _font = font;
        _renderer = renderer;
    }

    /// <summary>Set when the user clicks a news item with a valid <c>http(s)</c> URL; cleared by the caller.</summary>
    public string? ClickedUrl { get; set; }

    public void Handle(InputEvent e)
    {
        switch (e)
        {
            case MouseMoveEvent m:
                (_mouseX, _mouseY) = (m.X, m.Y);
                _hoveredIndex = ItemIndexAt(m.X, m.Y);
                break;
            case MouseLeaveEvent:
                (_mouseX, _mouseY) = (-1, -1);
                _hoveredIndex = -1;
                _pressedIndex = -1;
                break;
            case MouseWheelEvent wheel when Inside(_mouseX, _mouseY):
                int maxScroll = Math.Max(0, _items.Count - VisibleRows);
                _scrollIndex = Math.Clamp(_scrollIndex - wheel.Delta, 0, maxScroll);
                _hoveredIndex = ItemIndexAt(_mouseX, _mouseY);
                break;
            case MouseButtonEvent b:
                int index = ItemIndexAt(b.X, b.Y);
                if (b.Down)
                {
                    _pressedIndex = index;
                }
                else
                {
                    if (_pressedIndex >= 0 && _pressedIndex == index && index < _items.Count && _items[index].Url.Length > 0)
                        ClickedUrl = _items[index].Url;
                    _pressedIndex = -1;
                }
                break;
        }
    }

    public void Draw(IRenderer r, IReadOnlyList<NewsItem>? items, bool loading)
    {
        _box.Draw(r, BoxX, BoxY, BoxWidth, BoxHeight);
        if (_header is not null)
            r.Draw(_header, BoxX + (BoxWidth - _header.Width) / 2, TitleY);

        if (_font is null)
            return;

        if (items is null || items.Count == 0)
        {
            _items = [];
            _scrollIndex = 0;
            _hoveredIndex = -1;
            string empty = loading ? "Loading latest news..." : "No news available.";
            r.Draw(Text(empty, MutedColor), ListX, ListY);
            return;
        }

        _items = items;
        int maxScroll = Math.Max(0, items.Count - VisibleRows);
        if (_scrollIndex > maxScroll)
            _scrollIndex = maxScroll;
        _hoveredIndex = ItemIndexAt(_mouseX, _mouseY);

        for (int row = 0; row < VisibleRows && _scrollIndex + row < items.Count; row++)
        {
            int index = _scrollIndex + row;
            var item = items[index];
            int y = ListY + row * RowHeight;
            bool clickable = item.Url.Length > 0;
            bool hovered = clickable && index == _hoveredIndex;

            if (hovered)
                r.Draw(_hoverRow, ListX, y - 2);

            string badgeText = $"[{item.Category}]";
            var badge = Text(badgeText, BadgeColor(item.Category));
            r.Draw(badge, ListX, y);

            int titleRight = ListX + InnerWidth;
            if (item.Date.Length > 0)
            {
                var dateTex = Text(item.Date, hovered ? HoverTitleColor : DateColor);
                int dateX = ListX + InnerWidth - dateTex.Width;
                r.Draw(dateTex, dateX, y);
                titleRight = dateX - 8;
            }

            int titleX = ListX + badge.Width + 6;
            int maxTitleWidth = Math.Max(0, titleRight - titleX);
            if (maxTitleWidth > 0)
            {
                var title = Text(item.Title, hovered ? HoverTitleColor : TitleColor);
                r.DrawPart(title, titleX, y, 0, Math.Min(title.Width, maxTitleWidth));
            }
        }
    }

    private int ItemIndexAt(int x, int y)
    {
        if (x < ListX || x >= ListX + InnerWidth || y < ListY || y >= ListY + VisibleRows * RowHeight)
            return -1;
        int row = (y - ListY) / RowHeight;
        int index = _scrollIndex + row;
        return index < _items.Count ? index : -1;
    }

    private static bool Inside(int x, int y) => x >= BoxX && x < BoxX + BoxWidth && y >= BoxY && y < BoxY + BoxHeight;

    internal static uint BadgeColor(string category) => category switch
    {
        "Event" => EventBadgeColor,
        "Update" => UpdateBadgeColor,
        _ => InfoBadgeColor,
    };

    private ITexture Text(string value, uint color)
    {
        var key = (value, color);
        if (_textCache.TryGetValue(key, out var existing))
            return existing;
        if (_textCache.Count >= 128)
            ClearCache();
        var tex = _font!.Render(_renderer, value, color);
        _textCache[key] = tex;
        return tex;
    }

    private void ClearCache()
    {
        foreach (var tex in _textCache.Values)
            tex.Dispose();
        _textCache.Clear();
    }

    public void Dispose()
    {
        ClearCache();
        _hoverRow.Dispose();
    }
}
