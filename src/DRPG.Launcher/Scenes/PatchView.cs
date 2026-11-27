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
            var fill = _bar[0];
            int barX = BoxX + (BoxWidth - fill.Width) / 2;
            int fillWidth = (int)Math.Round(fill.Width * clamped);
            if (fillWidth > 0)
                r.DrawPart(fill, barX, BarY, 0, fillWidth);
            if (_bar.Length > 1)
                r.Draw(_bar[1], barX, BarY);
        }
    }

    public void Dispose()
    {
        _statusTexture?.Dispose();
        _percentTexture?.Dispose();
    }
}
