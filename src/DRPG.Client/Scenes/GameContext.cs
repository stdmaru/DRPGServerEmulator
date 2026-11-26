using DRPG.Client.Net;
using DRPG.Client.Startup;
using DRPG.Engine.Assets;
using DRPG.Engine.Audio;
using DRPG.Engine.Rendering;
using DRPG.Engine.Scenes;
using DRPG.Shared.Formats.Pack;

namespace DRPG.Client.Scenes;

/// <summary>Everything the scenes share: the install, the sprites, the font and what the loading screen loaded.</summary>
public sealed class GameContext(
    GameInstall install,
    PackArchive resource,
    SpriteStore sprites,
    IFont? font,
    IAudio audio,
    GameCommandLine command,
    ClientSettings settings)
{
    /// <summary>✅ Version text every original scene draws in the top left corner (<c>0x0102e220</c>).</summary>
    public const string VersionText = "Digimon RPG 3.26";

    public GameInstall Install { get; } = install;
    public PackArchive Resource { get; } = resource;
    public SpriteStore Sprites { get; } = sprites;
    public IFont? Font { get; } = font;
    public IAudio Audio { get; } = audio;
    public GameCommandLine Command { get; } = command;
    public ClientSettings Settings { get; } = settings;

    /// <summary>Set by the loading screen when it is done.</summary>
    public GameData? Data { get; set; }

    /// <summary>The login → channel connection; created by the server scene, used by the character selection.</summary>
    public AccountSession? Account { get; set; }

    /// <summary>A failure no scene can recover from: the loop shows it in a message box and quits.</summary>
    public string? FatalError { get; set; }

    public SceneManager Scenes { get; set; } = null!; // set once by Program before the first frame

    private ITexture? _version;

    /// <summary>✅ <c>FUN_00b55270(0, "Digimon RPG 3.26", x, 1, 0xffffff, 1, 0)</c>: white text at (x, 1).</summary>
    public void DrawVersion(IRenderer renderer, int x)
    {
        if (Font is null)
            return;
        _version ??= Font.Render(renderer, VersionText, 0xFFFFFFFF);
        renderer.Draw(_version, x, 1);
    }
}
