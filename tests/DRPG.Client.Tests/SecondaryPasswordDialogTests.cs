using DRPG.Client.Text;
using DRPG.Client.Ui;
using DRPG.Engine.Platform;
using DRPG.Shared.Packets.Channel;

namespace DRPG.Client.Tests;

/// <summary>Unit tests for <see cref="SecondaryPasswordRules"/>, <see cref="SecondaryPasswordDialog"/> and <see cref="DeleteCharacterDialog"/> state 0.</summary>
public sealed class SecondaryPasswordDialogTests
{
    private static TextAttach Texts()
    {
        var t = new TextAttach();
        t.AddTableRows([
            [$"{0x515}", "Check"],
            [$"{0x516}", "Cancel"],
            [$"{0x551}", "Yes"],
            [$"{0x552}", "No"],
            [$"{0x55b}", "You are a New Tamer."],
            [$"{0xf79}", "Please enter the secondary password."],
            [$"{0xf7d}", "Are you sure you want†to delete?"],
        ]);
        return t;
    }

    [Fact]
    public void Secondary_password_rules_enforce_length_letter_digit_and_confirmation()
    {
        Assert.Equal<int>([0x55c], SecondaryPasswordRules.Validate(false, "", "abc1", "abc2")!);
        Assert.Equal<int>([0x553], SecondaryPasswordRules.Validate(true, "old1", "abc1", "abc2")!);
        Assert.Equal<int>([0x54b], SecondaryPasswordRules.Validate(true, "", "abc1", "abc1")!);
        Assert.Equal<int>([0xead7], SecondaryPasswordRules.Validate(false, "", "", "")!);
        Assert.Equal<int>([0x54c], SecondaryPasswordRules.Validate(true, "old1", "", "")!);
        Assert.Equal<int>([0x54d, 0x54e], SecondaryPasswordRules.Validate(false, "", "ab1", "ab1")!);
        Assert.Equal<int>([0x54d, 0x54e], SecondaryPasswordRules.Validate(false, "", "abcdefghijklmno1", "abcdefghijklmno1")!);
        Assert.Equal<int>([0x54f, 0x54e], SecondaryPasswordRules.Validate(false, "", "abcdef", "abcdef")!);
        Assert.Equal<int>([0x54f, 0x54e], SecondaryPasswordRules.Validate(false, "", "123456", "123456")!);
        Assert.Null(SecondaryPasswordRules.Validate(false, "", "abc1", "ABC1"));
        Assert.Null(SecondaryPasswordRules.Validate(true, "old1", "Pass2026", "Pass2026"));
    }

    [Fact]
    public void Secondary_password_dialog_mandatory_register_blocks_escape_and_submits_md5()
    {
        var renderer = new FakeRenderer();
        var parts = Enumerable.Range(0, MessageBox.FrameCount).Select(_ => (DRPG.Engine.Rendering.ITexture)new FakeTexture(14, 14)).ToArray();
        var uiText = new UiText(null);
        var box = new MessageBox(parts, uiText);
        var texts = Texts();
        var mouse = new MouseState();

        using var dialog = new SecondaryPasswordDialog();
        dialog.OpenRegister(mandatory: true);
        Assert.Equal(SecondaryPasswordDialogState.Register, dialog.State);

        // Mandatory: Escape cannot close it
        dialog.Handle(new KeyEvent(GameKey.Escape));
        Assert.True(dialog.IsOpen);

        // Type new password, Tab to confirm, type same password
        dialog.Handle(new TextEvent("hero123"));
        dialog.Handle(new KeyEvent(GameKey.Tab));
        dialog.Handle(new TextEvent("hero123"));

        // Click Check at (470, 400) -> ConfirmRegister
        mouse.Handle(new MouseButtonEvent(470, 400, true), 10);
        dialog.Frame(renderer, box, uiText, texts, mouse, 10);
        mouse.EndFrame();
        mouse.Handle(new MouseButtonEvent(470, 400, false), 20);
        dialog.Frame(renderer, box, uiText, texts, mouse, 20);
        mouse.EndFrame();
        Assert.Equal(SecondaryPasswordDialogState.ConfirmRegister, dialog.State);

        // Click Yes at (360, 320) -> Submit with TextAttach[0x55b] and MD5s
        mouse.Handle(new MouseButtonEvent(360, 320, true), 30);
        dialog.Frame(renderer, box, uiText, texts, mouse, 30);
        mouse.EndFrame();
        mouse.Handle(new MouseButtonEvent(360, 320, false), 40);
        var action = dialog.Frame(renderer, box, uiText, texts, mouse, 40);
        var submit = Assert.IsType<SecondaryPasswordAction.Submit>(action);
        Assert.Equal("You are a New Tamer.", submit.OldPassword);
        Assert.Equal(DeleteCharacterDialog.ComputePasswordMd5("hero123"), submit.NewPassword);
        Assert.Equal(submit.NewPassword, submit.ConfirmPassword);

        // Show server success reply -> Check closes
        dialog.ShowResult(SecondaryPasswordResult.Success);
        Assert.Equal(SecondaryPasswordDialogState.Result, dialog.State);
        mouse.EndFrame();
        mouse.Handle(new MouseButtonEvent(400, 325, true), 50);
        dialog.Frame(renderer, box, uiText, texts, mouse, 50);
        mouse.EndFrame();
        mouse.Handle(new MouseButtonEvent(400, 325, false), 60);
        Assert.IsType<SecondaryPasswordAction.Closed>(dialog.Frame(renderer, box, uiText, texts, mouse, 60));
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void Optional_register_allows_escape_and_delete_state_0_hashes_entered_password()
    {
        using var spDialog = new SecondaryPasswordDialog();
        spDialog.OpenRegister(mandatory: false);
        spDialog.Handle(new KeyEvent(GameKey.Escape));
        Assert.False(spDialog.IsOpen);

        using var delDialog = new DeleteCharacterDialog();
        delDialog.Open(requirePassword: true);
        Assert.Equal(DeleteDialogState.EnterPassword, delDialog.State);
        delDialog.Handle(new TextEvent("secret9"));
        delDialog.Handle(new KeyEvent(GameKey.Enter));
        Assert.Equal(DeleteDialogState.Confirm, delDialog.State);
        Assert.Equal(DeleteCharacterDialog.ComputePasswordMd5("secret9"), delDialog.TakePasswordMd5());
    }
}
