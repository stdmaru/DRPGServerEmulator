using DRPG.Client.Net;
using DRPG.Client.Scenes;
using DRPG.Client.Text;
using DRPG.Client.Ui;
using DRPG.Engine.Platform;
using DRPG.Shared.Packets.Channel;

namespace DRPG.Client.Tests;

/// <summary>
/// Unit tests for Phase E: <see cref="ProhibitedWords"/>, <see cref="CharacterNameRules"/>, <see cref="EditBox"/>,
/// 9-part <see cref="MessageBox.DrawBox"/>, <see cref="DeleteCharacterDialog"/>, and <see cref="CharacterSelectScene"/>
/// creation/deletion flows and error replies (102, 2000, 105).
/// </summary>
public sealed class CharacterCreateDeleteTests
{
    private static CharacterListSlot EmptySlot(byte index) => CharacterListSlot.Empty(index, 100 + index);

    private static CharacterListSlot OccupiedSlot(byte index, uint uid = 10, string name = "TamerA") =>
        new(uid, 1, name, 1, 10, 10, 10, 1, "Gigimon", 0, 0, index, 0x4F, 100 + index);

    [Fact]
    public void Prohibited_words_loads_table_and_language_override_and_matches_any_uppercase_substring()
    {
        var words = new ProhibitedWords();
        words.AddTableRows([["1", "admin"], ["2", "gm"], ["bad", "x"], ["-1", "y"], ["3"]]);
        Assert.Equal(2, words.Count);
        Assert.True(words.ContainsProhibited("SuperAdmin99"));
        Assert.True(words.ContainsProhibited("xGMy"));
        Assert.False(words.ContainsProhibited("Marcus"));

        // Language file overrides id 2 with another word
        words.AddLanguageFile("\uFEFF2|MOD\r4|BADWORD\n\rbroken\r|empty\r99999999999|x"u8);
        Assert.False(words.ContainsProhibited("xGMy"));
        Assert.True(words.ContainsProhibited("myModName"));
        Assert.True(words.ContainsProhibited("xxbadwordyy"));
    }

    [Fact]
    public void Prohibited_words_survives_hostile_binary_input()
    {
        var words = new ProhibitedWords();
        words.AddLanguageFile(new byte[] { 0xFF, 0xFE, 0x00, (byte)'|', 0x0D, 0x7C, 0x7C });
        Assert.False(words.ContainsProhibited("NormalName"));
    }

    [Fact]
    public void Name_validation_enforces_exact_order_limits_non_ascii_and_prohibited_substrings()
    {
        var words = new ProhibitedWords();
        words.AddTableRows([["1", "GM"]]);

        // 1. Non-ASCII in either name comes first and focuses Tamer (0xee5)
        Assert.Equal(new NameValidationError(CharacterNameRules.NonAsciiText, NameField.Tamer),
            CharacterNameRules.Validate("Tameré", "Valid1", words));
        Assert.Equal(new NameValidationError(CharacterNameRules.NonAsciiText, NameField.Tamer),
            CharacterNameRules.Validate("", "Digião", words));

        // 2. Tamer empty or non-alphanumeric (0x25e)
        Assert.Equal(new NameValidationError(CharacterNameRules.TamerInvalidCharText, NameField.Tamer),
            CharacterNameRules.Validate("", "Valid1", words));
        Assert.Equal(new NameValidationError(CharacterNameRules.TamerInvalidCharText, NameField.Tamer),
            CharacterNameRules.Validate("Bad_Name", "Valid1", words));

        // 3. Tamer prohibited word in the middle (0x25f) — checked before length
        Assert.Equal(new NameValidationError(CharacterNameRules.TamerProhibitedText, NameField.Tamer),
            CharacterNameRules.Validate("gm", "Valid1", words));
        Assert.Equal(new NameValidationError(CharacterNameRules.TamerProhibitedText, NameField.Tamer),
            CharacterNameRules.Validate("SuperGmHero", "Valid1", words));

        // 4. Tamer length bounds: 2 and 17 fail (0x260); 3 and 16 pass
        Assert.Equal(new NameValidationError(CharacterNameRules.TamerLengthText, NameField.Tamer),
            CharacterNameRules.Validate("ab", "Valid1", words));
        Assert.Equal(new NameValidationError(CharacterNameRules.TamerLengthText, NameField.Tamer),
            CharacterNameRules.Validate(new string('a', 17), "Valid1", words));
        Assert.Null(CharacterNameRules.Validate("abc", "def", words));
        Assert.Null(CharacterNameRules.Validate(new string('a', 16), new string('b', 16), words));

        // 5-7. Digimon checks (0x261, 0x262, 0x263) focus Digimon
        Assert.Equal(new NameValidationError(CharacterNameRules.DigimonInvalidCharText, NameField.Digimon),
            CharacterNameRules.Validate("Marcus", "", words));
        Assert.Equal(new NameValidationError(CharacterNameRules.DigimonInvalidCharText, NameField.Digimon),
            CharacterNameRules.Validate("Marcus", "Agumon!", words));
        Assert.Equal(new NameValidationError(CharacterNameRules.DigimonProhibitedText, NameField.Digimon),
            CharacterNameRules.Validate("Marcus", "xGMy", words));
        Assert.Equal(new NameValidationError(CharacterNameRules.DigimonLengthText, NameField.Digimon),
            CharacterNameRules.Validate("Marcus", "ab", words));
        Assert.Equal(new NameValidationError(CharacterNameRules.DigimonLengthText, NameField.Digimon),
            CharacterNameRules.Validate("Marcus", new string('z', 17), words));
    }

    [Fact]
    public void Target_slot_picks_selected_if_empty_otherwise_lowest_empty_or_minus_one()
    {
        var slots = new[] { OccupiedSlot(0), EmptySlot(1), EmptySlot(2), OccupiedSlot(3) };
        Assert.Equal(2, CharacterNameRules.PickTargetSlot(slots, selectedSlot: 2));
        Assert.Equal(1, CharacterNameRules.PickTargetSlot(slots, selectedSlot: 0));
        Assert.Equal(1, CharacterNameRules.PickTargetSlot(slots, selectedSlot: -1));

        var full = new[] { OccupiedSlot(0), OccupiedSlot(1), OccupiedSlot(2), OccupiedSlot(3) };
        Assert.Equal(-1, CharacterNameRules.PickTargetSlot(full, selectedSlot: 0));
        Assert.Equal(-1, CharacterNameRules.PickTargetSlot(full, selectedSlot: -1));
    }

    [Theory]
    [InlineData(0, (ushort)24)]
    [InlineData(1, (ushort)27)]
    [InlineData(2, (ushort)31)]
    [InlineData(3, (ushort)40)]
    [InlineData(-1, null)]
    [InlineData(4, null)]
    public void Fourth_starter_maps_server_index_to_species(int index, ushort? expected) =>
        Assert.Equal(expected, CharacterNameRules.FourthStarterSpecies(index));

    [Fact]
    public void Create_header_uses_injected_random_in_exact_range()
    {
        var fixedRandom = new FixedRandom(0, 3, 3, 0);
        var h1 = CharacterNameRules.BuildCreateHeader(2, fixedRandom);
        Assert.Equal(new CharacterCreateHeader(2, 0x4F, 62, 58), h1);

        var h2 = CharacterNameRules.BuildCreateHeader(0, fixedRandom);
        Assert.Equal(new CharacterCreateHeader(0, 0x4F, 59, 55), h2);
    }

    [Fact]
    public void Edit_box_accepts_non_ascii_respects_max_length_and_backspace()
    {
        using var box = new EditBox(153, 57, 130, 15, maxLength: 5);
        Assert.True(box.Contains(153, 57));
        Assert.False(box.Contains(152, 57));
        Assert.False(box.Contains(283, 57));

        box.Type("Ab\t\r\né!");
        Assert.Equal("Abé!", box.Text);

        box.Type("12345");
        Assert.Equal("Abé!1", box.Text);

        box.Backspace();
        Assert.Equal("Abé!", box.Text);

        box.Clear();
        Assert.Equal("", box.Text);
        box.Backspace();
        Assert.Equal("", box.Text);
    }

    [Fact]
    public void Nine_slice_draw_box_draws_top_middle_and_bottom_rows()
    {
        var renderer = new FakeRenderer();
        var parts = Enumerable.Range(0, MessageBox.FrameCount).Select(_ => (DRPG.Engine.Rendering.ITexture)new FakeTexture(14, 14)).ToArray();
        var box = new MessageBox(parts, new UiText(null));

        // State 2 box: (310, 265) 180×70 -> repeats = (180-14)/7 = 23, middleRows = (70-28)/14 + 1 = 4
        box.DrawBox(renderer, 310, 265, 180, 70);

        // 1 top + 4 middle + 1 bottom = 6 rows; each row has 1 left + 24 middle + 1 right = 26 draws
        Assert.Equal(6 * 26, renderer.Draws.Count);
        Assert.Equal((310, 265), (renderer.Draws[0].X, renderer.Draws[0].Y));
        // Right corner of top row at 310 + 14 + 7 * 23 = 485
        Assert.Equal((485, 265), (renderer.Draws[25].X, renderer.Draws[25].Y));
        // Bottom row at 265 + 5 * 14 = 335
        Assert.Equal(335, renderer.Draws[^1].Y);
    }

    [Fact]
    public void Delete_dialog_handles_confirm_cancel_escape_mismatch_and_empty_md5()
    {
        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", DeleteCharacterDialog.ComputePasswordMd5(""));

        var renderer = new FakeRenderer();
        var parts = Enumerable.Range(0, MessageBox.FrameCount).Select(_ => (DRPG.Engine.Rendering.ITexture)new FakeTexture(14, 14)).ToArray();
        var uiText = new UiText(null);
        var box = new MessageBox(parts, uiText);
        var texts = new TextAttach();
        texts.AddTableRows([["3965", "Are you sure you want†to delete?"], ["3964", "Secondary password mismatch."], ["1361", "Yes"], ["1362", "No"], ["1301", "Check"]]);
        var mouse = new MouseState();
        var dialog = new DeleteCharacterDialog();

        // Open confirm (state 2) -> click Yes at (360, 320)
        dialog.OpenConfirm();
        Assert.True(dialog.IsOpen);
        Assert.Equal(DeleteDialogState.Confirm, dialog.State);
        mouse.Handle(new MouseButtonEvent(360, 320, true), 10);
        Assert.Equal(DeleteDialogAction.None, dialog.Frame(renderer, box, uiText, texts, mouse));
        mouse.EndFrame();
        mouse.Handle(new MouseButtonEvent(360, 320, false), 20);
        Assert.Equal(DeleteDialogAction.ConfirmDelete, dialog.Frame(renderer, box, uiText, texts, mouse));
        Assert.False(dialog.IsOpen);
        mouse.EndFrame();

        // Open confirm -> click No at (435, 320)
        dialog.OpenConfirm();
        mouse.Handle(new MouseButtonEvent(435, 320, true), 30);
        dialog.Frame(renderer, box, uiText, texts, mouse);
        mouse.EndFrame();
        mouse.Handle(new MouseButtonEvent(435, 320, false), 40);
        Assert.Equal(DeleteDialogAction.Closed, dialog.Frame(renderer, box, uiText, texts, mouse));
        Assert.False(dialog.IsOpen);
        mouse.EndFrame();

        // Open confirm -> Escape closes
        dialog.OpenConfirm();
        dialog.Handle(new KeyEvent(GameKey.Escape));
        Assert.False(dialog.IsOpen);

        // Open password mismatch (state 1) -> click Check at (400, 325)
        dialog.OpenPasswordMismatch();
        Assert.Equal(DeleteDialogState.PasswordMismatch, dialog.State);
        mouse.Handle(new MouseButtonEvent(400, 325, true), 50);
        dialog.Frame(renderer, box, uiText, texts, mouse);
        mouse.EndFrame();
        mouse.Handle(new MouseButtonEvent(400, 325, false), 60);
        Assert.Equal(DeleteDialogAction.Closed, dialog.Frame(renderer, box, uiText, texts, mouse));
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void Scene_handles_error_replies_102_2000_and_105()
    {
        using var fixture = new SceneFixture();
        var texts = new TextAttach();
        texts.AddTableRows([
            [$"{CharacterSelectScene.NameTakenText}", "The Tamer name already exists."],
            [$"{CharacterSelectScene.ConsignmentActiveText}", "Your Consignment Store is currently active."],
        ]);
        var table = DRPG.Shared.Formats.Pack.PackArchive.Open(fixture.Game.Install.TablePath);
        fixture.Game.Data = new DRPG.Client.Startup.GameData(
            fixture.Resource, table, new Dictionary<string, IReadOnlyList<string[]>>(),
            new Dictionary<string, byte[]>(), texts, new ProhibitedWords(),
            new Dictionary<string, DRPG.Shared.Formats.Sprite.SpriteFile>());

        using var scene = new CharacterSelectScene(fixture.Game, new FixedRandom(0));
        scene.Init();
        scene.CreatePanel.Enter(targetSlot: 0, fourthStarter: 0);
        scene.Handle(new TextEvent("TakenName"));
        scene.CreatePanel.FocusField(NameField.Digimon);

        // 102 (NameTaken): message 0xee4, clears and focuses tamer box
        scene.HandleCreateError(ChannelError.NameTaken);
        Assert.Equal("The Tamer name already exists.", scene.ActiveMessage);
        Assert.Equal("", scene.CreatePanel.TamerName);
        Assert.True(scene.CreatePanel.TamerNameFocused);

        // 2000: opens delete dialog state 1 (PasswordMismatch)
        scene.HandleDeleteError(CharacterSelectScene.DeletePasswordMismatchError);
        Assert.Equal(DeleteDialogState.PasswordMismatch, scene.DeleteDialog.State);

        // 105: shows 0xeb1a message
        scene.HandleDeleteError(CharacterSelectScene.DeleteConsignmentActiveError);
        Assert.Equal("Your Consignment Store is currently active.", scene.ActiveMessage);
    }

    private sealed class FixedRandom(params int[] values) : Random
    {
        private int _index;

        public override int Next(int maxValue)
        {
            int value = values[_index % values.Length];
            _index++;
            return value % maxValue;
        }
    }
}
