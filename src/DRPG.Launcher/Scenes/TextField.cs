using DRPG.Engine.Rendering;

namespace DRPG.Launcher.Scenes;

/// <summary>
/// One line of typed text in a fixed box: ID or password on the login panel. Accepts printable ASCII only (logins
/// are ASCII on the server; this also keeps IME and control characters out). A password is drawn as <c>*</c>.
/// When the text is wider than the box, its tail is shown so the caret stays visible.
/// </summary>
public sealed class TextField(int x, int y, int width, int height, int maxLength, bool masked, bool allowSpace)
{
    public const uint TextColor = 0xFFFFFFFF;
    public const uint DisabledTextColor = 0xFF9A9A9A;
    private const ulong CaretBlinkMs = 500;
    private const int PaddingX = 3;

    private string _text = "";
    private ITexture? _texture;
    private string? _textureText;
    private uint _textureColor;

    public string Text => _text;
    public bool Focused { get; set; }

    public bool Contains(int px, int py) => px >= x && py >= y && px < x + width && py < y + height;


    /// <summary>Appends what is accepted of <paramref name="input"/>; returns false when anything was dropped.</summary>
    public bool Type(string input)
    {
        bool all = true;
        foreach (char c in input)
        {
            bool printable = c is > ' ' and < (char)127 || allowSpace && c == ' ';
            if (!printable || _text.Length >= maxLength)
            {
                all = false;
                continue;
            }
            _text += c;
        }
        return all;
    }

    public void Backspace()
    {
        if (_text.Length > 0)
            _text = _text[..^1];
    }

    public void SetText(string text)
    {
        _text = "";
        Type(text);
    }

    public void Clear() => _text = "";

    /// <summary>Disabled: grey text and no caret.</summary>
    public void Draw(IRenderer r, IFont? font, ulong nowMs, bool enabled)
    {
        if (font is null)
            return;
        string shown = masked ? new string('*', _text.Length) : _text;
        uint color = enabled ? TextColor : DisabledTextColor;
        if (_texture is null || _textureText != shown || _textureColor != color)
        {
            _texture?.Dispose();
            _texture = font.Render(r, shown, color);
            _textureText = shown;
            _textureColor = color;
        }
        var texture = _texture;
        int inner = width - 2 * PaddingX;
        int textWidth = Math.Min(texture.Width, inner - 1);
        int textY = y + (height - font.LineHeight + 1) / 2;
        r.DrawPart(texture, x + PaddingX, textY, texture.Width - textWidth, textWidth);
        if (enabled && Focused && nowMs / CaretBlinkMs % 2 == 0)
            r.Draw(Caret(r, font.LineHeight), x + PaddingX + textWidth, textY);
    }

    private ITexture? _caret;
    private int _caretHeight;

    private ITexture Caret(IRenderer r, int lineHeight)
    {
        if (_caret is null || _caretHeight != lineHeight)
        {
            _caret?.Dispose();
            var pixels = new uint[lineHeight];
            Array.Fill(pixels, TextColor);
            _caret = r.CreateTexture(1, lineHeight, pixels);
            _caretHeight = lineHeight;
        }
        return _caret;
    }
}
