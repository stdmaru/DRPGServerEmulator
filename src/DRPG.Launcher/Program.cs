using DRPG.Launcher.Assets;
using DRPG.Launcher.Platform;
using DRPG.Launcher.Scenes;
using DRPG.Shared.Formats.Pack;

namespace DRPG.Launcher;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? gameDir = Option(args, "--game-dir");
        string? screenshot = Option(args, "--screenshot"); // render one frame, save it and exit (tests, CI)
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
        var scene = new LoginScene(sprites);

        if (screenshot is not null)
        {
            renderer.BeginFrame();
            scene.Draw(renderer, 0);
            var pixels = renderer.Capture();
            renderer.EndFrame();
            Screenshot.WritePpm(screenshot, pixels, Rendering.IRenderer.FrameWidth, Rendering.IRenderer.FrameHeight);
            return 0;
        }

        var events = new List<InputEvent>();
        while (!scene.CloseRequested)
        {
            platform.Poll(events);
            foreach (var e in events)
            {
                if (e is QuitEvent) return 0;
                scene.Handle(e);
            }
            renderer.BeginFrame();
            scene.Draw(renderer, SdlPlatform.TicksMs);
            renderer.EndFrame();
        }
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
