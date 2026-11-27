using DRPG.Client.Text;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;
using DRPG.Shared.Packets.Channel;

namespace DRPG.Client.Ui;

/// <summary>Pure validation rules of the secondary password dialog (✅ v322 <c>0x00c950d0</c>, <c>0x00c95000</c>).</summary>
public static class SecondaryPasswordRules
{
    public const int MinLength = 4;
    public const int MaxLength = 15;

    /// <summary>
    /// Validates the form fields in the original's order (<c>0x00c950d0</c>):
    /// returns the <c>TextAttach</c> lines to show on error, or <c>null</c> when valid.
    /// </summary>
    public static int[]? Validate(bool isChange, string oldPassword, string newPassword, string confirmPassword)
    {
        if (!string.Equals(newPassword, confirmPassword, StringComparison.OrdinalIgnoreCase))
            return [isChange ? 0x553 : 0x55c];

        if (isChange && oldPassword.Length == 0)
            return [0x54b];

        if (newPassword.Length == 0 || confirmPassword.Length == 0)
            return [isChange ? 0x54c : 0xead7];

        if (newPassword.Length < MinLength || newPassword.Length > MaxLength)
            return [0x54d, 0x54e];

        if (!HasLetterAndDigit(newPassword))
            return [0x54f, 0x54e];

        return null;
    }

    /// <summary>✅ <c>0x00c95000</c>: requires at least one ASCII letter (<c>isalpha</c>) and one ASCII digit (<c>isdigit</c>).</summary>
    public static bool HasLetterAndDigit(string value)
    {
        bool hasLetter = false, hasDigit = false;
        foreach (char c in value)
        {
            if (char.IsAsciiLetter(c))
                hasLetter = true;
            else if (char.IsAsciiDigit(c))
                hasDigit = true;
        }
        return hasLetter && hasDigit;
    }
}

/// <summary>State of <see cref="SecondaryPasswordDialog"/> (✅ v322 <c>0x00c950d0</c> field <c>+0x200</c>).</summary>
public enum SecondaryPasswordDialogState
{
    Closed = 0,
    /// <summary>✅ State 1: change existing secondary password (3 masked boxes, Check + Cancel).</summary>
    Change = 1,
    /// <summary>✅ State 2: validation error or confirm Yes/No for change.</summary>
    ConfirmChange = 2,
    /// <summary>✅ State 3: register initial secondary password (2 masked boxes, Check; Cancel when optional).</summary>
    Register = 3,
    /// <summary>✅ State 4: validation error or confirm Yes/No for register.</summary>
    ConfirmRegister = 4,
    /// <summary>✅ State 5: server reply (<c>0x15</c> S2C result 1/2/3 + Check).</summary>
    Result = 5,
}

/// <summary>Outcome of one frame of <see cref="SecondaryPasswordDialog"/>.</summary>
public abstract record SecondaryPasswordAction
{
    public sealed record None : SecondaryPasswordAction;
    public sealed record Submit(string OldPassword, string NewPassword, string ConfirmPassword) : SecondaryPasswordAction;
    public sealed record Closed : SecondaryPasswordAction;
}

/// <summary>
/// Secondary password registration and change dialog (✅ v322 <c>0x00c95000</c>–<c>0x00c96f90</c>, embedded at
/// <c>CSceneCharSel + 0x334</c>). Uses the 9-part box <c>dr2_in_000_003</c>, masked <see cref="EditBox"/> fields
/// (4..15 characters, letter + digit), <c>Tab</c> focus cycling (<c>0x00c968b0</c>), and orange hover labels.
/// </summary>
public sealed class SecondaryPasswordDialog : IDisposable
{
    private const int CheckText = 0x515, CancelText = 0x516, YesText = 0x551, NoText = 0x552;
    private const int NewTamerOldText = 0x55b;

    // ✅ State 1 (Change, 0x00c96ee0): box (290, 190) 220×220
    private const int ChangeX = 290, ChangeY = 190, ChangeW = 220, ChangeH = 220;
    private readonly EditBox _oldBox = new(ChangeX + 125, ChangeY + 75, 90, 15, SecondaryPasswordRules.MaxLength) { Masked = true };
    private readonly EditBox _newBoxChange = new(ChangeX + 125, ChangeY + 110, 90, 15, SecondaryPasswordRules.MaxLength) { Masked = true };
    private readonly EditBox _confirmBoxChange = new(ChangeX + 125, ChangeY + 145, 90, 15, SecondaryPasswordRules.MaxLength) { Masked = true };
    private readonly Button _changeCheck = new(345, 370, 45, 13);
    private readonly Button _changeCancel = new(428, 370, 45, 13);

    // ✅ State 3 (Register, 0x00c96ee0): box (290, 175) 220×250
    private const int RegisterX = 290, RegisterY = 175, RegisterW = 220, RegisterH = 250;
    private readonly EditBox _newBoxRegister = new(RegisterX + 125, RegisterY + 145, 90, 15, SecondaryPasswordRules.MaxLength) { Masked = true };
    private readonly EditBox _confirmBoxRegister = new(RegisterX + 125, RegisterY + 175, 90, 15, SecondaryPasswordRules.MaxLength) { Masked = true };
    private readonly Button _registerCheck = new(465, 395, 45, 24);
    private readonly Button _registerCancel = new(345, 395, 45, 24); // shown only when optional

    // ✅ States 2 & 4 (Confirm/Error, 0x00c96980 / 0x00c969f0): box (310, 265) 180×70
    private const int ConfirmX = 310, ConfirmY = 265, ConfirmW = 180, ConfirmH = 70;
    private readonly Button _confirmYes = new(355, 315, 45, 13);
    private readonly Button _confirmNo = new(428, 315, 45, 13);

    // ✅ State 5 (Result, 0x00c96a70): box (295, 265) 210×70, Check (390, 315) 45×24
    private const int ResultX = 295, ResultY = 265, ResultW = 210, ResultH = 70;
    private readonly Button _resultCheck = new(390, 315, 45, 24);

    private bool _mandatory;
    private bool _isChangeMode;
    private int[]? _validationError;
    private SecondaryPasswordResult _result;

    public SecondaryPasswordDialogState State { get; private set; } = SecondaryPasswordDialogState.Closed;

    public bool IsOpen => State != SecondaryPasswordDialogState.Closed;

    public bool IsMandatory => _mandatory;

    /// <summary>Opens state 3 (register initial secondary password). When <paramref name="mandatory"/> is false, Cancel/Esc close it.</summary>
    public void OpenRegister(bool mandatory)
    {
        ClearAll();
        _mandatory = mandatory;
        _isChangeMode = false;
        _validationError = null;
        _newBoxRegister.Focused = true;
        State = SecondaryPasswordDialogState.Register;
    }

    /// <summary>Opens state 1 (change existing secondary password).</summary>
    public void OpenChange()
    {
        ClearAll();
        _mandatory = false;
        _isChangeMode = true;
        _validationError = null;
        _oldBox.Focused = true;
        State = SecondaryPasswordDialogState.Change;
    }

    /// <summary>✅ <c>0x00c96a70</c>: opens state 5 with the server's <c>0x15</c> reply result.</summary>
    public void ShowResult(SecondaryPasswordResult result)
    {
        ClearAll();
        _result = result;
        State = SecondaryPasswordDialogState.Result;
    }

    public void Close()
    {
        ClearAll();
        _validationError = null;
        _mandatory = false;
        State = SecondaryPasswordDialogState.Closed;
    }

    public void Handle(InputEvent input)
    {
        if (!IsOpen)
            return;

        if (input is KeyEvent { Key: GameKey.Escape } && !_mandatory)
        {
            Close();
            return;
        }

        if (State is not (SecondaryPasswordDialogState.Change or SecondaryPasswordDialogState.Register))
            return;

        var boxes = ActiveBoxes();
        switch (input)
        {
            case KeyEvent { Key: GameKey.Tab }:
                CycleFocus(boxes);
                break;
            case KeyEvent { Key: GameKey.Backspace }:
                FocusedBox(boxes)?.Backspace();
                break;
            case TextEvent text:
                FocusedBox(boxes)?.Type(text.Text);
                break;
            case MouseButtonEvent { Down: true } click:
                foreach (var box in boxes)
                    box.Focused = box.Contains(click.X, click.Y);
                break;
        }
    }

    public SecondaryPasswordAction Frame(IRenderer renderer, MessageBox box, UiText uiText, TextAttach texts, MouseState mouse, ulong nowMs = 0)
    {
        switch (State)
        {
            case SecondaryPasswordDialogState.Change:
                return FrameChange(renderer, box, uiText, texts, mouse, nowMs);
            case SecondaryPasswordDialogState.Register:
                return FrameRegister(renderer, box, uiText, texts, mouse, nowMs);
            case SecondaryPasswordDialogState.ConfirmChange:
            case SecondaryPasswordDialogState.ConfirmRegister:
                return FrameConfirm(renderer, box, uiText, texts, mouse);
            case SecondaryPasswordDialogState.Result:
                return FrameResult(renderer, box, uiText, texts, mouse);
            default:
                return new SecondaryPasswordAction.None();
        }
    }

    private SecondaryPasswordAction FrameChange(IRenderer renderer, MessageBox box, UiText uiText, TextAttach texts, MouseState mouse, ulong nowMs)
    {
        box.DrawBox(renderer, ChangeX, ChangeY, ChangeW, ChangeH);
        uiText.Draw(renderer, texts[0x544], ChangeX + 15, ChangeY + 20, UiText.White, shadow: false);
        uiText.Draw(renderer, texts[0x545], ChangeX + 15, ChangeY + 40, UiText.White, shadow: false);
        uiText.Draw(renderer, texts[0x548], 305, 265, UiText.White, shadow: false);
        uiText.Draw(renderer, texts[0x549], 305, 300, UiText.White, shadow: false);
        uiText.Draw(renderer, texts[0x54a], 305, 335, UiText.White, shadow: false);

        _oldBox.Draw(renderer, uiText, nowMs);
        _newBoxChange.Draw(renderer, uiText, nowMs);
        _confirmBoxChange.Draw(renderer, uiText, nowMs);

        if (DrawLabelButton(renderer, _changeCheck, texts[CheckText], uiText, mouse))
        {
            _validationError = SecondaryPasswordRules.Validate(true, _oldBox.Text, _newBoxChange.Text, _confirmBoxChange.Text);
            State = SecondaryPasswordDialogState.ConfirmChange;
        }
        else if (DrawLabelButton(renderer, _changeCancel, texts[CancelText], uiText, mouse))
        {
            Close();
            return new SecondaryPasswordAction.Closed();
        }
        return new SecondaryPasswordAction.None();
    }

    private SecondaryPasswordAction FrameRegister(IRenderer renderer, MessageBox box, UiText uiText, TextAttach texts, MouseState mouse, ulong nowMs)
    {
        box.DrawBox(renderer, RegisterX, RegisterY, RegisterW, RegisterH);
        uiText.Draw(renderer, texts[0x544], RegisterX + 15, RegisterY + 20, UiText.White, shadow: false);
        uiText.Draw(renderer, texts[0x555], RegisterX + 15, RegisterY + 40, UiText.White, shadow: false);
        uiText.Draw(renderer, texts[0x556], RegisterX + 15, RegisterY + 60, UiText.White, shadow: false);
        uiText.Draw(renderer, texts[0x557], RegisterX + 15, RegisterY + 80, UiText.White, shadow: false);
        uiText.Draw(renderer, texts[0x558], RegisterX + 15, RegisterY + 100, UiText.White, shadow: false);
        uiText.Draw(renderer, texts[0x559], 315, 320, UiText.White, shadow: false);
        uiText.Draw(renderer, texts[0x55a], 315, 350, UiText.White, shadow: false);

        _newBoxRegister.Draw(renderer, uiText, nowMs);
        _confirmBoxRegister.Draw(renderer, uiText, nowMs);

        if (DrawLabelButton(renderer, _registerCheck, texts[CheckText], uiText, mouse))
        {
            _validationError = SecondaryPasswordRules.Validate(false, "", _newBoxRegister.Text, _confirmBoxRegister.Text);
            State = SecondaryPasswordDialogState.ConfirmRegister;
        }
        else if (!_mandatory && DrawLabelButton(renderer, _registerCancel, texts[CancelText], uiText, mouse))
        {
            Close();
            return new SecondaryPasswordAction.Closed();
        }
        return new SecondaryPasswordAction.None();
    }

    private SecondaryPasswordAction FrameConfirm(IRenderer renderer, MessageBox box, UiText uiText, TextAttach texts, MouseState mouse)
    {
        box.DrawBox(renderer, ConfirmX, ConfirmY, ConfirmW, ConfirmH);
        if (_validationError is { } errorLines)
        {
            for (int i = 0; i < errorLines.Length; i++)
                uiText.Draw(renderer, texts[errorLines[i]], ConfirmX + 15, ConfirmY + 15 + i * 15, UiText.White, shadow: false);
            if (DrawLabelButton(renderer, _confirmNo, texts[CheckText], uiText, mouse))
                ReopenForm();
            return new SecondaryPasswordAction.None();
        }

        uiText.Draw(renderer, texts[0x550], ConfirmX + 30, ConfirmY + 20, UiText.White, shadow: false);
        if (DrawLabelButton(renderer, _confirmYes, texts[YesText], uiText, mouse))
        {
            string oldVal = _isChangeMode ? DeleteCharacterDialog.ComputePasswordMd5(_oldBox.Text) : texts[NewTamerOldText];
            string newVal = DeleteCharacterDialog.ComputePasswordMd5(_isChangeMode ? _newBoxChange.Text : _newBoxRegister.Text);
            string confirmVal = DeleteCharacterDialog.ComputePasswordMd5(_isChangeMode ? _confirmBoxChange.Text : _confirmBoxRegister.Text);
            ClearAll();
            State = SecondaryPasswordDialogState.Closed;
            return new SecondaryPasswordAction.Submit(oldVal, newVal, confirmVal);
        }
        if (DrawLabelButton(renderer, _confirmNo, texts[NoText], uiText, mouse))
        {
            if (_isChangeMode)
            {
                Close();
                return new SecondaryPasswordAction.Closed();
            }
            ReopenForm();
        }
        return new SecondaryPasswordAction.None();
    }

    private SecondaryPasswordAction FrameResult(IRenderer renderer, MessageBox box, UiText uiText, TextAttach texts, MouseState mouse)
    {
        box.DrawBox(renderer, ResultX, ResultY, ResultW, ResultH);
        int textId = _result switch
        {
            SecondaryPasswordResult.Success => 0x55d,
            SecondaryPasswordResult.InvalidNewPassword => 0x55e,
            SecondaryPasswordResult.InvalidOldPassword => 0x55f,
            _ => 0x55e,
        };
        uiText.Draw(renderer, texts[textId], ResultX + 15, ResultY + 20, UiText.White, shadow: false);
        if (DrawLabelButton(renderer, _resultCheck, texts[CheckText], uiText, mouse))
        {
            if (_result == SecondaryPasswordResult.Success)
            {
                Close();
                return new SecondaryPasswordAction.Closed();
            }
            ReopenForm();
        }
        return new SecondaryPasswordAction.None();
    }

    private void ReopenForm()
    {
        if (_isChangeMode)
            OpenChange();
        else
            OpenRegister(_mandatory);
    }

    private EditBox[] ActiveBoxes() => State == SecondaryPasswordDialogState.Change
        ? [_oldBox, _newBoxChange, _confirmBoxChange]
        : [_newBoxRegister, _confirmBoxRegister];

    private static EditBox? FocusedBox(EditBox[] boxes)
    {
        foreach (var box in boxes)
        {
            if (box.Focused)
                return box;
        }
        return null;
    }

    private static void CycleFocus(EditBox[] boxes)
    {
        int current = -1;
        for (int i = 0; i < boxes.Length; i++)
        {
            if (boxes[i].Focused)
                current = i;
            boxes[i].Focused = false;
        }
        boxes[(current + 1) % boxes.Length].Focused = true;
    }

    private void ClearAll()
    {
        _oldBox.Clear();
        _oldBox.Focused = false;
        _newBoxChange.Clear();
        _newBoxChange.Focused = false;
        _confirmBoxChange.Clear();
        _confirmBoxChange.Focused = false;
        _newBoxRegister.Clear();
        _newBoxRegister.Focused = false;
        _confirmBoxRegister.Clear();
        _confirmBoxRegister.Focused = false;
    }

    private static bool DrawLabelButton(IRenderer renderer, Button button, string label, UiText uiText, MouseState mouse)
    {
        button.Update(mouse, enabled: true);
        uint color = button.State != 0 ? UiText.Orange : UiText.White;
        uiText.Draw(renderer, label, button.X, button.Y, color, shadow: false);
        return button.Clicked;
    }

    public void Dispose()
    {
        _oldBox.Dispose();
        _newBoxChange.Dispose();
        _confirmBoxChange.Dispose();
        _newBoxRegister.Dispose();
        _confirmBoxRegister.Dispose();
    }
}
