using DRPG.Engine.Rendering;

namespace DRPG.Client.Ui;

/// <summary>
/// A single-line text box of the client (✅ v322 <c>0x00a19040</c>, docs/cliente/cenas.md#criação-de-personagem-modo-1--re):
/// fixed rectangle and character limit, accepts whatever text events arrive (including non-ASCII from an IME, so the
/// name validation reports <c>0xee5</c>), Backspace removes the last character, and draws white text with a black shadow
/// in the standard font plus a blinking caret when focused.
/// </summary>
public sealed class EditBox(int x, int y, int width, int height, int maxLength) : IDisposable
{
    private const ulong CaretBlinkMs = 500;
    private const uint CaretRgba = 0xFFFFFFFF;

    private string _text = "";
    private ITexture? _caret;

    public int X { get; } = x;
    public int Y { get; } = y;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int MaxLength { get; } = maxLength;

    public string Text => _text;
    public bool Focused { get; set; }

    public bool Contains(int px, int py) => px >= X && py >= Y && px < X + Width && py < Y + Height;

    /// <summary>
    /// Appends non-control characters from <paramref name="input"/> up to <see cref="MaxLength"/>. Non-ASCII characters
    /// are kept so the scene's validation rejects them with <c>0xee5</c> like the original.
    /// </summary>
    public void Type(string input)
    {
        foreach (char c in input)
        {
            if (char.IsControl(c))
                continue;
            if (_text.Length >= MaxLength)
                break;
            _text += c;
        }
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

    /// <summary>✅ <c>0x00a19040</c>: draws white text with a black shadow at (X, Y) and a blinking caret when focused.</summary>
    public void Draw(IRenderer renderer, UiText uiText, ulong nowMs, bool enabled = true)
    {
        int maxTextWidth = Math.Max(0, Width - 1);
        int shownWidth = uiText.DrawTail(renderer, _text, X, Y, maxTextWidth, UiText.White, shadow: true);
        if (enabled && Focused && nowMs / CaretBlinkMs % 2 == 0)
            renderer.Draw(Caret(renderer), X + shownWidth, Y);
    }

    private ITexture Caret(IRenderer renderer)
    {
        if (_caret is not null)
            return _caret;
        var pixels = new uint[UiText.CellHeight];
        Array.Fill(pixels, CaretRgba);
        _caret = renderer.CreateTexture(1, UiText.CellHeight, pixels);
        return _caret;
    }

    public void Dispose()
    {
        _caret?.Dispose();
        _caret = null;
    }
}
