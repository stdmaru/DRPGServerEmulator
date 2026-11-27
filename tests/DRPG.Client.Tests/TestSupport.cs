using DRPG.Client.Scenes;
using DRPG.Client.Ui;
using DRPG.Engine.Assets;
using DRPG.Engine.Audio;
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
    public sealed record DrawCall(FakeTexture Texture, int X, int Y, int? PartWidth = null, int Alpha = IRenderer.OpaqueAlpha, int? PartHeight = null);

    public List<DrawCall> Draws { get; } = [];
    public string BackendName => "fake";

    public ITexture CreateTexture(int width, int height, ReadOnlySpan<uint> pixels) => new FakeTexture(width, height);

    public void BeginFrame()
    {
        Draws.Clear();
        Darkens.Clear();
    }

    public void Draw(ITexture texture, int x, int y) => Draws.Add(new DrawCall((FakeTexture)texture, x, y));

    public void DrawPart(ITexture texture, int x, int y, int sourceX, int width) => Draws.Add(new DrawCall((FakeTexture)texture, x, y, width));

    public void DrawRegion(ITexture texture, int x, int y, int sourceX, int sourceY, int width, int height) =>
        Draws.Add(new DrawCall((FakeTexture)texture, x, y, width, PartHeight: height));

    public void DrawRegionBlended(ITexture texture, int x, int y, int sourceX, int sourceY, int width, int height, int alpha) =>
        Draws.Add(new DrawCall((FakeTexture)texture, x, y, width, alpha, height));

    public List<int> Darkens { get; } = [];

    public void Darken(int amount) => Darkens.Add(amount);

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
            ("gui\\dr2_in_001_002.spr", Sprite(231, 18, 2)),
            ("gui\\dr2_in_000_000.spr", Sprite(800, 600)),
            ("interface\\cursor_normal.spr", Sprite(36, 36)),
            ("interface\\cursor_click.spr", Sprite(36, 36)),
            // world selection
            ("gui\\dr2_in_002_005_eng.spr", Sprite(800, 600)),
            ("gui\\dr2_in_000_006_eng.spr", Sprite(107, 29, 3)),
            ("gui\\dr2_in_002_000.spr", Sprite(243, 256, 3)),
            ("gui\\dr2_in_002_001.spr", Sprite(242, 256, 3)),
            ("gui\\dr2_in_002_002.spr", Sprite(800, 599, 2)),
            ("gui\\dr2_in_002_003.spr", Sprite(800, 599, 2)),
            ("gui\\dr2_in_002_004.spr", Sprite(800, 599, 2)),
            ("gui\\dr2_in_002_006.spr", Sprite(255, 256, 3)),
            ("gui\\dr2_in_002_007.spr", Sprite(255, 255, 2)),
            ("gui\\dr2_in_002_008.spr", Sprite(147, 31, 2)),
            ("gui\\dr2_in_002_010.spr", Sprite(255, 256, 2)),
            ("gui\\dr2_in_000_003.spr", Sprite(14, 14, 9)),
            // character selection
            ("gui\\dr2_ch_000_000_eng.spr", Sprite(800, 600)),
            ("gui\\dr2_ch_000_001.spr", Sprite(355, 230, 2)),
            ("gui\\dr2_ch_000_002.spr", Sprite(355, 230, 2)),
            ("gui\\dr2_ch_000_003.spr", Sprite(355, 230, 2)),
            ("gui\\dr2_ch_000_004.spr", Sprite(355, 230, 2)),
            ("gui\\dr2_ch_000_005_eng.spr", Sprite(100, 30, 3)),
            ("gui\\dr2_ch_000_006_eng.spr", Sprite(100, 30, 3)),
            ("gui\\dr2_ch_000_007_eng.spr", Sprite(150, 40, 3)),
            ("gui\\dr2_ch_000_008_eng.spr", Sprite(100, 40, 3)),
            ("gui\\dr2_ch_000_009_eng.spr", Sprite(100, 30, 3)),
            // character creation
            ("gui\\dr2_ch_001_000_eng.spr", Sprite(800, 600)),
            ("gui\\dr2_ch_001_001_eng.spr", Sprite(100, 40, 3)),
            ("gui\\dr2_ch_001_002_eng.spr", Sprite(100, 40, 3)),
            ("gui\\dr2_ch_001_003.spr", Sprite(107, 154, 3)),
            ("gui\\dr2_ch_001_004.spr", Sprite(107, 154, 3)),
            ("gui\\loading_image_eng.spr", Sprite(800, 550, 3)),
            ("gui\\loading_under_line.spr", Sprite(800, 50)),
            ("gui\\game_end_gauge.spr", Sprite(40, 40, 12)),
            ("gui\\dr2_sub_016_005.spr", Sprite(57, 20, 6)),
            // the field bar: the background and its glow, the chat's background (taller than the 81 px drawn)
            ("gui\\dr2_main_000_000.spr", Sprite(800, 105, 2)),
            ("gui\\dr2_main_000_018.spr", Sprite(409, 190)),
            // portraits: tamer 1 has its own, the fallback icon_065 stands in for the others
            ("sprite\\icon_001.spr", Sprite(41, 41, 2)),
            ("sprite\\icon_065.spr", Sprite(65, 65, 2)),
            ("sprite\\a_00.spr", Sprite(100, 160, 16, layers: true)),
            ("sprite\\a_00.ani", Ani("a_00.spr", 16)),
            // the default clothes of tamer 1, without a_20 (tamer 3 of the real install has no a_20 either)
            ("sprite\\a_15_01_00.spr", Sprite(15, 15, 16)),
            ("sprite\\a_16_01_00.spr", Sprite(16, 16, 16)),
            ("sprite\\a_17_01_00.spr", Sprite(17, 17, 16)),
            ("sprite\\a_18_01_00.spr", Sprite(18, 18, 16)),
            ("sprite\\a_19_01_00.spr", Sprite(19, 19, 4)),
            ("sprite\\a_21_01_00.spr", Sprite(21, 21, 16)),
            ("sprite\\003_1.spr", Sprite(80, 80, 16, layers: true)),
            ("sprite\\003_1.ani", Ani("003_1.spr", 16)),
            ("sprite\\065_1.spr", Sprite(70, 70, 16)),
            ("sprite\\065_1.ani", Ani("065_1.spr", 16)),
            // the maps of the world entry: r001 is 20 x 20 cells without tiles or objects, r002 is not a map
            ("map\\r001.map", EmptyMap(20, 20)),
            ("map\\r002.map", [1, 2, 3]),
        });
        if (withTable)
            PackWriter.Create(Path.Combine(Directory, "table.hf"), new (string, byte[])[] { ("table\\x.dat", [1, 2, 3]) });
        System.IO.Directory.CreateDirectory(Path.Combine(Directory, "Sound"));
        foreach (var name in new[] { "menu-bt.wav", "menu-1.wav", "menu-dark.wav" })
            File.WriteAllBytes(Path.Combine(Directory, "Sound", name), SilentWav());
        System.IO.Directory.CreateDirectory(Path.Combine(Directory, "Language"));
        File.WriteAllBytes(Path.Combine(Directory, "Language", "Test_US.bin"), [9, 9]);
        File.WriteAllBytes(Path.Combine(Directory, "Language", "TextAttach_US.bin"), System.Text.Encoding.UTF8.GetBytes("\uFEFF1498|Net Ocean\r1515|(c) Test\r3825|Level %3d\r"));
    }

    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);

    /// <summary>A 16-bit mono WAV of 0.01 s of silence.</summary>
    private static byte[] SilentWav()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + 441 * 2);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(44100);
        w.Write(44100 * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(441 * 2);
        w.Write(new byte[441 * 2]);
        return ms.ToArray();
    }

    /// <summary>A <c>.map</c> of <paramref name="width"/> x <paramref name="height"/> cells without tilesets or objects (layout of MapFormatTests).</summary>
    internal static byte[] EmptyMap(int width, int height)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((uint)width);
        w.Write((uint)height);
        w.Write(64u);
        w.Write(32u);
        for (int i = 0; i < width * height; i++)
        {
            w.Write(0u);
            w.Write(-1);
            w.Write(-1);
            w.Write((short)-1);
            w.Write((short)0);
            w.Write(0u);
            w.Write(1u);
        }
        w.Write((ushort)0);
        w.Write((ushort)0);
        return ms.ToArray();
    }

    /// <summary>An <c>.ani</c>: animation 1 = frames 0..7 (8), animation 5 = 8..15 (8), every anchor (120, 162).</summary>
    internal static byte[] Ani(string sprite, int frames)
    {
        var data = new byte[0x2a + frames * 12 + 0x3c0 + 6240];
        System.Text.Encoding.ASCII.GetBytes("DCANIMATION0.5").CopyTo(data, 0);
        System.Text.Encoding.ASCII.GetBytes(sprite).CopyTo(data, 0x0e);
        BitConverter.GetBytes(frames).CopyTo(data, 0x26);
        for (int i = 0; i < frames; i++)
        {
            BitConverter.GetBytes(120).CopyTo(data, 0x2a + i * 12 + 4);
            BitConverter.GetBytes(162).CopyTo(data, 0x2a + i * 12 + 8);
        }
        void Set(int animation, int start, int count)
        {
            int at = 0x2a + frames * 12 + (animation * 8) * 6;
            BitConverter.GetBytes((ushort)start).CopyTo(data, at);
            BitConverter.GetBytes((ushort)(start + count - 1)).CopyTo(data, at + 2);
            BitConverter.GetBytes((ushort)count).CopyTo(data, at + 4);
        }
        Set(1, 0, 8);
        Set(5, 8, 8);
        return data;
    }

    /// <summary>
    /// A fully transparent <c>.spr</c> of <paramref name="frames"/> frames of the given size (layout of SpriteTests); with
    /// <paramref name="layers"/> it also has the shadow and the edge layers, transparent too.
    /// </summary>
    private static byte[] Sprite(int width, int height, int frames = 1, bool layers = false)
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
        for (int layer = 0; layer < 2; layer++)             // the shadow, then the extra (edge) layer
        {
            if (!layers)
            {
                w.Write((uint)0);                           // no layer
                continue;
            }
            w.Write((uint)words.Count);
            foreach (var offset in offsets) w.Write(offset);
            foreach (var word in words) w.Write(word);
        }
        return ms.ToArray();
    }
}

/// <summary>An <see cref="IAudio"/> that remembers what was played.</summary>
internal sealed class RecordingAudio : IAudio
{
    public sealed record Played(PcmSound Sound, float Volume, bool Loop);

    private sealed class Voice(RecordingAudio owner) : IVoice
    {
        public bool IsPlaying { get; private set; } = true;

        public void Stop()
        {
            IsPlaying = false;
            owner.Stopped++;
        }
    }

    public List<Played> Plays { get; } = [];
    public int Stopped { get; private set; }
    public string BackendName => "recording";

    public IVoice Play(PcmSound sound, float volume, bool loop)
    {
        Plays.Add(new Played(sound, volume, loop));
        return new Voice(this);
    }

    public void Update()
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>What a scene needs to run without a window: a fake install, a recording renderer and audio, a context.</summary>
internal sealed class SceneFixture : IDisposable
{
    public FakeInstall Install { get; } = new();
    public FakeRenderer Renderer { get; } = new();
    public RecordingAudio Audio { get; } = new();
    public List<string> Log { get; } = [];
    public PackArchive Resource { get; }
    public SpriteStore Sprites { get; }
    public SoundBank Sounds { get; }
    public GameContext Game { get; }

    public SceneFixture(ClientSettings? settings = null, float volume = 1f)
    {
        var install = GameInstall.Find(Install.Directory)!;
        Resource = PackArchive.Open(install.ResourcePath);
        Sprites = new SpriteStore(Resource, Renderer);
        Sounds = new SoundBank(Audio, install.SoundDirectory, volume, volume, Log.Add);
        Game = new GameContext(install, Resource, Sprites, new UiText(null), Sounds, new GameCommandLine("tamer", "0123456789ABCDEF0123456789ABCDEF01234567"),
            settings ?? ClientSettings.Local, SpriteOptions.Default, Log.Add);
        Game.Scenes = new SceneManager(_ => throw new InvalidOperationException("no scene is created in these tests"));
    }

    /// <summary>The effects the scenes play, by the frames of audio they were decoded from (all test sounds are 0.01 s of silence).</summary>
    public int EffectsPlayed => Audio.Plays.Count(p => !p.Loop);

    public void Dispose()
    {
        Sprites.Dispose();
        Install.Dispose();
    }
}
