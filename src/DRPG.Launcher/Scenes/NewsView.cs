using DRPG.Engine.Assets;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;
using DRPG.Launcher.Patching;

namespace DRPG.Launcher.Scenes;

/// <summary>
/// Left-column top panel of the launcher: shows the latest news items from <c>DR_Configuration.json</c> with coloured
/// <c>[Event]</c>, <c>[Info]</c>, and <c>[Update]</c> badges and mouse-wheel scrolling.
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
    private const uint MutedColor = 0xFFB0B0B0;

    private readonly NineSliceBox _box;
    private readonly ITexture? _header;
    private readonly IFont? _font;
    private readonly IRenderer _renderer;
    private readonly Dictionary<(string Text, uint Color), ITexture> _textCache = [];

    private int _scrollIndex;
    private int _lastCount;
    private int _mouseX = -1, _mouseY = -1;

    public NewsView(SpriteStore sprites, IFont? font, IRenderer renderer)
    {
        _box = new NineSliceBox(sprites.Load("gui\\dr2_in_000_003.spr"));
        var headerFrames = sprites.Gui("dr2_in_000_007");
        _header = headerFrames.Length > 0 ? headerFrames[0] : null;
        _font = font;
        _renderer = renderer;
    }

    public void Handle(InputEvent e)
    {
        switch (e)
        {
            case MouseMoveEvent m:
                (_mouseX, _mouseY) = (m.X, m.Y);
                break;
            case MouseLeaveEvent:
                (_mouseX, _mouseY) = (-1, -1);
                break;
            case MouseWheelEvent wheel when Inside(_mouseX, _mouseY):
                int maxScroll = Math.Max(0, _lastCount - VisibleRows);
                _scrollIndex = Math.Clamp(_scrollIndex - wheel.Delta, 0, maxScroll);
                break;
        }
    }

    public void Draw(IRenderer r, IReadOnlyList<PatchConfiguration.NewsItem>? items, bool loading)
    {
        _box.Draw(r, BoxX, BoxY, BoxWidth, BoxHeight);
        if (_header is not null)
            r.Draw(_header, BoxX + (BoxWidth - _header.Width) / 2, TitleY);

        if (_font is null)
            return;

        if (items is null || items.Count == 0)
        {
            _lastCount = 0;
            _scrollIndex = 0;
            string empty = loading ? "Loading latest news..." : "No news available.";
            r.Draw(Text(empty, MutedColor), ListX, ListY);
            return;
        }

        _lastCount = items.Count;
        int maxScroll = Math.Max(0, items.Count - VisibleRows);
        if (_scrollIndex > maxScroll)
            _scrollIndex = maxScroll;

        for (int row = 0; row < VisibleRows && _scrollIndex + row < items.Count; row++)
        {
            var item = items[_scrollIndex + row];
            int y = ListY + row * RowHeight;
            string badgeText = $"[{item.Category}]";
            var badge = Text(badgeText, BadgeColor(item.Category));
            r.Draw(badge, ListX, y);

            int titleX = ListX + badge.Width + 6;
            int maxTitleWidth = Math.Max(0, ListX + InnerWidth - titleX);
            if (maxTitleWidth > 0)
            {
                var title = Text(item.Title, TitleColor);
                r.DrawPart(title, titleX, y, 0, Math.Min(title.Width, maxTitleWidth));
            }
        }
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

    public void Dispose() => ClearCache();
}
