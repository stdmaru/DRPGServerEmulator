namespace DRPG.Client.Features.Chat;

/// <summary>A line of the chat: its text and colour (✅ the r, g, b bytes of <c>0x00b32cd0</c>).</summary>
public readonly record struct ChatLine(string Text, byte Red, byte Green, byte Blue)
{
    /// <summary>The colour as a Windows COLORREF (0x00bbggrr), what the text drawing takes.</summary>
    public uint ColorRef => (uint)(Red | Green << 8 | Blue << 16);
}

/// <summary>
/// The lines of the chat (✅ <c>DAT_01158508</c>'s chat list, fed by <c>0x00b869a0</c> → <c>0x00b32cd0</c>): what the client prints in it.
/// The window that draws it reads <see cref="Lines"/>; messages from other players come with the chat packets.
/// </summary>
public sealed class ChatLog
{
    /// <summary>✅ <c>0x00b86a60</c>: the list keeps 0x60 lines; past that the oldest goes (<c>0x00b86c20</c>).</summary>
    public const int Capacity = 0x60;

    private readonly List<ChatLine> _lines = [];

    public IReadOnlyList<ChatLine> Lines => _lines;

    /// <summary>✅ <c>0x00b869a0(text, r, g, b, 0)</c>: a line printed by the client itself; an empty text prints nothing.</summary>
    public void AddSystem(string text, byte red, byte green, byte blue)
    {
        if (text.Length == 0)
            return;
        if (_lines.Count >= Capacity)
            _lines.RemoveAt(0);
        _lines.Add(new ChatLine(text, red, green, blue));
    }
}
