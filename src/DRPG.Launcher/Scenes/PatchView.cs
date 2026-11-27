using DRPG.Engine.Assets;
using DRPG.Engine.Rendering;

namespace DRPG.Launcher.Scenes;

/// <summary>
/// Left-column bottom panel of the launcher: always-visible auto-patcher status (download/apply progress, maintenance
/// notice, or "Up to date") with a full-width progress bar built from <c>dr2_in_001_002</c> and percentage.
/// </summary>
public sealed class PatchView : IDisposable
{
    public const int BoxX = 30, BoxY = 392, BoxWidth = 400, BoxHeight = 98;
    private const int PaddingX = 16;
    private const int StatusY = BoxY + 18;
    private const int BarY = BoxY + 48;
    private const int BarCapWidth = 8;

    private const uint NormalColor = 0xFFFFFFFF;
    private const uint CompleteColor = 0xFF6EE66E;

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

    public PatchView(SpriteStore sprites, IFont? font, IRenderer renderer)
    {
        _box = new NineSliceBox(sprites.Load("gui\\dr2_in_000_003.spr"));
        _bar = sprites.Gui("dr2_in_001_002");
        _font = font;
        _renderer = renderer;
    }

    public void Draw(IRenderer r, string status, double fraction, bool complete)
    {
        _box.Draw(r, BoxX, BoxY, BoxWidth, BoxHeight);

        double clamped = Math.Clamp(fraction, 0, 1);
        int innerWidth = BoxWidth - 2 * PaddingX;
        uint color = complete ? CompleteColor : NormalColor;

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
            int barX = BoxX + PaddingX;
            int fillWidth = (int)Math.Round(innerWidth * clamped);
            if (fillWidth > 0)
                DrawStretchedBar(r, _bar[0], barX, BarY, innerWidth, fillWidth);
            if (_bar.Length > 1)
                DrawStretchedBar(r, _bar[1], barX, BarY, innerWidth, innerWidth);
        }
    }

    /// <summary>
    /// Draws a horizontal bar sprite (<c>dr2_in_001_002</c>, 231×23) stretched to <paramref name="totalWidth"/> by
    /// preserving the left/right caps and tiling the middle segment, clipped at <paramref name="visibleWidth"/>.
    /// </summary>
    private static void DrawStretchedBar(IRenderer r, ITexture tex, int x, int y, int totalWidth, int visibleWidth)
    {
        if (visibleWidth <= 0 || totalWidth <= 0 || tex.Width <= 0)
            return;

        int cap = Math.Min(BarCapWidth, tex.Width / 2);
        int middleSrcW = tex.Width - 2 * cap;
        if (middleSrcW <= 0)
        {
            r.DrawPart(tex, x, y, 0, Math.Min(visibleWidth, tex.Width));
            return;
        }

        // Left cap
        int leftW = Math.Min(cap, visibleWidth);
        if (leftW > 0)
            r.DrawPart(tex, x, y, 0, leftW);

        // Middle tiled section
        int middleEnd = Math.Min(totalWidth - cap, visibleWidth);
        int cursor = cap;
        while (cursor < middleEnd)
        {
            int chunk = Math.Min(middleSrcW, middleEnd - cursor);
            r.DrawPart(tex, x + cursor, y, cap, chunk);
            cursor += chunk;
        }

        // Right cap
        int rightStart = totalWidth - cap;
        if (visibleWidth > rightStart)
        {
            int rightW = Math.Min(cap, visibleWidth - rightStart);
            r.DrawPart(tex, x + rightStart, y, tex.Width - cap, rightW);
        }
    }

    public void Dispose()
    {
        _statusTexture?.Dispose();
        _percentTexture?.Dispose();
    }
}
