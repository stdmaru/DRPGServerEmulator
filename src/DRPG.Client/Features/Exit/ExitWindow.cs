using DRPG.Client.Text;
using DRPG.Client.Ui;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Features.Exit;

/// <summary>
/// The exit window, window 6 (✅ ctor <c>0x00b80c10</c>, vtable <c>0x01031518</c>: load <c>0x00b81560</c>, open <c>0x00b814b0</c>,
/// close <c>0x00b81420</c>, frame <c>0x00b80e80</c>, key <c>0x00b81450</c>; docs/cliente/janelas.md#saída-janela-6). Escape with no
/// window open and the options' "Exit Game" open it. "Close" sends the <c>0x2B</c>, closes every other window, locks the field and counts
/// 10 seconds; at the end the player leaves the world for the world selection (scene 2). "Cancel" closes it, before or during the count.
/// </summary>
public sealed class ExitWindow(WindowManager windows, ExitActions actions, UiText text, TextAttach texts,
    ITexture[] panel, ITexture[] buttons) : GameWindow(WindowManager.ExitWindow)
{
    /// <summary>
    /// ✅ The texts: the question (<c>0xecc</c>), the count (<c>0xf1c</c>, "%d"), "Close" (<c>0xf1f</c>), "Cancel" (<c>0x516</c>),
    /// "Exit" (<c>0xf1e</c>).
    /// </summary>
    public const int QuestionText = 0xecc, CountText = 0xf1c, CloseText = 0xf1f, CancelText = 0x516, ExitText = 0xf1e;

    /// <summary>✅ <c>DAT_01011c10</c> = 10.0: seconds from "Close" to leaving.</summary>
    public const double CountSeconds = 10.0;

    /// <summary>✅ The text's top, 40 px under the window's, and its colour, a light yellow (<c>0x80ffff</c>).</summary>
    private const int TextTop = 0x28;
    private const uint TextColor = 0x80FFFF;

    /// <summary>
    /// ✅ <c>0x00b81560</c>: the buttons' corners from the window's: Close (<c>+0x580</c>, 0x34, 0x49), Cancel (<c>+0x5c0</c>, 0x73),
    /// the count's Cancel (<c>+0x600</c>, 0x50; 0x73 once the server allows the exit, <c>0x00b816e0</c>) and Exit (<c>+0x640</c>, 0x34).
    /// </summary>
    private const int CloseLeft = 0x34, CancelLeft = 0x73, CountCancelLeft = 0x50, AllowedCountCancelLeft = 0x73, ExitLeft = 0x34,
        ButtonTop = 0x49;

    /// <summary>✅ The label inside a button, 8 px right and 6 px down from its corner, white with shadow.</summary>
    private const int LabelLeft = 8, LabelTop = 6;


    private readonly Button _close = new(buttons, 0, 0), _cancel = new(buttons, 0, 0), _countCancel = new(buttons, 0, 0),
        _exit = new(buttons, 0, 0);
    private ulong? _countStartMs;
    private string _message = "";

    /// <summary>✅ <c>+0x680</c>: the 10 s count runs.</summary>
    public bool Counting => _countStartMs is not null;

    /// <summary>✅ <c>+0x690</c>: the server answered the <c>0x2B</c>; the count shows "Exit" (<see cref="AllowExit"/>).</summary>
    public bool ExitAllowed { get; private set; }

    /// <summary>The count ended: the scene takes the player out of the world (✅ <c>0x00ad0ae0</c>, <c>0x00ad0bd0</c> → scene 2).</summary>
    public bool LeaveRequested { get; private set; }

    /// <summary>✅ <c>0x00b81560</c> (vtable <c>+0x20</c>, called by every opener before it opens the window) and <c>0x00b814b0</c>.</summary>
    protected override void OnOpen()
    {
        _message = texts[QuestionText];                                // ✅ 0x00b81480(0xecc)
        Width = panel.Length > 0 ? panel[0].Width : 0;
        Height = panel.Length > 0 ? panel[0].Height : 0;
        // ✅ 0x00b81560: the panel's frame 0 (214×117) centred on the screen, 0x190 − w / 2 and 0x12c − h / 2
        X = IRenderer.FrameWidth / 2 - Width / 2;
        Y = IRenderer.FrameHeight / 2 - Height / 2;
        (_close.X, _close.Y) = (X + CloseLeft, Y + ButtonTop);
        (_cancel.X, _cancel.Y) = (X + CancelLeft, Y + ButtonTop);
        (_exit.X, _exit.Y) = (X + ExitLeft, Y + ButtonTop);
        SetExitAllowed(false);                                         // ✅ 0x00b81560: +0x690 = 0
        _countStartMs = null;
    }

    /// <summary>
    /// ✅ S2C <c>0x2B</c> (<c>0xabe26d</c> → <c>0x00b816e0(1)</c>): the count's Cancel moves to the right and its "Exit" button shows.
    /// The original does it whatever the window shows; the button is only read during the count.
    /// </summary>
    public void AllowExit() => SetExitAllowed(true);

    /// <summary>✅ <c>0x00b816e0</c>: <c>+0x690</c> and the count's Cancel at x + 0x73 when allowed, x + 0x50 otherwise.</summary>
    private void SetExitAllowed(bool allowed)
    {
        ExitAllowed = allowed;
        (_countCancel.X, _countCancel.Y) = (X + (allowed ? AllowedCountCancelLeft : CountCancelLeft), Y + ButtonTop);
    }

    /// <summary>✅ <c>0x00b81420</c>: the count stops and the field is unlocked.</summary>
    protected override void OnClose()
    {
        _countStartMs = null;
        windows.Locked = false;
    }

    /// <summary>✅ <c>0x00b81450</c>: Escape closes the window while it is open (no sound: the vtable, not the manager).</summary>
    public override void Key(GameKey key)
    {
        if (key == GameKey.Escape && Visible)
            Close();
    }

    /// <summary>✅ <c>0x00b80e80</c>.</summary>
    public override void Frame(IRenderer renderer, MouseState mouse, ulong nowMs)
    {
        if (_message.Length == 0)
            return;
        if (panel.Length > 0)
            renderer.Draw(panel[0], X, Y);
        if (_countStartMs is { } start)
            FrameCount(renderer, mouse, (nowMs - start) / 1000.0);
        else
            FrameQuestion(renderer, mouse, nowMs);
    }

    /// <summary>
    /// ✅ The question: the text centred, "Close" and "Cancel". "Close" starts the count: every other window but 0xd8 closes, the
    /// <c>0x2B</c> goes out and the field locks (<c>+0x3eb0</c>). ❓ The party's <c>0x32</c> (<c>0x00ae2870</c>) waits for the party.
    /// </summary>
    private void FrameQuestion(IRenderer renderer, MouseState mouse, ulong nowMs)
    {
        DrawCentredText(renderer, _message);
        _close.Update(mouse);
        _cancel.Update(mouse);
        _close.Draw(renderer);
        _cancel.Draw(renderer);
        DrawLabel(renderer, _close, texts[CloseText]);
        DrawLabel(renderer, _cancel, texts[CancelText]);
        if (_close.Clicked)
        {
            _countStartMs = nowMs;
            foreach (var window in windows.Windows.ToArray())
                if (window != this)
                    windows.Close(window.Id);
            actions.ExitGame();
            windows.Locked = true;
        }
        else if (_cancel.Clicked)
            Close();
    }

    /// <summary>
    /// ✅ The count: "Exit after %d second(s)." with <c>10 − (int)seconds</c>, and its own "Cancel" centred under it; at 10 s the window
    /// closes and the player leaves. ❓ Cancelling opens the quest panels (window 0x17b) outside the BATTLE ZONE: they come with their step.
    /// ✅ <c>0x00b8118d</c>: once the server allowed it, the "Exit" button too (label centred, <c>0x00b61f50</c>); its click closes the
    /// window, resets <c>+0x690</c> and leaves at once, as the end of the count (<c>0x00b8100a</c>).
    /// </summary>
    private void FrameCount(IRenderer renderer, MouseState mouse, double seconds)
    {
        _message = PrintfFormat.Format(texts[CountText], 10 - (int)seconds);
        DrawCentredText(renderer, _message);
        if (seconds >= CountSeconds)
        {
            Close();
            LeaveRequested = true;
            return;
        }
        _countCancel.Update(mouse);
        _countCancel.Draw(renderer);
        DrawLabel(renderer, _countCancel, texts[CancelText]);
        if (_countCancel.Clicked)
        {
            Close();
            return;
        }
        if (!ExitAllowed)
            return;
        _exit.Update(mouse);
        _exit.Draw(renderer);
        var exitLabel = texts[ExitText];
        text.Draw(renderer, exitLabel, _exit.X + CentredOffset(_exit.Width, text.Measure(renderer, exitLabel).Width), _exit.Y + LabelTop);
        if (_exit.Clicked)
        {
            Close();
            SetExitAllowed(false);
            LeaveRequested = true;
        }
    }

    /// <summary>✅ <c>0x00b61f50</c>: <c>(outer − inner) / 2</c>, 0 when they are equal.</summary>
    private static int CentredOffset(int outer, int inner) => outer == inner ? 0 : (outer - inner) / 2;

    /// <summary>✅ <c>x + (w / 2 − textWidth / 2)</c>, each half rounded towards zero, at <see cref="TextTop"/>.</summary>
    private void DrawCentredText(IRenderer renderer, string message)
    {
        int width = text.Measure(renderer, message).Width;
        text.Draw(renderer, message, X + (Width / 2 - width / 2), Y + TextTop, TextColor);
    }

    private void DrawLabel(IRenderer renderer, Button button, string label) =>
        text.Draw(renderer, label, button.X + LabelLeft, button.Y + LabelTop);
}
