using System.Text;
using DRPG.Shared.Formats.Sprite;

namespace DRPG.Shared.Tests;

public sealed class AniTests
{
    /// <summary>A file with <paramref name="frames"/> frames; animation 1 direction 0 = 0..7, animation 5 direction 0 = 64..71.</summary>
    private static byte[] Build(int frames = 2, string signature = AniFile.Signature, string name = "a_00.spr")
    {
        var data = new byte[0x2a + frames * 12 + 0x3c0 + 6240];
        Encoding.ASCII.GetBytes(signature).CopyTo(data, 0);
        Encoding.ASCII.GetBytes(name).CopyTo(data, 0x0e);
        BitConverter.GetBytes(frames).CopyTo(data, 0x26);
        for (int i = 0; i < frames; i++)
        {
            BitConverter.GetBytes(6).CopyTo(data, 0x2a + i * 12);
            BitConverter.GetBytes(120).CopyTo(data, 0x2a + i * 12 + 4);
            BitConverter.GetBytes(162).CopyTo(data, 0x2a + i * 12 + 8);
        }
        void Set(int animation, int direction, ushort start, ushort end, ushort count)
        {
            int at = 0x2a + frames * 12 + (animation * 8 + direction) * 6;
            BitConverter.GetBytes(start).CopyTo(data, at);
            BitConverter.GetBytes(end).CopyTo(data, at + 2);
            BitConverter.GetBytes(count).CopyTo(data, at + 4);
        }
        Set(1, 0, 0, 7, 8);
        Set(5, 0, 64, 71, 8);
        return data;
    }

    [Fact]
    public void Reads_the_name_the_anchors_and_the_ranges()
    {
        var ani = AniFile.Parse(Build());

        Assert.Equal("a_00", ani.SpriteName);
        Assert.Equal(new AniFrame(6, 120, 162), ani.Frames[1]);
        Assert.Equal(new AniRange(0, 7, 8), ani.Range(1, 0));
        Assert.Equal(new AniRange(64, 71, 8), ani.Range(5, 0));
    }

    [Fact]
    public void Animation_fifteen_is_five()
    {
        var ani = AniFile.Parse(Build());

        Assert.Equal(ani.Range(5, 0), ani.Range(0xf, 0));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(20, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 8)]
    [InlineData(int.MaxValue, int.MinValue)]
    public void Numbers_outside_the_table_give_an_empty_range(int animation, int direction)
    {
        Assert.Equal(default, AniFile.Parse(Build()).Range(animation, direction));
    }

    [Fact]
    public void A_count_that_does_not_fit_the_file_is_refused_before_allocating()
    {
        var data = Build();
        BitConverter.GetBytes(uint.MaxValue).CopyTo(data, 0x26);

        Assert.Throws<AniFormatException>(() => AniFile.Parse(data));
    }

    [Fact]
    public void A_wrong_signature_is_refused() =>
        Assert.Throws<AniFormatException>(() => AniFile.Parse(Build(signature: "DCANIMATION0.4")));

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(0x29)]
    public void A_file_cut_inside_the_header_is_refused(int length) =>
        Assert.Throws<AniFormatException>(() => AniFile.Parse(Build().AsSpan(0, length)));

    [Fact]
    public void A_file_cut_inside_the_animation_table_is_refused()
    {
        var data = Build();

        Assert.Throws<AniFormatException>(() => AniFile.Parse(data.AsSpan(0, 0x2a + 2 * 12 + 0x3bf)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..\\evil.spr")]
    [InlineData("dir/x.spr")]
    public void A_sprite_name_that_is_empty_or_a_path_is_refused(string name) =>
        Assert.Throws<AniFormatException>(() => AniFile.Parse(Build(name: name)));

    [Fact]
    public void A_name_without_a_terminator_uses_all_24_bytes()
    {
        var data = Build(name: new string('x', 24));

        Assert.Equal(new string('x', 24), AniFile.Parse(data).SpriteName);
    }

    [Fact]
    public void Random_bytes_never_read_out_of_range()
    {
        var random = new Random(7);
        for (int i = 0; i < 300; i++)
        {
            var data = Build();
            for (int j = 0; j < 6; j++)
                data[random.Next(data.Length)] = (byte)random.Next(256);
            try
            {
                AniFile.Parse(data.AsSpan(0, random.Next(data.Length + 1)));
            }
            catch (AniFormatException)
            {
            }
        }
    }
}
