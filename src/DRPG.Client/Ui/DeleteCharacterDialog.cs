using System.Security.Cryptography;
using System.Text;
using DRPG.Client.Text;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Ui;

/// <summary>State of <see cref="DeleteCharacterDialog"/> (✅ v322 <c>0x01035d00</c> field <c>+0x308</c>).</summary>
public enum DeleteDialogState
{
    Closed = -1,
    /// <summary>✅ State 1: secondary password mismatch (<c>0xf7c</c>, Check button <c>0x515</c>).</summary>
    PasswordMismatch = 1,
    /// <summary>✅ State 2: delete confirmation (<c>0xf7d</c>, Yes <c>0x551</c> / No <c>0x552</c>).</summary>
    Confirm = 2,
}

/// <summary>Outcome of one frame of <see cref="DeleteCharacterDialog"/>.</summary>
public enum DeleteDialogAction
{
    None,
    ConfirmDelete,
    Closed,
}

/// <summary>
/// The character deletion confirmation dialog of <c>CSceneCharSel</c> (✅ v322 class vtable <c>0x01035d00</c>,
/// <c>0x00c09e00</c>, <c>0x00c0a5c0</c>, <c>0x00c0a540</c>, docs/cliente/cenas.md#apagar-personagem--re).
/// State 0 (secondary password prompt) is unreachable in v322 and intentionally not implemented; clicking Delete jumps
/// straight to state 2 (Yes/No) and sends the MD5 of <c>""</c>. State 1 is shown when the server replies with error 2000.
/// </summary>
public sealed class DeleteCharacterDialog
{
    // ✅ TextAttach ids
    public const int ConfirmText = 0xf7d;
    public const int MismatchText = 0xf7c;
    public const int YesText = 0x551;
    public const int NoText = 0x552;
    public const int CheckText = 0x515;

    // ✅ State 2 (0x00c0a5c0): box (310, 265) 180×70, Yes (355, 315) 45×13, No (428, 315) 45×13
    private const int ConfirmBoxX = 310, ConfirmBoxY = 265, ConfirmBoxW = 180, ConfirmBoxH = 70;
    private const int ConfirmLine0X = 340, ConfirmLine0Y = 285, ConfirmLine1X = 370, ConfirmLine1Y = 300;
    private readonly Button _yes = new(355, 315, 45, 13);
    private readonly Button _no = new(428, 315, 45, 13);

    // ✅ State 1: box (295, 265) 210×70, Check (390, 315) 45×24
    private const int MismatchBoxX = 295, MismatchBoxY = 265, MismatchBoxW = 210, MismatchBoxH = 70;
    private const int MismatchTextX = 310, MismatchTextY = 285;
    private readonly Button _check = new(390, 315, 45, 24);

    public DeleteDialogState State { get; private set; } = DeleteDialogState.Closed;

    public bool IsOpen => State != DeleteDialogState.Closed;

    /// <summary>✅ Opens straight into state 2 (confirmation), as v322 does in <c>0x00b48c60</c> → <c>0x00c0a5c0</c>.</summary>
    public void OpenConfirm() => State = DeleteDialogState.Confirm;

    /// <summary>✅ Opens state 1 (secondary password mismatch) on <c>0x11</c> error 2000.</summary>
    public void OpenPasswordMismatch() => State = DeleteDialogState.PasswordMismatch;

    public void Close() => State = DeleteDialogState.Closed;

    /// <summary>✅ <c>0x00c0a540</c>: Escape closes the dialog.</summary>
    public void Handle(InputEvent input)
    {
        if (IsOpen && input is KeyEvent { Key: GameKey.Escape })
            Close();
    }

    /// <summary>
    /// Draws the active state and processes clicks. Button labels are white normally and orange (<c>0x4080ff</c>) under
    /// the mouse (✅ <c>0x00b55640</c>).
    /// </summary>
    public DeleteDialogAction Frame(IRenderer renderer, MessageBox box, UiText uiText, TextAttach texts, MouseState mouse)
    {
        switch (State)
        {
            case DeleteDialogState.Confirm:
                box.DrawBox(renderer, ConfirmBoxX, ConfirmBoxY, ConfirmBoxW, ConfirmBoxH);
                var lines = texts[ConfirmText].Split('†');
                if (lines.Length > 0)
                    uiText.Draw(renderer, lines[0], ConfirmLine0X, ConfirmLine0Y, UiText.White, shadow: false);
                if (lines.Length > 1)
                    uiText.Draw(renderer, lines[1], ConfirmLine1X, ConfirmLine1Y, UiText.White, shadow: false);
                if (DrawLabelButton(renderer, _yes, texts[YesText], uiText, mouse))
                {
                    Close();
                    return DeleteDialogAction.ConfirmDelete;
                }
                if (DrawLabelButton(renderer, _no, texts[NoText], uiText, mouse))
                {
                    Close();
                    return DeleteDialogAction.Closed;
                }
                return DeleteDialogAction.None;

            case DeleteDialogState.PasswordMismatch:
                box.DrawBox(renderer, MismatchBoxX, MismatchBoxY, MismatchBoxW, MismatchBoxH);
                uiText.Draw(renderer, texts[MismatchText], MismatchTextX, MismatchTextY, UiText.White, shadow: false);
                if (DrawLabelButton(renderer, _check, texts[CheckText], uiText, mouse))
                {
                    Close();
                    return DeleteDialogAction.Closed;
                }
                return DeleteDialogAction.None;

            default:
                return DeleteDialogAction.None;
        }
    }

    /// <summary>
    /// ✅ <c>0x00869140</c>: lowercase hex MD5 of the password box text (empty string in v322 →
    /// <c>d41d8cd98f00b204e9800998ecf8427e</c>). Never log the input or result.
    /// </summary>
    public static string ComputePasswordMd5(string password) =>
        Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(password)));

    private static bool DrawLabelButton(IRenderer renderer, Button button, string label, UiText uiText, MouseState mouse)
    {
        button.Update(mouse, enabled: true);
        uint color = button.State != 0 ? UiText.Orange : UiText.White;
        uiText.Draw(renderer, label, button.X, button.Y, color, shadow: false);
        return button.Clicked;
    }
}
