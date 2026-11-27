using DRPG.Client.Scenes;
using DRPG.Client.Ui;
using DRPG.Engine.Assets;
using DRPG.Engine.Audio;
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

    /// <summary>Development only: <c>path.ppm</c> to save the frame shown <see cref="ScreenshotDelayVariable"/> ms after the loading screen ends (hidden window), then quit.</summary>
    private const string ScreenshotVariable = "DRPG_SCREENSHOT";

    private const string ScreenshotDelayVariable = "DRPG_SCREENSHOT_MS";

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
        var screenshot = Environment.GetEnvironmentVariable(ScreenshotVariable);
        ulong screenshotAtMs = ulong.TryParse(Environment.GetEnvironmentVariable(ScreenshotDelayVariable), out var delay) ? delay : 1000;
        var settings = ClientSettings.Load(Path.Combine(AppContext.BaseDirectory, ClientSettings.FileName));

        using var platform = new SdlPlatform(WindowTitle, fullscreen: false, hidden: screenshot is not null);
        var renderer = platform.Renderer;
        Console.WriteLine($"renderer: {renderer.BackendName}");
        var resource = PackArchive.Open(install.ResourcePath);
        using var sprites = new SpriteStore(resource, renderer);
        using var font = OpenUiFont();
        using var uiText = new UiText(font);
        using var audio = AudioFactory.Create(Console.Error.WriteLine);
        var options = IniFile.Read(install.OptionsPath);
        using var sounds = new SoundBank(audio, install.SoundDirectory, Volume(options, "bgm.sound"), Volume(options, "bgm.bgm"), Console.Error.WriteLine);
        var game = new GameContext(install, resource, sprites, uiText, sounds, command, settings, SpriteOptions.From(options), Console.WriteLine);
        using var scenes = new SceneManager(id => CreateScene(id, game));
        game.Scenes = scenes;
        var cursor = new CursorView(sprites);

        scenes.Switch(SceneId.Title);
        var events = new List<InputEvent>();
        ulong lastFrameMs = 0;
        ulong? loadedAtMs = null;                       // development clocks count from the end of the loading screen
        var script = DevScript.FromEnvironment();
        while (game.FatalError is null && !game.QuitRequested)
        {
            platform.Poll(events);
            if (game.Data is not null)
                loadedAtMs ??= SdlPlatform.TicksMs;
            if (script is not null && loadedAtMs is { } loaded)
                events.AddRange(script.Due(SdlPlatform.TicksMs - loaded));
            foreach (var e in events)
            {
                if (e is QuitEvent)
                    return 0;
                cursor.Handle(e);
                game.Mouse.Handle(e, SdlPlatform.TicksMs);
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
            audio.Update();
            renderer.BeginFrame();
            scenes.Frame(renderer, now);
            game.Mouse.EndFrame();
            if (scenes.Current?.ShowCursor ?? true)
                cursor.Draw(renderer, now);
            if (screenshot is not null && loadedAtMs is { } shotLoaded && now - shotLoaded >= screenshotAtMs)
            {
                Screenshot.WritePpm(screenshot, renderer.Capture(), IRenderer.FrameWidth, IRenderer.FrameHeight);
                return 0;
            }
            renderer.EndFrame();
        }
        if (game.FatalError is null)
            return 0;
        Console.Error.WriteLine(game.FatalError);
        SdlPlatform.ShowError(GameCommandLine.ErrorTitle, game.FatalError);
        return 3;
    }

    /// <summary>✅ <c>FUN_00b500a0(2)</c>: a return to scene 2 first shows the 3 s loading screen; the first entry does not.</summary>
    internal static IScene ServerOrReturnLoading(GameContext game)
    {
        if (!game.SkipReturnLoading)
            return new ReturnLoadingScene(game, Random.Shared);
        game.SkipReturnLoading = false;
        return new ServerScene(game);
    }

    private static IScene CreateScene(SceneId id, GameContext game) => id switch
    {
        SceneId.Title => new TitleScene(game),
        SceneId.Server => ServerOrReturnLoading(game),
        SceneId.CharacterSelect => new CharacterSelectScene(game, Random.Shared),
        SceneId.Game => new GameScene(game, Random.Shared),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown scene"),
    };

    /// <summary>
    /// ✅ The sound levels of <c>option.ini</c> (<c>[+8]</c> music, <c>[+9]</c> effects of the original). ❓ The keys are
    /// <c>[BGM] bgm</c> and <c>sound</c> by name; a missing or bad value is the file's default of 1.
    /// </summary>
    private static float Volume(Dictionary<string, string> options, string key) =>
        SoundBank.GainFromOption(options.TryGetValue(key, out var text) && int.TryParse(text, out int level) ? level : 1);

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
            // ✅ the original draws with GDI TextOutA and the bitmap GulimChe (0x00b55270): hard edges, full colour
            return new SdlTtfFont(path, FontLocator.UiPixelSize, smoothEdges: false);
        }
        catch (InvalidOperationException e)
        {
            Console.Error.WriteLine(e.Message);
            return null;
        }
    }
}
