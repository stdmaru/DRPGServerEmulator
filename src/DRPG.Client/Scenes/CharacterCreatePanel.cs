using DRPG.Client.Data;
using DRPG.Client.Ui;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;
using DRPG.Shared.Packets.Channel;

namespace DRPG.Client.Scenes;

/// <summary>Outcome of one frame of <see cref="CharacterCreatePanel"/>.</summary>
public abstract record CreatePanelAction
{
    public sealed record None : CreatePanelAction;
    public sealed record Cancel : CreatePanelAction;
    public sealed record ValidationError(int TextId) : CreatePanelAction;
    public sealed record Submit(CharacterCreateRequest Request) : CreatePanelAction;
}

/// <summary>
/// Character creation mode (mode 1) of <c>CSceneCharSel</c> (✅ v322 <c>0x00b45510(1)</c>, <c>0x00b48c60</c>,
/// <c>0x00b46f00</c>, <c>0x00b46690</c>, <c>0x00b45990</c>, docs/cliente/cenas.md#criação-de-personagem-modo-1--re).
/// </summary>
public sealed class CharacterCreatePanel(GameContext game, AnimatedSprites animated, Random random) : IDisposable
{
    private const int SelectSound = 5;
    private const int TamerCount = 4;
    private const int MaxDigimonCount = 4;

    // ✅ Buttons: 0x00b47b30, 0x00b45510
    private const int OkX = 565, CancelX = 681, ActionButtonY = 549;
    private static readonly (int X, int Y)[] TamerButtonPositions = [(73, 188), (254, 188), (439, 188), (619, 188)];
    private static readonly (int X, int Y)[] DigimonButtonPositions = [(126, 394), (273, 394), (419, 394), (565, 394)];

    // ✅ Edit boxes: 0x00b47b30 (130×15, max 20 characters)
    private const int TamerBoxX = 153, DigimonBoxX = 522, EditBoxY = 57, EditBoxW = 130, EditBoxH = 15, EditBoxMaxLength = 20;

    // ✅ Text layout: 0x00b46f00 (tamer) and 0x00b46690 (digimon)
    private const int NameWidth = 130, NameY = 60, TamerNameX = 18, DigimonNameX = 658;
    private const int DescStartY = 84, DescLineStep = 14, DescBottom = 144;
    private const int TamerDescX = 24, TamerDescWidth = 279 - 24;
    private const int DigimonDescX = 522, DigimonDescWidth = 780 - 522;

    // ✅ TextAttach ids (0x00b44738)
    private const int TamerNamesText = 0xee8, TamerDescsText = 0xee9;
    private const int DigimonNamesText = 0xeeb, DigimonDescsText = 0xeec;

    private static readonly ushort[] BaseStarters = [10, 11, 12]; // Gigimon, Gummymon, Viximon

    private ITexture _background = null!;
    private Button _ok = null!, _cancel = null!;
    private readonly Button[] _tamerButtons = new Button[TamerCount];
    private readonly Button[] _digimonButtons = new Button[MaxDigimonCount];
    private readonly TamerDoll?[] _tamerDolls = new TamerDoll?[TamerCount];
    private readonly SpriteAnimator?[] _digimonSprites = new SpriteAnimator?[MaxDigimonCount];
    private readonly ushort[] _digimonSpecies = new ushort[MaxDigimonCount];

    private string[] _tamerNames = [];
    private string[] _tamerDescs = [];
    private string[] _digimonNames = [];
    private string[] _digimonDescs = [];

    private readonly EditBox _tamerNameBox = new(TamerBoxX, EditBoxY, EditBoxW, EditBoxH, EditBoxMaxLength);
    private readonly EditBox _digimonNameBox = new(DigimonBoxX, EditBoxY, EditBoxW, EditBoxH, EditBoxMaxLength);

    private int _targetSlot = -1;
    private byte _selectedTamer = 1;
    private ushort _selectedDigimon = 10;
    private int _digimonCount = BaseStarters.Length;

    public int TargetSlot => _targetSlot;
    public byte SelectedTamer => _selectedTamer;
    public ushort SelectedDigimon => _selectedDigimon;
    public string TamerName => _tamerNameBox.Text;
    public string DigimonName => _digimonNameBox.Text;
    public bool TamerNameFocused => _tamerNameBox.Focused;
    public bool DigimonNameFocused => _digimonNameBox.Focused;

    public void Init()
    {
        var sprites = game.Sprites;
        _background = sprites.Gui("dr2_ch_001_000")[0];
        _ok = new Button(sprites.Gui("dr2_ch_001_001"), OkX, ActionButtonY);
        _cancel = new Button(sprites.Gui("dr2_ch_001_002"), CancelX, ActionButtonY);
        var tamerButtonFrames = sprites.Gui("dr2_ch_001_003");
        for (int i = 0; i < TamerCount; i++)
        {
            _tamerButtons[i] = new Button(tamerButtonFrames, TamerButtonPositions[i].X, TamerButtonPositions[i].Y);
            _tamerDolls[i] = animated.OpenTamer(i + 1);
        }
        var digimonButtonFrames = sprites.Gui("dr2_ch_001_004");
        for (int i = 0; i < MaxDigimonCount; i++)
            _digimonButtons[i] = new Button(digimonButtonFrames, DigimonButtonPositions[i].X, DigimonButtonPositions[i].Y);

        var texts = game.Texts;
        _tamerNames = SplitList(texts[TamerNamesText], "\n");
        _tamerDescs = SplitList(texts[TamerDescsText], "-\n");
        _digimonNames = SplitList(texts[DigimonNamesText], "\n");
        _digimonDescs = SplitList(texts[DigimonDescsText], "-\n");
    }

    /// <summary>✅ <c>0x00b45510(1)</c>: resets names, picks tamer 1 and digimon 10 (animation 5), opens starter sprites, focuses tamer name.</summary>
    public void Enter(int targetSlot, int fourthStarter)
    {
        _targetSlot = targetSlot;
        _tamerNameBox.Clear();
        _digimonNameBox.Clear();
        FocusField(NameField.Tamer);

        _selectedTamer = 1;
        _selectedDigimon = BaseStarters[0];

        for (int i = 0; i < BaseStarters.Length; i++)
            _digimonSpecies[i] = BaseStarters[i];

        if (CharacterNameRules.FourthStarterSpecies(fourthStarter) is { } extra)
        {
            _digimonSpecies[3] = extra;
            _digimonCount = 4;
        }
        else
        {
            _digimonSpecies[3] = 0;
            _digimonCount = 3;
        }

        for (int i = 0; i < TamerCount; i++)
        {
            if (i + 1 == _selectedTamer)
                _tamerDolls[i]?.Body.Restart(SpriteAnimator.Selected);
            else
                _tamerDolls[i]?.Body.Restart(SpriteAnimator.Standing);
        }

        for (int i = 0; i < _digimonCount; i++)
        {
            _digimonSprites[i] = animated.OpenDigimon(_digimonSpecies[i]);
            if (_digimonSpecies[i] == _selectedDigimon)
                _digimonSprites[i]?.Restart(SpriteAnimator.Selected);
            else
                _digimonSprites[i]?.Restart(SpriteAnimator.Standing);
        }
        for (int i = _digimonCount; i < MaxDigimonCount; i++)
            _digimonSprites[i] = null;
    }

    /// <summary>✅ <c>0x00b45c30</c> error 102 (<c>NameTaken</c>): clears the tamer name box and focuses it.</summary>
    public void ClearAndFocusTamer()
    {
        _tamerNameBox.Clear();
        FocusField(NameField.Tamer);
    }

    public void FocusField(NameField field)
    {
        _tamerNameBox.Focused = field == NameField.Tamer;
        _digimonNameBox.Focused = field == NameField.Digimon;
    }

    public void Handle(InputEvent input)
    {
        switch (input)
        {
            case MouseButtonEvent { Down: true } click:
                if (_tamerNameBox.Contains(click.X, click.Y))
                    FocusField(NameField.Tamer);
                else if (_digimonNameBox.Contains(click.X, click.Y))
                    FocusField(NameField.Digimon);
                break;
            case TextEvent text:
                if (_tamerNameBox.Focused)
                    _tamerNameBox.Type(text.Text);
                else if (_digimonNameBox.Focused)
                    _digimonNameBox.Type(text.Text);
                break;
            case KeyEvent { Key: GameKey.Backspace }:
                if (_tamerNameBox.Focused)
                    _tamerNameBox.Backspace();
                else if (_digimonNameBox.Focused)
                    _digimonNameBox.Backspace();
                break;
            case KeyEvent { Key: GameKey.Tab }:
                FocusField(_tamerNameBox.Focused ? NameField.Digimon : NameField.Tamer);
                break;
        }
    }

    /// <summary>
    /// Draws mode 1 in the original's order (✅ <c>0x00b48840</c>): background + OK/Cancel (<c>0x00b48c60</c>),
    /// tamer column (<c>0x00b46f00</c>), digimon column (<c>0x00b46690</c>), edit boxes (<c>0x00a19680</c>).
    /// </summary>
    public CreatePanelAction Frame(IRenderer renderer, ulong nowMs, bool hasList, bool interactive)
    {
        renderer.Draw(_background, 0, 0);

        bool canOk = interactive && hasList && _selectedTamer != 0 && _selectedDigimon != 0;
        _ok.Update(game.Mouse, canOk);
        _ok.Draw(renderer);

        _cancel.Update(game.Mouse, interactive);
        _cancel.Draw(renderer);

        CreatePanelAction action = new CreatePanelAction.None();
        if (_ok.Clicked && canOk)
        {
            if (CharacterNameRules.Validate(_tamerNameBox.Text, _digimonNameBox.Text, game.ProhibitedWords) is { } error)
            {
                FocusField(error.Focus);
                action = new CreatePanelAction.ValidationError(error.TextId);
            }
            else if (_targetSlot >= 0 && !string.IsNullOrEmpty(_tamerNameBox.Text))
            {
                var header = CharacterNameRules.BuildCreateHeader((byte)_targetSlot, random);
                var request = new CharacterCreateRequest(header, _selectedTamer, _tamerNameBox.Text, _selectedDigimon, _digimonNameBox.Text);
                action = new CreatePanelAction.Submit(request);
            }
        }
        else if (_cancel.Clicked && interactive)
        {
            _tamerNameBox.Clear();
            _digimonNameBox.Clear();
            action = new CreatePanelAction.Cancel();
        }

        DrawTamers(renderer, interactive && hasList);
        DrawDigimons(renderer, interactive && hasList);

        _tamerNameBox.Draw(renderer, game.Text, nowMs, interactive);
        _digimonNameBox.Draw(renderer, game.Text, nowMs, interactive);

        return action;
    }

    /// <summary>✅ <c>0x00b46f00</c>: the four tamer buttons, dolls, and selected tamer name + description.</summary>
    private void DrawTamers(IRenderer renderer, bool interactive)
    {
        for (int i = 0; i < TamerCount; i++)
        {
            byte tamerId = (byte)(i + 1);
            var button = _tamerButtons[i];
            button.Update(game.Mouse, interactive);
            if (_selectedTamer == tamerId)
                button.DrawFrame(renderer, 0);
            else
                button.Draw(renderer);

            if (button.Clicked && interactive && _selectedTamer != tamerId)
            {
                _tamerDolls[_selectedTamer - 1]?.Body.Switch(SpriteAnimator.Standing);
                _selectedTamer = tamerId;
                _tamerDolls[i]?.Body.Restart(SpriteAnimator.Selected);
                game.Sounds.PlayEffect(SelectSound);
            }

            _tamerDolls[i]?.Body.Tick();
            // ✅ 0x0099fb90(tamer, xButton + 58, yButton + 126) → top-left (xButton - 62, yButton - 36)
            _tamerDolls[i]?.Draw(renderer, button.X - 62, button.Y - 36);
        }

        DrawCenteredName(renderer, Lookup(_tamerNames, _selectedTamer), TamerNameX);
        DrawDescription(renderer, Lookup(_tamerDescs, _selectedTamer), TamerDescX, TamerDescWidth);
    }

    /// <summary>✅ <c>0x00b46690</c>: the 3 or 4 starter digimon buttons, sprites, and selected name + description.</summary>
    private void DrawDigimons(IRenderer renderer, bool interactive)
    {
        for (int i = 0; i < _digimonCount; i++)
        {
            ushort species = _digimonSpecies[i];
            var button = _digimonButtons[i];
            button.Update(game.Mouse, interactive);
            if (_selectedDigimon == species)
                button.DrawFrame(renderer, 0);
            else
                button.Draw(renderer);

            if (button.Clicked && interactive && _selectedDigimon != species)
            {
                for (int j = 0; j < _digimonCount; j++)
                {
                    if (_digimonSpecies[j] == _selectedDigimon)
                        _digimonSprites[j]?.Switch(SpriteAnimator.Standing);
                }
                _selectedDigimon = species;
                _digimonSprites[i]?.Restart(SpriteAnimator.Selected);
                game.Sounds.PlayEffect(SelectSound);
            }

            if (_digimonSprites[i]?.Texture is { } frame)
            {
                var (dx, dy) = CreationDigimonOffset(species);
                renderer.Draw(frame, button.X + dx, button.Y + dy);
            }
            _digimonSprites[i]?.Tick();
        }

        int index = DigimonTextIndex(_selectedDigimon);
        DrawCenteredName(renderer, Lookup(_digimonNames, index), DigimonNameX);
        DrawDescription(renderer, Lookup(_digimonDescs, index), DigimonDescX, DigimonDescWidth);
    }

    private void DrawCenteredName(IRenderer renderer, string name, int areaX)
    {
        if (name.Length == 0)
            return;
        var (width, _) = game.Text.Measure(renderer, name);
        game.Text.Draw(renderer, name, areaX + (NameWidth - width) / 2, NameY, UiText.NameHighlight, shadow: false);
    }

    private void DrawDescription(IRenderer renderer, string description, int x, int maxWidth)
    {
        if (description.Length == 0)
            return;
        var lines = description.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int y = DescStartY + i * DescLineStep;
            if (y >= DescBottom)
                break;
            game.Text.DrawClipped(renderer, lines[i], x, y, maxWidth, UiText.White, shadow: false);
        }
    }

    /// <summary>✅ <c>0x00b46690</c>: sprite corner offset from the digimon button in creation mode.</summary>
    internal static (int Dx, int Dy) CreationDigimonOffset(ushort species) => species switch
    {
        27 => (-156, -148),
        31 => (-156, -165),
        40 => (-156, -160),
        42 => (-106, -82),
        _ => (-110, -70),
    };

    /// <summary>✅ <c>0x00b46690</c>: maps starter species to the index in <c>0xeeb</c>/<c>0xeec</c>.</summary>
    internal static int DigimonTextIndex(ushort species) => species switch
    {
        10 => 1,
        11 => 2,
        12 => 3,
        24 => 4,
        27 => 5,
        31 => 6,
        40 => 7,
        42 => 8,
        _ => 0,
    };

    private static string[] SplitList(string raw, string separator) =>
        raw.Replace("†", "\n").Split(separator);

    private static string Lookup(string[] list, int index) =>
        index >= 0 && index < list.Length ? list[index] : "";

    public void Dispose()
    {
        _tamerNameBox.Dispose();
        _digimonNameBox.Dispose();
    }
}
