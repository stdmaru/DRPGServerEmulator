using DRPG.Engine.Rendering;

namespace DRPG.Client.Ui;

/// <summary>
/// A text link of the world selection (✅ <c>0x00b53db0(slot, textId, x, y, indent, eventId)</c>): the hit area is the
/// size of the text; at rest the text is yellow with a shadow at (x, y); under the mouse it moves up one pixel and the
/// brackets sprite (<c>dr2_in_002_008</c>, frame 1) appears at (x − indent, y − 9).
/// </summary>
public sealed class TextLink(int textId, int x, int y, int indent, int eventId)
{
    private const int BracketsRise = 9;
    private const uint Yellow = 0xFFFF;

    private Button? _area;

    public int EventId { get; } = eventId;

    public int TextId { get; } = textId;

    /// <summary>Updates the hit area with the mouse and draws the link; true when it was clicked in this frame.</summary>
    public bool Frame(IRenderer renderer, UiText text, string label, ITexture[] brackets, MouseState mouse)
    {
        if (_area is null)
        {
            var (width, height) = text.Measure(renderer, label);
            _area = new Button(x, y, width, height);
        }
        _area.Update(mouse);
        if (_area.State == 0)
        {
            text.Draw(renderer, label, x, y, Yellow);
        }
        else
        {
            if (brackets.Length > 1)
                renderer.Draw(brackets[1], x - indent, y - BracketsRise);
            text.Draw(renderer, label, x, y - 1, Yellow);
        }
        return _area.Clicked;
    }
}
