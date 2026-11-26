using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Formats.Sprite;

namespace DRPG.Shared.Tests;

public sealed class SpriteTests
{
    private const ushort Red = 0xF800, White = 0xFFFF;

    // Two frames: 3x2 with a transparent gap, and a zero-sized one.
    private static byte[] Build(bool shadow = false)
    {
        ushort[] frame0 = [2, 2, 0, 1, Red, 1, 1, White, 1, 1, 2, White, Red];
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((uint)2);
        w.Write((uint)frame0.Length);
        w.Write((uint)0); w.Write((uint)0);
        w.Write((uint)3); w.Write((uint)0);
        w.Write((uint)2); w.Write((uint)0);
        foreach (var v in frame0) w.Write(v);
        if (shadow)
        {
            ushort[] s = [2, 1, 0, 3, 0, 0, 0, 0];
            w.Write((uint)s.Length);
            w.Write((uint)0); w.Write((uint)0);
            foreach (var v in s) w.Write(v);
        }
        else
        {
            w.Write((uint)0);
        }
        w.Write((uint)0);
        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void Decodes_runs_with_transparent_gaps()
    {
        var sprite = SpriteFile.Parse(Build());

        Assert.Equal(new SpriteFrameInfo(3, 2), sprite.Frames[0]);
        Assert.Null(sprite.Shadow);
        uint red = SpriteFile.ToRgba(Red), white = SpriteFile.ToRgba(White);
        Assert.Equal([red, 0, white, 0, white, red], sprite.Decode(sprite.Image, 0));
        Assert.Empty(sprite.Decode(sprite.Image, 1));
    }

    [Fact]
    public void Reads_the_shadow_layer()
    {
        var sprite = SpriteFile.Parse(Build(shadow: true));

        var shadow = sprite.Decode(sprite.Shadow!, 0);
        Assert.Equal(SpriteFile.ToRgba(0), shadow[0]);
        Assert.Equal(0u, shadow[3]);
    }

    [Fact]
    public void Rgb565_expands_to_full_range()
    {
        Assert.Equal(0xFFFFFFFFu, SpriteFile.ToRgba(0xFFFF));
        Assert.Equal(0xFF0000FFu, SpriteFile.ToRgba(Red));
    }

    [Fact]
    public void Rejects_truncated_and_trailing_data()
    {
        var data = Build();
        Assert.Throws<SpriteFormatException>(() => SpriteFile.Parse(data.AsSpan(0, data.Length - 10)));
        Assert.Throws<SpriteFormatException>(() => SpriteFile.Parse([.. data, 0, 0, 0, 0, 1]));
    }

    [Fact]
    public void Rejects_runs_past_the_row()
    {
        var data = Build();
        data[8 + 24 + 2 * 3] = 9; // first run length
        var sprite = SpriteFile.Parse(data);
        Assert.Throws<SpriteFormatException>(() => sprite.Decode(sprite.Image, 0));
    }

    /// <summary>Decodes every GUI sprite of a real install when DRPG_CLIENT_DIR points at one.</summary>
    [Fact]
    public void Decodes_every_gui_sprite_of_a_real_install()
    {
        var dir = Environment.GetEnvironmentVariable("DRPG_CLIENT_DIR");
        if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "resource.hf")))
            return;
        var pack = PackArchive.Open(Path.Combine(dir, "resource.hf"));
        int decoded = 0;
        foreach (var name in pack.Names.Where(n => n.StartsWith("gui\\", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".spr", StringComparison.OrdinalIgnoreCase)))
        {
            var sprite = SpriteFile.Parse(pack.ReadEntry(name));
            for (int f = 0; f < sprite.Frames.Count; f++)
                sprite.Decode(sprite.Image, f);
            decoded++;
        }
        Assert.True(decoded > 0);
    }
}
