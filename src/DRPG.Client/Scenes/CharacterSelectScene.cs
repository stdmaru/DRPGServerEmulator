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
/// the four slots with the tamer and the digimon animated, their texts, and the Delete / Create / Exchange Password /
/// Game Start / Back buttons. Asks for the list (<c>0x12</c> + <c>0x14</c>) when it opens; Back returns to the world
/// selection; Game Start fades out and logs the map server (entering the map is milestone 3).
/// ❓ Not done here: the character creation mode, the delete confirmation and the password change (next milestone: the
/// buttons are drawn and their click is only logged), and the sprite shadow option.
/// </summary>
public sealed class CharacterSelectScene(GameContext game) : IScene
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

    // ✅ text ids
    private const int LevelText = 0xef1, WinLoseText = 0xef2;

    private const int MapLineHeight = 18, MapMaxWidth = 140, MapMaxLines = 2;

    private ITexture _background = null!;                      // set in Init
    private readonly Button[] _slotButtons = new Button[SlotCount];
    private Button _delete = null!, _create = null!, _exchange = null!, _gameStart = null!, _back = null!;
    private readonly SpriteAnimator?[] _digimon = new SpriteAnimator?[SlotCount];
    private readonly TamerDoll?[] _tamers = new TamerDoll?[SlotCount];
    private AnimatedSprites _animated = null!;                 // set in Init
    private bool _requested, _loadedSlots;
    private int _selected = -1;
    private FadeOut? _fade;
    private ulong _lastFrameMs;

    public int FrameIntervalMs => 1000 / FramesPerSecond;

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
        game.Sounds.Register(SelectSound, "Sound\\menu-1.wav");     // ✅ the music does not change: title.ogg goes on
    }

    public void Handle(InputEvent input)
    {
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
        if (account.Characters is { } list && !_loadedSlots)
            LoadSlots(list);

        renderer.Draw(_background, 0, 0);
        bool interactive = _fade is null;
        DrawSlots(renderer, account, interactive);
        DrawButtons(renderer, account, interactive);
        game.DrawVersion(renderer, 1);
        if (_fade is { } fade)
        {
            fade.Frame(renderer, 1f / FramesPerSecond);
        }
        _lastFrameMs = nowMs;
    }

    /// <summary>✅ Arrival of the list: nothing selected, the animations of the species and tamers opened (<c>0x00b45c30</c>, case <c>0x12</c>).</summary>
    private void LoadSlots(CharacterListReply list)
    {
        _loadedSlots = true;
        _selected = -1;
        for (int i = 0; i < SlotCount && i < list.Slots.Count; i++)
        {
            var slot = list.Slots[i];
            if (slot.IsEmpty || slot.TamerId == 0)
                continue;
            _digimon[i] = _animated.OpenDigimon(slot.LeaderSpeciesId);
            _tamers[i] = _animated.OpenTamer(slot.TamerId);
        }
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
        int tamerY, digimonY;
        if (index < 2)
            (tamerY, digimonY) = (y, y + 108);
        else
            (tamerY, digimonY) = (y + 90, y + 10);

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
        _delete.Update(game.Mouse, interactive);
        _delete.Draw(renderer);
        _create.Update(game.Mouse, interactive);
        _create.Draw(renderer);
        _exchange.Update(game.Mouse, interactive);
        _exchange.Draw(renderer);
        _gameStart.Update(game.Mouse, interactive);
        _gameStart.Draw(renderer);
        _back.Update(game.Mouse, interactive);
        _back.Draw(renderer);

        if (_delete.Clicked && _selected >= 0)
            game.Log("Delete Character: the confirmation comes with the next milestone");        // ❓
        if (_create.Clicked && !game.IsBattleZone)
            game.Log("Create Character: the creation screen comes with the next milestone");     // ❓
        if (_exchange.Clicked)
            game.Log("Exchange Password: it comes with the next milestone");                      // ❓
        if (_gameStart.Clicked && _selected >= 0 && hasList)
            EnterGame();
        if (_back.Clicked)
        {
            account.Reset();                                                                      // ✅ disconnects and returns to scene 2
            game.Scenes.Switch(SceneId.Server);
        }
    }

    /// <summary>
    /// ✅ <c>0x00b48670</c>: fades out like the world selection and logs the map server. ❓ Where the map server is comes
    /// with the map protocol (milestone 3); entering scene 5 is not part of this milestone.
    /// </summary>
    private void EnterGame()
    {
        if (_fade is not null)
            return;
        _fade = new FadeOut();
        game.Log("Map Server : (not known yet, milestone 3) 0");
    }

    public void Dispose()
    {
    }
}
