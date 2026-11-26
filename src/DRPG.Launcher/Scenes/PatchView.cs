using DRPG.Launcher.Assets;
using DRPG.Launcher.Rendering;

namespace DRPG.Launcher.Scenes;

/// <summary>
/// ❓ Provisional patch screen (option A, docs/cliente/tela-login-launcher.md#tela-do-patch): the "Loading" box in
/// place of the login panel, the bar cut to the progress of the current patch.
/// </summary>
public sealed class PatchView(SpriteStore sprites)
{
    private const int BoxX = 280, BoxY = 124;
    private const int BarX = BoxX + 3, BarY = BoxY + 165;

    private readonly ITexture _box = sprites.Gui("dr2_in_001_000")[0];
    private readonly ITexture _bar = sprites.Gui("dr2_in_001_002")[0];

    public void Draw(IRenderer r, double fraction)
    {
        r.Draw(_box, BoxX, BoxY);
        r.DrawPart(_bar, BarX, BarY, 0, (int)(_bar.Width * Math.Clamp(fraction, 0, 1)));
    }
}
