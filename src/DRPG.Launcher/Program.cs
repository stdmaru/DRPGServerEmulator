using DRPG.Launcher.Assets;
using DRPG.Launcher.Login;
using DRPG.Launcher.Patching;
using DRPG.Launcher.Platform;
using DRPG.Launcher.Rendering;
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
        var login = new LoginScene(sprites);
        var patchView = new PatchView(sprites);

        if (screenshot is not null)
        {
            double? fraction = screenshotPatch is null ? null : double.Parse(screenshotPatch, System.Globalization.CultureInfo.InvariantCulture);
            renderer.BeginFrame();
            DrawFrame(renderer, login, patchView, fraction is null ? LauncherStage.Ready : LauncherStage.Patching, fraction ?? 0, 0);
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
                TimeSpan.FromSeconds(10))));
        flow.Start(stop.Token);

        var events = new List<InputEvent>();
        while (!login.CloseRequested)
        {
            var state = flow.Snapshot;
            login.Interactive = state.Stage is LauncherStage.Ready or LauncherStage.Failed;

            platform.Poll(events);
            foreach (var e in events)
            {
                if (e is QuitEvent)
                    return Quit(stop);
                login.Handle(e);
            }
            if (login.SubmitRequested)
            {
                login.SubmitRequested = false;
                if (state.Stage == LauncherStage.Failed)
                    flow.Retry();
                // Ready: the login itself comes with the text fields (next step).
            }

            renderer.BeginFrame();
            DrawFrame(renderer, login, patchView, state.Stage, state.Progress?.Fraction ?? 0, SdlPlatform.TicksMs);
            renderer.EndFrame();
        }
        return Quit(stop);
    }

    private static void DrawFrame(IRenderer r, LoginScene login, PatchView patch, LauncherStage stage, double fraction, ulong nowMs)
    {
        login.DrawBackground(r);
        if (stage == LauncherStage.Patching)
            patch.Draw(r, fraction);
        else
            login.DrawPanel(r, nowMs);
        login.DrawStatusBox(r);
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

/// <summary>Binary PPM: no image library needed, every viewer and Pillow read it.</summary>
internal static class Screenshot
{
    public static void WritePpm(string path, uint[] rgba, int width, int height)
    {
        using var f = File.Create(path);
        f.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
        var row = new byte[width * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                uint p = rgba[y * width + x];
                row[x * 3] = (byte)p;
                row[x * 3 + 1] = (byte)(p >> 8);
                row[x * 3 + 2] = (byte)(p >> 16);
            }
            f.Write(row);
        }
    }
}
