using DRPG.Client.Scenes;
using DRPG.Engine.Assets;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;
using DRPG.Engine.Rendering.Backends;
using DRPG.Engine.Scenes;
using DRPG.Shared.Formats.Pack;

namespace DRPG.Client;

internal static class Program
{
    /// <summary>✅ Window title of the original (<c>CreateWindowExW</c> in <c>0x00aa4600</c>: the class and the title are "Digimon RPG").</summary>
    private const string WindowTitle = "Digimon RPG";

    /// <summary>Development only: where the game is when it is not next to the executable. The command line stays at 2 tokens.</summary>
    private const string GameDirectoryVariable = "DRPG_GAME_DIR";

    private static int Main(string[] args)
    {
        // ✅ 0x00aa24b0: anything but exactly two tokens is an error box and exit
        if (GameCommandLine.Parse(args) is not { } command)
        {
            SdlPlatform.ShowError(GameCommandLine.ErrorTitle, GameCommandLine.ErrorText);
            return 1;
        }

        try
        {
            return Run(command);
        }
        catch (Exception e) when (e is IOException or PackFormatException or ClientSettingsException or InvalidOperationException)
        {
            Console.Error.WriteLine(e.Message);
            SdlPlatform.ShowError(GameCommandLine.ErrorTitle, e.Message);
            return 2;
        }
    }

    private static int Run(GameCommandLine command)
    {
        var install = GameInstall.Find(Environment.GetEnvironmentVariable(GameDirectoryVariable))
            ?? throw new IOException("resource.hf/.pf not found: run the client from the game folder.");
        var settings = ClientSettings.Load(Path.Combine(AppContext.BaseDirectory, ClientSettings.FileName));

        using var platform = new SdlPlatform(WindowTitle, fullscreen: false, hidden: false);
        var renderer = platform.Renderer;
        Console.WriteLine($"renderer: {renderer.BackendName}");
        var resource = PackArchive.Open(install.ResourcePath);
        using var sprites = new SpriteStore(resource, renderer);
        using var font = OpenUiFont();
        var game = new GameContext(install, resource, sprites, font, command, settings);
        using var scenes = new SceneManager(id => CreateScene(id, game));
        game.Scenes = scenes;
        var cursor = new CursorView(sprites);

        scenes.Switch(SceneId.Title);
        var events = new List<InputEvent>();
        ulong lastFrameMs = 0;
        while (game.FatalError is null)
        {
            platform.Poll(events);
            foreach (var e in events)
            {
                if (e is QuitEvent)
                    return 0;
                cursor.Handle(e);
                scenes.Handle(e);
            }
            ulong now = SdlPlatform.TicksMs;
            // ✅ a scene may hold its own frame rate (the loading screen: 30 FPS)
            if (scenes.Current is { FrameIntervalMs: > 0 } paced && now - lastFrameMs < (ulong)paced.FrameIntervalMs)
            {
                SdlPlatform.Delay(1);
                continue;
            }
            lastFrameMs = now;
            renderer.BeginFrame();
            scenes.Frame(renderer, now);
            cursor.Draw(renderer, now);
            renderer.EndFrame();
        }
        Console.Error.WriteLine(game.FatalError);
        SdlPlatform.ShowError(GameCommandLine.ErrorTitle, game.FatalError);
        return 3;
    }

    private static IScene CreateScene(SceneId id, GameContext game) => id switch
    {
        SceneId.Title => new TitleScene(game),
        SceneId.Server => new ServerScene(game, Console.WriteLine),
        SceneId.CharacterSelect => new CharacterSelectScene(game),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown scene"),
    };

    /// <summary>Text is optional: without a usable font the client still runs, only without labels.</summary>
    private static IFont? OpenUiFont()
    {
        var path = FontLocator.FindUiFont();
        if (path is null)
        {
            Console.Error.WriteLine("No UI font found: labels are not drawn.");
            return null;
        }
        try
        {
            return new SdlTtfFont(path, FontLocator.UiPixelSize);
        }
        catch (InvalidOperationException e)
        {
            Console.Error.WriteLine(e.Message);
            return null;
        }
    }
}
