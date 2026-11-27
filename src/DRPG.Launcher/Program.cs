using DRPG.Engine.Assets;
using DRPG.Launcher.Login;
using DRPG.Launcher.Patching;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;
using DRPG.Engine.Rendering.Backends;
using DRPG.Engine.Scenes;
using DRPG.Launcher.Scenes;
using DRPG.Shared.Formats.Pack;

namespace DRPG.Launcher;

internal static class Program
{
    private const uint LauncherVersion = 1;

    private static int Main(string[] args)
    {
        string? gameDir = Option(args, "--game-dir");
        string? screenshot = Option(args, "--screenshot"); // render one frame, save it and exit (checks without a display)
        string? screenshotPatch = Option(args, "--screenshot-patch"); // with --screenshot: patch screen at this fraction
        bool fullscreen = args.Contains("--fullscreen");

        var install = GameInstall.Find(gameDir);
        if (install is null)
        {
            Console.Error.WriteLine("resource.hf/.pf not found: run the launcher from the game folder or pass --game-dir.");
            return 2;
        }

        using var platform = new SdlPlatform("Digimon RPG", fullscreen, hidden: screenshot is not null);
        var renderer = platform.Renderer;
        Console.WriteLine($"renderer: {renderer.BackendName}");
        using var sprites = new SpriteStore(PackArchive.Open(install.ResourcePath), renderer);
        using IFont? font = OpenUiFont();
        var remembered = RememberedLogin.Load(Path.Combine(install.Directory, "option.ini"));
        using var login = new LoginScene(sprites, remembered, font, renderer);
        using var newsView = new NewsView(sprites, font, renderer);
        using var patchView = new PatchView(sprites, font, renderer);
        var cursor = new CursorView(sprites);

        if (screenshot is not null)
        {
            double? fraction = screenshotPatch is null ? null : double.Parse(screenshotPatch, System.Globalization.CultureInfo.InvariantCulture);
            renderer.BeginFrame();
            bool patchComplete = fraction is null;
            login.PanelAvailable = patchComplete;
            var sampleNews = new PatchConfiguration.NewsItem[]
            {
                new() { Category = "Event", Title = "Double EXP weekend in Digital World" },
                new() { Category = "Update", Title = "Client v322 character creation and deletion" },
                new() { Category = "Info", Title = "Welcome to Digimon RPG Preservation Server" },
            };
            var sampleState = new LauncherFlow.State(
                patchComplete ? LauncherStage.Connecting : LauncherStage.Patching,
                patchComplete ? "Connecting to server..." : "",
                null,
                patchComplete ? "Up to date (v322) - Ready" : "Downloading Patch_322.zip (1/1)  12.3 / 27.1 MB",
                fraction ?? 1.0,
                patchComplete,
                sampleNews);
            DrawFrame(renderer, login, newsView, patchView, sampleState, 0);
            var pixels = renderer.Capture();
            renderer.EndFrame();
            Screenshot.WritePpm(screenshot, pixels, IRenderer.FrameWidth, IRenderer.FrameHeight);
            return 0;
        }

        LauncherSettings settings;
        try
        {
            settings = LauncherSettings.Load(Path.Combine(AppContext.BaseDirectory, LauncherSettings.FileName));
        }
        catch (LauncherSettingsException e)
        {
            Console.Error.WriteLine(e.Message);
            return 3;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var stop = new CancellationTokenSource();
        var flow = new LauncherFlow(
            settings,
            new Patcher(http, install.Directory, settings.AllowInsecure),
            server => new LoginClient(new LoginClient.Options(
                LoginClient.Tcp(server.Host, server.Port),
                LauncherVersion,
                settings.AllowInsecure ? null : server.KeyFingerprint,
                TimeSpan.FromSeconds(10))),
            LauncherFlow.DefaultRetryDelay);
        flow.Start(stop.Token);

        var events = new List<InputEvent>();
        var previousStage = LauncherStage.Patching;
        while (!login.CloseRequested)
        {
            var state = flow.Snapshot;
            login.PanelAvailable = state.PatchComplete
                && state.Stage is LauncherStage.Ready or LauncherStage.LoggingIn or LauncherStage.LoggedIn;
            login.Interactive = state.Stage == LauncherStage.Ready; // connected to the server and idle
            if (previousStage == LauncherStage.LoggingIn && state.Stage != LauncherStage.LoggingIn)
                login.ClearPassword(); // refused or failed: typed again; on success it is not needed any more
            previousStage = state.Stage;

            if (state.Stage == LauncherStage.LoggedIn && flow.GameLogin is { } game)
            {
                remembered.LoggedIn(login.Login);
                var error = GameStarter.Start(install.Directory, flow.Configuration!.LauncherInfo.RunExeFileName,
                    settings.Runner, game.GameUsername, game.GameKey);
                if (error is null)
                    return Quit(stop);
                Console.Error.WriteLine(error);
                flow.GameStartFailed(error);
                continue;
            }

            platform.Poll(events);
            foreach (var e in events)
            {
                if (e is QuitEvent)
                    return Quit(stop);
                login.Handle(e);
                newsView.Handle(e);
                cursor.Handle(e);
            }
            if (login.SubmitRequested)
            {
                login.SubmitRequested = false;
                flow.Login(login.Login, login.Password, stop.Token);
            }

            renderer.BeginFrame();
            DrawFrame(renderer, login, newsView, patchView, state, SdlPlatform.TicksMs);
            cursor.Draw(renderer, SdlPlatform.TicksMs);
            renderer.EndFrame();
        }
        return Quit(stop);
    }

    private static void DrawFrame(IRenderer r, LoginScene login, NewsView news, PatchView patch, LauncherFlow.State state, ulong nowMs)
    {
        login.DrawBackground(r);
        news.Draw(r, state.News, loading: state.Stage == LauncherStage.Patching && !state.PatchComplete);
        patch.Draw(r, state.PatchStatus, state.PatchDetail, state.PatchFraction, state.PatchComplete);
        login.DrawPanel(r, nowMs);
        if (state.PatchComplete && state.Status.Length > 0)
            login.DrawStatusBox(r, state.Status);
    }

    /// <summary>Text is optional: without a usable font the launcher still works, only without labels.</summary>
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
            var font = new SdlTtfFont(path, FontLocator.UiPixelSize);
            Console.WriteLine($"font: {path}");
            return font;
        }
        catch (InvalidOperationException e)
        {
            Console.Error.WriteLine(e.Message);
            return null;
        }
    }

    private static int Quit(CancellationTokenSource stop)
    {
        stop.Cancel();
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
