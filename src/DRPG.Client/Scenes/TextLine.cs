using DRPG.Engine.Rendering;

namespace DRPG.Client.Scenes;

/// <summary>One line of text kept as a texture and rendered again only when the text changes.</summary>
public sealed class TextLine(IFont? font) : IDisposable
{
    private ITexture? _texture;
    private string _text = "";

    public void Draw(IRenderer renderer, string text, int x, int y, uint rgba = 0xFFFFFFFF)
    {
        if (font is null || text.Length == 0)
            return;
        if (_texture is null || text != _text)
        {
            _texture?.Dispose();
            _texture = font.Render(renderer, text, rgba);
            _text = text;
        }
        renderer.Draw(_texture, x, y);
    }

    public void Dispose() => _texture?.Dispose();
}
