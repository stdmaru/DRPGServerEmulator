using DRPG.Client.Data;
using DRPG.Client.Net;
using DRPG.Client.Text;
using DRPG.Client.Ui;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;
using DRPG.Shared.Packets.Channel;

namespace DRPG.Client.Scenes;

/// <summary>
/// <c>CSceneCharSel</c>, the character selection (✅ v322 <c>0x00b43f20</c>, docs/cliente/cenas.md#cscenecharsel-seleção-de-personagens-):
/// mode 0 shows the four slots with the tamer and digimon animated, their texts, and the Delete / Create / Exchange
/// Password / Game Start / Back buttons plus the delete confirmation dialog; mode 1 is character creation
/// (<see cref="CharacterCreatePanel"/>). Asks for the list (<c>0x12</c> + <c>0x14</c>) when it opens; Back returns to
/// the world selection; Game Start fades out and logs the map server (entering the map is milestone 3).
/// ❓ Not done here: Exchange Password (next milestone: click is only logged) and the sprite shadow option.
/// </summary>
public sealed class CharacterSelectScene(GameContext game, Random random) : IScene
{
    private const int SlotCount = CharacterListReply.SlotCount;
    private const int FramesPerSecond = 30;
    private const int SelectSound = 5;

    // ✅ docs/cliente/cenas.md#init-0x00b47b30 (the buttons, `0x009b75a0(x, y, sprite)`)
    private static readonly (int X, int Y)[] SlotButtons = [(26, 66), (425, 66), (26, 301), (425, 301)];
    private static readonly string[] SlotSprites = ["dr2_ch_000_001", "dr2_ch_000_002", "dr2_ch_000_003", "dr2_ch_000_004"];

    // ✅ docs/cliente/cenas.md#slots-0x00b47790: where the texts of each slot start
    private static readonly (int X, int Y)[] TextOrigins = [(231, 110), (449, 110), (231, 340), (449, 340)];

    private const int DeleteX = 26, CreateX = 134, ExchangeX = 246, ButtonsY = 565, GameStartX = 520, BackX = 681, BackY = 549, GameStartY = 549;

    // ✅ text ids & message durations (0x00b48840, 0x00b45c30)
    private const int LevelText = 0xef1, WinLoseText = 0xef2;
    public const int NameTakenText = 0xee4;
    public const int ConsignmentActiveText = 0xeb1a;
    private const int NameTakenFrames = 160;

    /// <summary>✅ <c>0x00b48670</c>: the text shown for 80 frames when the map server cannot be reached; the scene stays.</summary>
    public const int MapConnectFailedText = 0xee6;
    private const int MapConnectFailedFrames = 80;
    private const int MessageCenterX = 400, MessageCenterY = 300;

    // ✅ reply error codes (0x00b45c30)
    public const int DeletePasswordMismatchError = 2000;
    public const int DeleteConsignmentActiveError = 105;

    private const int MapLineHeight = 18, MapMaxWidth = 140, MapMaxLines = 2;

    private ITexture _background = null!;                      // set in Init
    private readonly Button[] _slotButtons = new Button[SlotCount];
    private Button _delete = null!, _create = null!, _exchange = null!, _gameStart = null!, _back = null!;
    private readonly SpriteAnimator?[] _digimon = new SpriteAnimator?[SlotCount];
    private readonly TamerDoll?[] _tamers = new TamerDoll?[SlotCount];
    private AnimatedSprites _animated = null!;                 // set in Init
    private MessageBox _box = null!;                           // set in Init
    private CharacterCreatePanel _createPanel = null!;         // set in Init
    private readonly DeleteCharacterDialog _deleteDialog = new();
    private readonly SecondaryPasswordDialog _secondaryPasswordDialog = new();

    private bool _requested;
    private bool _checkedSecondaryPasswordPrompt;
    private int _loadedListVersion;
    private int _mode;                                         // ✅ +0x85c: 0 = selection, 1 = creation
    private int _selected = -1;
    private string? _message;
    private int _messageFrames;                                // 0 = until click; > 0 = countdown or click
    private FadeOut? _fade;
    private bool _worldStarted;

    public int FrameIntervalMs => 1000 / FramesPerSecond;
    internal int Mode => _mode;
    internal int SelectedSlot => _selected;
    internal string? ActiveMessage => _message;
    internal DeleteCharacterDialog DeleteDialog => _deleteDialog;
    internal SecondaryPasswordDialog SecondaryPasswordDialog => _secondaryPasswordDialog;
    internal CharacterCreatePanel CreatePanel => _createPanel;

    public void Init()
    {
        var sprites = game.Sprites;
        _background = sprites.Gui("dr2_ch_000_000")[0];
        for (int i = 0; i < SlotCount; i++)
        {
            // ✅ 0x00b48000: a loop over the four slots maps their states to frames 0, 1, 1, 1 (the sprites have only two
            // frames, so "pressed" shows the highlighted one)
            _slotButtons[i] = new Button(sprites.Gui(SlotSprites[i]), SlotButtons[i].X, SlotButtons[i].Y, [0, 1, 1, 1]);
        }
        _delete = new Button(sprites.Gui("dr2_ch_000_005"), DeleteX, ButtonsY);
        _create = new Button(sprites.Gui("dr2_ch_000_006"), CreateX, ButtonsY);
        _exchange = new Button(sprites.Gui("dr2_ch_000_009"), ExchangeX, ButtonsY);
        _gameStart = new Button(sprites.Gui("dr2_ch_000_007"), GameStartX, GameStartY);
        _back = new Button(sprites.Gui("dr2_ch_000_008"), BackX, BackY);
        _animated = new AnimatedSprites(game.Resource, sprites, game.Log);
        _box = new MessageBox(sprites.Load("gui\\dr2_in_000_003.spr"), game.Text);
        _createPanel = new CharacterCreatePanel(game, _animated, random);
        _createPanel.Init();
        game.Sounds.Register(SelectSound, "Sound\\menu-1.wav");     // ✅ the music does not change: title.ogg goes on
    }

    public void Handle(InputEvent input)
    {
        if (_secondaryPasswordDialog.IsOpen)
        {
            _secondaryPasswordDialog.Handle(input);
            return;
        }
        if (_deleteDialog.IsOpen)
        {
            _deleteDialog.Handle(input);
            return;
        }
        if (_mode == 1)
            _createPanel.Handle(input);
    }

    public void Frame(IRenderer renderer, ulong nowMs)
    {
        var account = game.Account!;                                // the server scene only leaves with an account
        if (!_requested && account.Step == AccountStep.Authenticated)
        {
            _requested = true;
            account.RequestCharacterList(nowMs);                    // ✅ 0x12 then 0x14, in the init
        }
        account.Update(nowMs);
        if (account.Step == AccountStep.Failed)
        {
            // ✅ no network: back to the world selection
            account.Reset();
            game.Scenes.Switch(SceneId.Server);
            return;
        }

        ProcessAccountEvents(account);

        if (_mode == 1)
            FrameCreateMode(renderer, account, nowMs);
        else
            FrameSelectMode(renderer, account, nowMs);

        DrawMessage(renderer);
        // ✅ 0x00b48840: CSceneCharSel does not draw the version text (only Title and Server scenes do)
        if (_fade is { } fade)
        {
            fade.Frame(renderer, 1f / FramesPerSecond);
            AdvanceWorldEntry(fade);
        }
    }

    private void ProcessAccountEvents(AccountSession account)
    {
        if (account.Characters is { } list && account.CharacterListVersion != _loadedListVersion)
            LoadSlots(list, account.CharacterListVersion);

        if (account.TakeCreateError() is { } createError)
            HandleCreateError(createError);

        if (account.TakeDeleteError() is { } deleteError)
            HandleDeleteError(deleteError);

        // ✅ 0x00b45c30 case 0x14: RequiredUnset opens mandatory registration on entering character select
        if (account.UserConfirmed && !_checkedSecondaryPasswordPrompt)
        {
            _checkedSecondaryPasswordPrompt = true;
            if (account.SecondaryPasswordStatus == SecondaryPasswordStatus.RequiredUnset)
                _secondaryPasswordDialog.OpenRegister(mandatory: true);
        }

        if (account.TakeSecondaryPasswordResult() is { } spResult)
            _secondaryPasswordDialog.ShowResult(spResult);
    }

    /// <summary>✅ <c>0x00b45c30</c> case <c>0x10</c>: only error 102 (<c>NameTaken</c>) is handled.</summary>
    internal void HandleCreateError(int errorCode)
    {
        if (errorCode != ChannelError.NameTaken)
            return;
        ShowMessage(game.Texts[NameTakenText], NameTakenFrames);
        _createPanel.ClearAndFocusTamer();
    }

    /// <summary>✅ <c>0x00b45c30</c> case <c>0x11</c>: error 2000 opens state 1; error 105 shows <c>0xeb1a</c> until click.</summary>
    internal void HandleDeleteError(int errorCode)
    {
        switch (errorCode)
        {
            case DeletePasswordMismatchError:
                _deleteDialog.OpenPasswordMismatch();
                break;
            case DeleteConsignmentActiveError:
                ShowMessage(game.Texts[ConsignmentActiveText], frames: 0);
                break;
        }
    }

    private void FrameSelectMode(IRenderer renderer, AccountSession account, ulong nowMs)
    {
        renderer.Draw(_background, 0, 0);
        // ✅ +0x318 (delete dialog) and +0x538 (secondary password dialog) lock all mode 0 buttons and slots
        bool interactive = _fade is null && !_deleteDialog.IsOpen && !_secondaryPasswordDialog.IsOpen && _message is null;
        DrawSlots(renderer, account, interactive);
        DrawButtons(renderer, account, interactive);

        if (_deleteDialog.IsOpen)
        {
            var action = _deleteDialog.Frame(renderer, _box, game.Text, game.Texts, game.Mouse, nowMs);
            if (action == DeleteDialogAction.ConfirmDelete)
                SendDelete(account);
        }

        if (_secondaryPasswordDialog.IsOpen)
        {
            var action = _secondaryPasswordDialog.Frame(renderer, _box, game.Text, game.Texts, game.Mouse, nowMs);
            if (action is SecondaryPasswordAction.Submit submit && account.Step == AccountStep.Ready)
                account.SendSecondaryPassword(submit.OldPassword, submit.NewPassword, submit.ConfirmPassword);
        }
    }

    private void FrameCreateMode(IRenderer renderer, AccountSession account, ulong nowMs)
    {
        bool hasList = account.Characters is not null;
        bool interactive = _fade is null && _message is null;
        var action = _createPanel.Frame(renderer, nowMs, hasList, interactive);
        switch (action)
        {
            case CreatePanelAction.Cancel:
                ExitCreateMode();
                break;
            case CreatePanelAction.ValidationError error:
                ShowMessage(game.Texts[error.TextId], frames: 0);
                break;
            case CreatePanelAction.Submit submit when account.Step == AccountStep.Ready:
                account.CreateCharacter(submit.Request);
                break;
        }
    }

    private void SendDelete(AccountSession account)
    {
        if (account.Step != AccountStep.Ready || account.Characters is not { } list)
            return;
        if (_selected < 0 || _selected >= list.Slots.Count)
            return;
        var slot = list.Slots[_selected];
        if (slot.IsEmpty || string.IsNullOrEmpty(slot.Name))
            return;
        // ✅ 0x00b46020: sends MD5 of the password box ("" when state 0 was skipped)
        account.DeleteCharacter(slot.Uid, _deleteDialog.TakePasswordMd5());
    }

    /// <summary>✅ Arrival of the list: mode 0, nothing selected, the animations of the species and tamers opened (<c>0x00b45c30</c>, case <c>0x12</c>).</summary>
    private void LoadSlots(CharacterListReply list, int version)
    {
        _loadedListVersion = version;
        _mode = 0;
        _selected = -1;
        _deleteDialog.Close();
        for (int i = 0; i < SlotCount; i++)
        {
            _digimon[i] = null;
            _tamers[i] = null;
            if (i >= list.Slots.Count)
                continue;
            var slot = list.Slots[i];
            if (slot.IsEmpty || slot.TamerId == 0)
                continue;
            _digimon[i] = _animated.OpenDigimon(slot.LeaderSpeciesId);
            _tamers[i] = _animated.OpenTamer(slot.TamerId);
        }
    }

    private void TryEnterCreateMode(AccountSession account)
    {
        if (account.Characters is not { } list)
            return;
        int targetSlot = CharacterNameRules.PickTargetSlot(list.Slots, _selected);
        if (targetSlot < 0)
        {
            ShowMessage(game.Texts[CharacterNameRules.NoEmptySlotText], frames: 0);
            return;
        }
        _createPanel.Enter(targetSlot, account.FourthStarter);
        _mode = 1;
    }

    private void ExitCreateMode()
    {
        _mode = 0;
        _selected = -1;
    }

    private void ShowMessage(string text, int frames)
    {
        _message = text;
        _messageFrames = frames;
    }

    private void DrawMessage(IRenderer renderer)
    {
        if (_message is null)
            return;
        _box.Draw(renderer, _message, MessageCenterX, MessageCenterY);
        if (game.Mouse.Pressed)
        {
            _message = null;
            _messageFrames = 0;
            return;
        }
        if (_messageFrames > 0 && --_messageFrames == 0)
            _message = null;
    }

    private void DrawSlots(IRenderer renderer, AccountSession account, bool interactive)
    {
        var slots = account.Characters?.Slots;
        if (slots is null)
            return;
        for (int i = 0; i < SlotCount && i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.IsEmpty)
                continue;
            var button = _slotButtons[i];
            button.Update(game.Mouse, interactive);
            if (_selected == i)
                button.DrawFrame(renderer, 1);                     // ✅ the selected slot draws frame 1 fixed
            else
                button.Draw(renderer);

            if (button.Clicked)
                Select(i, slot);
            else if (button.DoubleClicked && _selected == i)
                EnterGame();                                       // ✅ double click on the selected slot

            if (slot.TamerId == 0)
                continue;
            DrawDigimon(renderer, i, slot);
            _digimon[i]?.Tick();                                   // ✅ 0x00b46120 draws, then advances
            _tamers[i]?.Body.Tick();                               // ✅ 0x00b46d50 advances, then draws
            DrawTamer(renderer, i);
            DrawTexts(renderer, i, slot);
        }
    }

    /// <summary>✅ A click on a slot with a tamer selects it (sound 5, "selected" animation from its start); on one without, nothing stays selected.</summary>
    private void Select(int index, CharacterListSlot slot)
    {
        if (slot.TamerId == 0)
        {
            _selected = -1;
            return;
        }
        if (_selected == index)
            return;
        if (_selected >= 0)
        {
            _digimon[_selected]?.Switch(SpriteAnimator.Standing);  // ✅ back to "standing", the frame goes on until the loop
            _tamers[_selected]?.Body.Switch(SpriteAnimator.Standing);
        }
        _selected = index;
        game.Sounds.PlayEffect(SelectSound);
        _digimon[index]?.Restart(SpriteAnimator.Selected);
        _tamers[index]?.Body.Restart(SpriteAnimator.Selected);
    }

    /// <summary>
    /// ✅ <c>0x00b4782c</c>: slots 1 and 3 draw their digimon and tamer 150 px right of the button, which is where
    /// <c>0x00b47790</c> adds it before calling both.
    /// </summary>
    private static int DrawX(int index) => SlotButtons[index].X + (index % 2 == 1 ? 150 : 0);

    /// <summary>✅ <c>0x00b46120</c>: the frame by its corner at (x + offset of the species), or centred for a few species.</summary>
    private void DrawDigimon(IRenderer renderer, int index, CharacterListSlot slot)
    {
        if (_digimon[index]?.Texture is not { } frame)
            return;
        int x = DrawX(index), y = SlotButtons[index].Y;
        if (SpeciesTables.CenterOf(slot.LeaderSpeciesId) is { } center)
        {
            renderer.Draw(frame, x + center.X - frame.Width / 2, y + center.Y - frame.Height / 2);
            return;
        }
        var (dx, dy) = SpeciesTables.OffsetOf(slot.LeaderSpeciesId);
        renderer.Draw(frame, x + dx, y + dy);
    }

    /// <summary>
    /// ✅ <c>0x00b46d50</c> → <c>0x0099fb90</c>: the tamer and its clothes by the frame's top-left corner at
    /// (x + 70 − 120, y + 164 − 162). The <c>.ani</c> anchor is not used here.
    /// </summary>
    private void DrawTamer(IRenderer renderer, int index) =>
        _tamers[index]?.Draw(renderer, DrawX(index) + 70 - 120, SlotButtons[index].Y + 164 - 162);

    /// <summary>✅ <c>0x00b47350</c>: the texts of a slot, white with a shadow.</summary>
    private void DrawTexts(IRenderer renderer, int index, CharacterListSlot slot)
    {
        var (x, y) = TextOrigins[index];
        var texts = game.Texts;
        // ✅ 0x00b45c30: in the BATTLE ZONE the levels shown are fixed at 10
        int tamerLevel = game.IsBattleZone ? 10 : slot.Level;
        int digimonLevel = game.IsBattleZone ? 10 : slot.LeaderLevel;
        var level = texts[LevelText];
        var record = PrintfFormat.Format(texts[WinLoseText], slot.TotalWins, slot.TotalBattles,
            slot.TotalBattles == 0 ? 0.0 : (double)slot.TotalWins / slot.TotalBattles * 100);
        // ✅ the original writes the total of battles where the text says "Lose"
        // ✅ 0x00b47350: slots 0/1 put tamer at y and digimon at y + 108; slots 2/3 put digimon at y + 10 and tamer at y + 100
        int tamerY, digimonY;
        if (index < 2)
            (tamerY, digimonY) = (y, y + 108);
        else
            (tamerY, digimonY) = (y + 100, y + 10);

        var text = game.Text;
        text.Draw(renderer, slot.Name, x, tamerY);
        text.Draw(renderer, PrintfFormat.Format(level, tamerLevel), x, tamerY + 18);
        var mapLines = text.Wrap(renderer, game.Data?.MapName(slot.MapId) ?? "", MapMaxWidth);
        for (int line = 0; line < mapLines.Count && line < MapMaxLines; line++)
            text.Draw(renderer, mapLines[line], x, tamerY + 36 + line * MapLineHeight);
        text.Draw(renderer, PrintfFormat.Format("(%d,%d)", slot.X, slot.Y), x, tamerY + 54);

        text.Draw(renderer, slot.LeaderName, x, digimonY);
        text.Draw(renderer, PrintfFormat.Format(level, digimonLevel), x, digimonY + 18);
        text.Draw(renderer, record, x, digimonY + 36);
    }

    private void DrawButtons(IRenderer renderer, AccountSession account, bool interactive)
    {
        bool hasList = account.Characters is not null;
        bool exchangeVisible = account.SecondaryPasswordStatus != SecondaryPasswordStatus.Disabled;
        _delete.Update(game.Mouse, interactive);
        _delete.Draw(renderer);
        _create.Update(game.Mouse, interactive);
        _create.Draw(renderer);
        if (exchangeVisible)
        {
            _exchange.Update(game.Mouse, interactive);
            _exchange.Draw(renderer);
        }
        _gameStart.Update(game.Mouse, interactive);
        _gameStart.Draw(renderer);
        _back.Update(game.Mouse, interactive);
        _back.Draw(renderer);

        if (!interactive)
            return;
        if (_delete.Clicked && _selected >= 0 && hasList)
            _deleteDialog.Open(requirePassword: account.SecondaryPasswordStatus == SecondaryPasswordStatus.Set);
        if (_create.Clicked && !game.IsBattleZone && hasList)
            TryEnterCreateMode(account);
        if (exchangeVisible && _exchange.Clicked && hasList)
        {
            if (account.SecondaryPasswordStatus == SecondaryPasswordStatus.Set)
                _secondaryPasswordDialog.OpenChange();
            else
                _secondaryPasswordDialog.OpenRegister(mandatory: false);
        }
        if (_gameStart.Clicked && _selected >= 0 && hasList)
            EnterGame();
        if (_back.Clicked)
        {
            account.Reset();                                                                      // ✅ disconnects and returns to scene 2
            game.Scenes.Switch(SceneId.Server);
        }
    }

    /// <summary>
    /// ✅ <c>0x00b48670</c>: fades out like the world selection, logs the map server and keeps the chosen character for scene 5.
    /// ❓ The original picks the map server from a world × map table of the binary; the new client has one address (<c>servers.map</c>).
    /// </summary>
    private void EnterGame()
    {
        if (_fade is not null || _selected < 0)
            return;
        var list = game.Account?.Characters;
        if (list is null)
            return;
        game.World?.Dispose();
        game.World = new WorldEntry(list.Slots[_selected], _selected);
        _fade = new FadeOut();
        _worldStarted = false;
        game.Log($"Map Server : {game.Settings.Map.Host} {game.Settings.Map.Port}");
    }

    /// <summary>
    /// ✅ At the end of the fade (<c>0x00b47790</c>): the map connection. Connected → scene 5 (its loading screen sends the <c>0x01</c> and
    /// the channel's <c>0x16</c>/<c>0x18</c> later); not connected → text <c>0xee6</c> for 80 frames and the selection goes on.
    /// </summary>
    private void AdvanceWorldEntry(FadeOut fade)
    {
        var world = game.World;
        if (world is null)
            return;
        if (!_worldStarted)
        {
            if (!fade.Done)
                return;
            _worldStarted = true;
            world.Session = new WorldSession(game.Settings, game.Command, game.Log);
            world.Session.Start(world.Slot.Name, firstEntry: !game.HasEnteredWorld);
            return;
        }
        var session = world.Session!;
        session.Update();
        if (session.MapConnected)
        {
            game.Scenes.Switch(SceneId.Game);
        }
        else if (session.Step == WorldStep.Failed)
        {
            game.World?.Dispose();
            game.World = null;
            _fade = null;
            _worldStarted = false;
            ShowMessage(game.Texts[MapConnectFailedText], MapConnectFailedFrames);
        }
    }

    public void Dispose()
    {
        _createPanel?.Dispose();
        _deleteDialog.Dispose();
        _secondaryPasswordDialog.Dispose();
    }
}
