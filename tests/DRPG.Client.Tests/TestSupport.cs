using DRPG.Engine.Rendering;
using DRPG.Shared.Formats.Pack;

namespace DRPG.Client.Tests;

/// <summary>A texture that remembers its size; no GPU.</summary>
internal sealed class FakeTexture(int width, int height) : ITexture
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public void Dispose()
    {
    }
}

/// <summary>An <see cref="IRenderer"/> that records the draw calls of a frame.</summary>
internal sealed class FakeRenderer : IRenderer
{
    public sealed record DrawCall(FakeTexture Texture, int X, int Y);

    public List<DrawCall> Draws { get; } = [];
    public string BackendName => "fake";

    public ITexture CreateTexture(int width, int height, ReadOnlySpan<uint> pixels) => new FakeTexture(width, height);

    public void BeginFrame() => Draws.Clear();

    public void Draw(ITexture texture, int x, int y) => Draws.Add(new DrawCall((FakeTexture)texture, x, y));

    public void DrawPart(ITexture texture, int x, int y, int sourceX, int width) => Draws.Add(new DrawCall((FakeTexture)texture, x, y));

    public void EndFrame()
    {
    }

    public uint[] Capture() => [];

    public void Dispose()
    {
    }
}

/// <summary>A game folder with synthetic packs: <c>resource</c> with the loading screen sprites, <c>table</c> and one language file.</summary>
internal sealed class FakeInstall : IDisposable
{
    public string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("drpg-client-").FullName;

    /// <summary>Frames of the loading bar sprite.</summary>
    public const int BarFrames = 11;

    public FakeInstall(bool withTable = true)
    {
        PackWriter.Create(Path.Combine(Directory, "resource.hf"), new (string, byte[])[]
        {
            ("gui\\dr2_in_000_000_eng.spr", Sprite(800, 600)),
            ("gui\\dr2_in_001_000.spr", Sprite(222, 215)),
            ("gui\\dr2_in_001_001.spr", Sprite(352, 60)),
            ("gui\\dr2_in_001_002.spr", Sprite(213, 18, BarFrames)),
            ("gui\\dr2_in_000_000.spr", Sprite(800, 600)),
            ("gui\\dr2_ch_000_000.spr", Sprite(800, 600)),
            ("interface\\cursor_normal.spr", Sprite(36, 36)),
            ("interface\\cursor_click.spr", Sprite(36, 36)),
        });
        if (withTable)
            PackWriter.Create(Path.Combine(Directory, "table.hf"), new (string, byte[])[] { ("table\\x.dat", [1, 2, 3]) });
        System.IO.Directory.CreateDirectory(Path.Combine(Directory, "Language"));
        File.WriteAllBytes(Path.Combine(Directory, "Language", "Test_US.bin"), [9, 9]);
    }

    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);

    /// <summary>A fully transparent <c>.spr</c> of <paramref name="frames"/> frames of the given size (layout of SpriteTests).</summary>
    private static byte[] Sprite(int width, int height, int frames = 1)
    {
        var words = new List<ushort>();
        var offsets = new List<uint>();
        for (int i = 0; i < frames; i++)
        {
            offsets.Add((uint)words.Count);
            words.Add((ushort)height);                      // rows
            for (int row = 0; row < height; row++)
                words.Add(0);                               // no runs in the row
        }
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((uint)frames);
        w.Write((uint)words.Count);
        foreach (var offset in offsets) w.Write(offset);
        for (int i = 0; i < frames; i++) w.Write((uint)width);
        for (int i = 0; i < frames; i++) w.Write((uint)height);
        foreach (var word in words) w.Write(word);
        w.Write((uint)0);                                   // no shadow layer
        w.Write((uint)0);                                   // no extra layer
        return ms.ToArray();
    }
}
