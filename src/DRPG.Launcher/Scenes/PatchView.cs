using DRPG.Engine.Assets;
using DRPG.Engine.Rendering;

namespace DRPG.Launcher.Scenes;

/// <summary>
/// Left-column bottom panel of the launcher: always-visible auto-patcher status (download/apply progress, maintenance
/// title + wrapped multi-line message below the bar, or "Up to date") with the native-size <c>dr2_in_001_002</c> bar
/// centred inside the box.
/// </summary>
public sealed class PatchView : IDisposable
{
    public const int BoxX = 30, BoxY = 392, BoxWidth = 400, MinBoxHeight = 98;
    private const int PaddingX = 16;
    private const int StatusY = BoxY + 18;
    private const int BarY = BoxY + 46;
    private const int DetailTopGap = 10, DetailLineGap = 4, BottomPadding = 16;

    private const uint NormalColor = 0xFFFFFFFF;
    private const uint CompleteColor = 0xFF6EE66E;
    private const uint DetailColor = 0xFFD8D8D8;

    private readonly NineSliceBox _box;
    private readonly ITexture[] _bar;
    private readonly IFont? _font;
    private readonly IRenderer _renderer;
    private ITexture? _statusTexture;
    private string? _statusText;
    private uint _statusColor;
    private ITexture? _percentTexture;
    private string? _percentText;
    private uint _percentColor;
    private readonly List<ITexture> _detailLines = [];
    private string? _detailText;

    public PatchView(SpriteStore sprites, IFont? font, IRenderer renderer)
    {
        _box = new NineSliceBox(sprites.Load("gui\\dr2_in_000_003.spr"));
        _bar = sprites.Gui("dr2_in_001_002");
        _font = font;
        _renderer = renderer;
    }

    public void Draw(IRenderer r, string status, string detail, double fraction, bool complete)
    {
        double clamped = Math.Clamp(fraction, 0, 1);
        int innerWidth = BoxWidth - 2 * PaddingX;
        uint color = complete ? CompleteColor : NormalColor;

        if (_font is not null && _detailText != detail)
        {
            ClearDetailLines();
            if (!string.IsNullOrWhiteSpace(detail))
            {
                foreach (var line in WrapLines(detail, innerWidth))
                    _detailLines.Add(_font.Render(_renderer, line, DetailColor));
            }
            _detailText = detail;
        }

        int barHeight = _bar.Length > 0 ? _bar[0].Height : 23;
        int lineHeight = _font?.LineHeight ?? 14;
        int boxHeight = MinBoxHeight;
        if (_detailLines.Count > 0)
        {
            int detailHeight = _detailLines.Count * lineHeight + (_detailLines.Count - 1) * DetailLineGap;
            boxHeight = Math.Max(MinBoxHeight, (BarY - BoxY) + barHeight + DetailTopGap + detailHeight + BottomPadding);
        }

        _box.Draw(r, BoxX, BoxY, BoxWidth, boxHeight);

        int percentReserve = 0;
        if (_font is not null)
        {
            string percent = $"{(int)Math.Round(clamped * 100)}%";
            if (_percentTexture is null || _percentText != percent || _percentColor != color)
            {
                _percentTexture?.Dispose();
                _percentTexture = _font.Render(_renderer, percent, color);
                _percentText = percent;
                _percentColor = color;
            }
            percentReserve = _percentTexture.Width + 8;
            r.Draw(_percentTexture, BoxX + BoxWidth - PaddingX - _percentTexture.Width, StatusY);
        }

        if (_font is not null && status.Length > 0)
        {
            if (_statusTexture is null || _statusText != status || _statusColor != color)
            {
                _statusTexture?.Dispose();
                _statusTexture = _font.Render(_renderer, status, color);
                _statusText = status;
                _statusColor = color;
            }
            int maxStatusWidth = Math.Max(0, innerWidth - percentReserve);
            r.DrawPart(_statusTexture, BoxX + PaddingX, StatusY, 0, Math.Min(_statusTexture.Width, maxStatusWidth));
        }

        if (_bar.Length > 0)
        {
            var fill = _bar[0];
            int barX = BoxX + (BoxWidth - fill.Width) / 2;
            int fillWidth = (int)Math.Round(fill.Width * clamped);
            if (fillWidth > 0)
                r.DrawPart(fill, barX, BarY, 0, fillWidth);
            if (_bar.Length > 1)
                r.Draw(_bar[1], barX, BarY);
        }

        if (_detailLines.Count > 0)
        {
            int y = BarY + barHeight + DetailTopGap;
            foreach (var line in _detailLines)
            {
                r.DrawPart(line, BoxX + PaddingX, y, 0, Math.Min(line.Width, innerWidth));
                y += lineHeight + DetailLineGap;
            }
        }
    }

    private List<string> WrapLines(string text, int maxWidth)
    {
        var lines = new List<string>();
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var paragraph in normalized.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                lines.Add("");
                continue;
            }
            string current = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = current.Length == 0 ? word : $"{current} {word}";
                if (MeasureWidth(candidate) <= maxWidth)
                {
                    current = candidate;
                    continue;
                }
                if (current.Length > 0)
                    lines.Add(current);
                current = word;
                while (current.Length > 1 && MeasureWidth(current) > maxWidth)
                {
                    int fit = current.Length - 1;
                    while (fit > 1 && MeasureWidth(current[..fit]) > maxWidth)
                        fit--;
                    lines.Add(current[..fit]);
                    current = current[fit..];
                }
            }
            if (current.Length > 0)
                lines.Add(current);
        }
        return lines;
    }

    private int MeasureWidth(string text)
    {
        if (_font is null)
            return text.Length * 7;
        using var t = _font.Render(_renderer, text, DetailColor);
        return t.Width;
    }

    private void ClearDetailLines()
    {
        foreach (var line in _detailLines)
            line.Dispose();
        _detailLines.Clear();
    }

    public void Dispose()
    {
        _statusTexture?.Dispose();
        _percentTexture?.Dispose();
        ClearDetailLines();
    }
}
