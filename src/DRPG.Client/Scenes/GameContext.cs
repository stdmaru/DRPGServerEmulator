using DRPG.Client.Net;
using DRPG.Client.Startup;
using DRPG.Client.Text;
using DRPG.Client.Ui;
using DRPG.Engine.Assets;
using DRPG.Engine.Audio;
using DRPG.Engine.Rendering;
using DRPG.Engine.Scenes;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Packets.Channel;

namespace DRPG.Client.Scenes;

/// <summary>Everything the scenes share: the install, the sprites, the text, the sound and what the loading screen loaded.</summary>
public sealed class GameContext(
    GameInstall install,
    PackArchive resource,
    SpriteStore sprites,
    UiText text,
    SoundBank sounds,
    GameCommandLine command,
    ClientSettings settings,
    SpriteOptions spriteOptions,
    Action<string> log)
{
    /// <summary>✅ Version text every original scene draws in the top left corner (<c>0x0102e220</c>).</summary>
    public const string VersionText = "Digimon RPG 3.26";

    /// <summary>✅ The lines 7 to 12 of the original server table are the BATTLE ZONE channels (<c>0x00b45c30</c>, case <c>0x12</c>).</summary>
    public const int FirstBattleZoneLine = 7, LastBattleZoneLine = 12;

    private static readonly TextAttach NoTexts = new();
    private static readonly ProhibitedWords NoProhibitedWords = new();

    public GameInstall Install { get; } = install;
    public PackArchive Resource { get; } = resource;
    public SpriteStore Sprites { get; } = sprites;
    public UiText Text { get; } = text;
    public SoundBank Sounds { get; } = sounds;
    public GameCommandLine Command { get; } = command;
    public ClientSettings Settings { get; } = settings;

    /// <summary>✅ <c>option.ini [SPRITE]</c>: the blends, shadows and soft edges of the sprites (the options window <c>0x00b16980</c> changes them in game).</summary>
    public SpriteOptions SpriteOptions { get; set; } = spriteOptions;
    public Action<string> Log { get; } = log;

    /// <summary>The mouse; the loop feeds it and clears it after each frame.</summary>
    public MouseState Mouse { get; } = new();

    /// <summary>Set by the loading screen when it is done.</summary>
    public GameData? Data { get; set; }

    /// <summary>The interface texts by id; <c>Null Text</c> for all of them until the loading screen is done.</summary>
    public TextAttach Texts => Data?.Texts ?? NoTexts;

    /// <summary>Prohibited substrings for character and digimon names; empty until the loading screen is done.</summary>
    public ProhibitedWords ProhibitedWords => Data?.ProhibitedWords ?? NoProhibitedWords;

    /// <summary>The login → channel connection, owned by the server scene and used by the character selection.</summary>
    public AccountSession? Account { get; set; }

    /// <summary>✅ The row of the original's server table of the channel the player chose (<c>[DAT_011584f4]+4</c>); -1 before a choice.</summary>
    public int ChannelLine { get; set; } = -1;

    public bool IsBattleZone => ChannelLine is >= FirstBattleZoneLine and <= LastBattleZoneLine;

    /// <summary>
    /// The name the tamer panel shows for the server (✅ <c>[DAT_011584f4]+0xafc60</c>). ❓ Provisional: the text of the channel link
    /// the player clicked (lines 0..3); the other lines have no name yet.
    /// </summary>
    public string ChannelName => (uint)ChannelLine < (uint)ChannelTextIds.Length ? Texts[ChannelTextIds[ChannelLine]] : "";

    private static readonly int[] ChannelTextIds = [0x5e3, 0x5e0, 0x5e1, 0x5e2];

    /// <summary>The character chosen in the character selection and the connection to the world; null until "Game Start".</summary>
    public WorldEntry? World { get; set; }

    /// <summary>✅ <c>[+0x3f15]</c>: set by the first entry into the world of the run, which also sends the <c>0x121</c>.</summary>
    public bool HasEnteredWorld { get; set; }

    /// <summary>A failure no scene can recover from: the loop shows it in a message box and quits.</summary>
    public string? FatalError { get; set; }

    /// <summary>The player closed the game (Close button, window); the loop ends without an error.</summary>
    public bool QuitRequested { get; set; }

    public SceneManager Scenes { get; set; } = null!; // set once by Program before the first frame

    /// <summary>
    /// ✅ <c>[DAT_0135b150]+0x690</c>: the next switch to scene 2 skips the 3 s <see cref="ReturnLoadingScene"/>. True at
    /// start (<c>Game Init</c> sets it), cleared by the first entry; every later return to scene 2 shows the screen first.
    /// </summary>
    public bool SkipReturnLoading { get; set; } = true;

    /// <summary>✅ <c>FUN_00b55270(0, "Digimon RPG 3.26", x, 1, 0xffffff, 1, 0)</c>: white text with shadow at (x, 1).</summary>
    public void DrawVersion(IRenderer renderer, int x) => Text.Draw(renderer, VersionText, x, 1);
}

/// <summary>✅ The state of the world entry (<c>[DAT_011584f0]</c>): the character of "Game Start" and the connection to the map.</summary>
public sealed class WorldEntry(CharacterListSlot slot, int slotIndex) : IDisposable
{
    public CharacterListSlot Slot { get; } = slot;
    public int SlotIndex { get; } = slotIndex;

    /// <summary>✅ The map of the slot (<c>[DAT_011584f8]+0x511c98</c>).</summary>
    public int MapId { get; set; } = slot.MapId;

    public WorldSession? Session { get; set; }

    public void Dispose() => Session?.Dispose();
}
