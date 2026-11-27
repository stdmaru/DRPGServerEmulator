using System.Text;
using DRPG.Shared.Formats.Map;
using DRPG.Shared.Formats.Sprite;

namespace DRPG.Shared.Tests;

/// <summary>The <c>.map</c>, <c>.til</c> and <c>.obj</c> readers, with synthetic files (no asset of the game) and hostile input.</summary>
public sealed class MapFormatTests
{
    private static void Name(BinaryWriter w, string name)
    {
        var field = new byte[MapFile.NameBytes];
        Encoding.ASCII.GetBytes(name).CopyTo(field, 0);
        w.Write(field);
    }

    /// <summary>A map with the given cells (<c>object, type, tileset, frame, flags</c> per cell) and name lists.</summary>
    private static byte[] Map(int width, int height, (int Obj, int Type, short Tile, uint Frame, uint Flags)[] cells, string[] tiles, string[] objects,
        uint cellWidth = 64, uint cellHeight = 32)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((uint)width);
        w.Write((uint)height);
        w.Write(cellWidth);
        w.Write(cellHeight);
        for (int i = 0; i < width * height; i++)
        {
            (int Obj, int Type, short Tile, uint Frame, uint Flags) c = i < cells.Length ? cells[i] : (-1, -1, -1, 0, 0);
            w.Write(0u);
            w.Write(c.Obj);
            w.Write(c.Type);
            w.Write(c.Tile);
            w.Write((short)0);
            w.Write(c.Frame);
            w.Write(c.Flags);
        }
        w.Write((ushort)tiles.Length);
        foreach (var name in tiles) Name(w, name);
        w.Write((ushort)objects.Length);
        foreach (var name in objects) Name(w, name);
        return ms.ToArray();
    }

    /// <summary>One frame in the row format of the <c>.spr</c>: every row is one run of <paramref name="rgb"/>.</summary>
    private static ushort[] Frame(int width, int height, ushort rgb)
    {
        var words = new List<ushort> { (ushort)height };
        for (int y = 0; y < height; y++)
        {
            words.Add(1);
            words.Add(0);
            words.Add((ushort)width);
            for (int x = 0; x < width; x++) words.Add(rgb);
        }
        return [.. words];
    }

    private static byte[] Til(params (int Width, int Height, ushort Rgb)[] frames)
    {
        var words = new List<ushort>();
        var offsets = new List<uint>();
        foreach (var f in frames)
        {
            offsets.Add((uint)words.Count);
            words.AddRange(Frame(f.Width, f.Height, f.Rgb));
        }
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes(TileSetFile.Signature));
        w.Write(640u);
        w.Write(640u);
        w.Write((uint)frames.Length);
        foreach (var f in frames) w.Write((uint)f.Width);
        foreach (var f in frames) w.Write((uint)f.Height);
        foreach (var o in offsets) w.Write(o);
        w.Write((uint)words.Count);
        foreach (var word in words) w.Write(word);
        return ms.ToArray();
    }

    private static byte[] Obj(int width, int height, int anchorX, int anchorY, bool second = true)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        Name(w, "tree");
        w.Write((ushort)width);
        w.Write((ushort)height);
        w.Write((ushort)anchorX);
        w.Write((ushort)anchorY);
        w.Write(new byte[MapObjectFile.MaskBytes]);
        var image = Frame(width, height, 0xF800);
        w.Write((uint)image.Length);
        foreach (var word in image) w.Write(word);
        if (second)
        {
            w.Write((uint)image.Length);
            foreach (var word in image) w.Write(word);
        }
        return ms.ToArray();
    }

    [Fact]
    public void A_map_is_read_cell_by_cell()
    {
        var data = Map(3, 2,
        [
            (-1, -1, 0, 0, 1), (0, 1000, 1, 5, 0), (-1, 7, -1, 0, 1),
            (1, -1, 0, 199, 0x100), (-1, -1, -1, 0, 0), (-1, 8999, 1, 3, 1),
        ], ["001.til", "002.til"], ["a.obj", "b.obj"]);

        var map = MapFile.Parse(data);

        Assert.Equal((3, 2), (map.Width, map.Height));
        Assert.Equal(["001.til", "002.til"], map.TileNames);
        Assert.Equal(["a.obj", "b.obj"], map.ObjectNames);
        Assert.Equal(new MapCell(0, 1000, 1, 5, 0), map[1, 0]);
        Assert.True(map[0, 0].Walkable);
        Assert.False(map[1, 0].Walkable);
        Assert.Equal(199, map[0, 1].TileFrame);
        Assert.Equal(1, map[0, 1].Object);
        Assert.Equal(8999, map[2, 1].Type);
        Assert.Equal(0, map.InvalidCellReferences);
    }

    [Fact]
    public void A_name_in_the_korean_code_page_is_decoded_as_text_not_as_latin1()
    {
        // ✅ the maps of the game have names like 074_자갈.obj; the bytes are code page 949 (the last field of the file is the object name)
        var data = Map(1, 1, [(0, -1, -1, 0, 1)], [], ["074_xxxx.obj"]);
        byte[] korean = [0x30, 0x37, 0x34, 0x5f, 0xc0, 0xda, 0xb0, 0xa5, 0x2e, 0x6f, 0x62, 0x6a];     // "074_자갈.obj"
        Array.Clear(data, data.Length - MapFile.NameBytes, MapFile.NameBytes);
        korean.CopyTo(data, data.Length - MapFile.NameBytes);

        var map = MapFile.Parse(data);

        Assert.Equal(["074_\uc790\uac08.obj"], map.ObjectNames);
    }

    [Fact]
    public void A_cell_outside_the_map_is_empty()
    {
        var map = MapFile.Parse(Map(1, 1, [(-1, -1, 0, 0, 1)], ["a.til"], []));

        Assert.Equal(MapCell.None, map[5, 0].TileSet);
        Assert.Equal(MapCell.None, map[-1, 0].TileSet);
        Assert.Equal(MapCell.None, map[0, 99].Object);
    }

    [Fact]
    public void A_cell_index_outside_the_lists_is_cleared_and_counted()
    {
        // the old maps carry junk in the cells (0xBAADF0xx); the original warns and draws nothing there
        var data = Map(2, 1, [(160, -1, 0, 0, 1), (0, -1, 3, 0, 1)], ["a.til"], ["a.obj"]);

        var map = MapFile.Parse(data);

        Assert.Equal(MapCell.None, map[0, 0].Object);
        Assert.Equal(MapCell.None, map[1, 0].TileSet);
        Assert.Equal(0, map[1, 0].Object);
        Assert.Equal(2, map.InvalidCellReferences);
    }

    [Fact]
    public void A_tile_frame_outside_the_tileset_clears_the_ground_of_the_cell()
    {
        var map = MapFile.Parse(Map(1, 1, [(-1, -1, 0, 0xBAADF00D, 1)], ["a.til"], []));

        Assert.Equal(MapCell.None, map[0, 0].TileSet);
        Assert.Equal(1, map.InvalidCellReferences);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(MapFile.MaxSide + 1, 1)]
    public void A_map_size_outside_the_limits_is_a_format_error(int width, int height)
    {
        var data = Map(1, 1, [], [], []);
        BitConverter.GetBytes(width).CopyTo(data, 0);
        BitConverter.GetBytes(height).CopyTo(data, 4);

        Assert.Throws<MapFormatException>(() => MapFile.Parse(data));
    }

    [Fact]
    public void A_huge_size_does_not_allocate_before_it_is_checked()
    {
        var data = new byte[16];
        BitConverter.GetBytes(MapFile.MaxSide).CopyTo(data, 0);
        BitConverter.GetBytes(MapFile.MaxSide).CopyTo(data, 4);
        BitConverter.GetBytes(64).CopyTo(data, 8);
        BitConverter.GetBytes(32).CopyTo(data, 12);

        Assert.Throws<MapFormatException>(() => MapFile.Parse(data));
    }

    [Fact]
    public void A_cell_size_other_than_64_by_32_is_refused()
    {
        Assert.Throws<MapFormatException>(() => MapFile.Parse(Map(1, 1, [], [], [], cellWidth: 32, cellHeight: 32)));
    }

    [Fact]
    public void More_names_than_the_original_allows_is_refused()
    {
        var tiles = Enumerable.Range(0, MapFile.MaxTileSets + 1).Select(i => $"{i}.til").ToArray();
        var objects = Enumerable.Range(0, MapFile.MaxObjects + 1).Select(i => $"{i}.obj").ToArray();

        Assert.Throws<MapFormatException>(() => MapFile.Parse(Map(1, 1, [], tiles, [])));
        Assert.Throws<MapFormatException>(() => MapFile.Parse(Map(1, 1, [], [], objects)));
        Assert.Equal(MapFile.MaxTileSets, MapFile.Parse(Map(1, 1, [], tiles[..MapFile.MaxTileSets], [])).TileNames.Count);
    }

    [Fact]
    public void Every_truncation_of_a_map_is_a_format_error_and_never_a_crash()
    {
        var data = Map(3, 2, [(0, 1, 0, 1, 1)], ["a.til"], ["a.obj"]);

        for (int length = 0; length < data.Length; length++)
            Assert.Throws<MapFormatException>(() => MapFile.Parse(data.AsSpan(0, length)));
        MapFile.Parse(data);
    }

    [Fact]
    public void Random_bytes_never_escape_as_another_exception()
    {
        var random = new Random(7);
        var valid = Map(4, 4, [(0, 1, 0, 1, 1)], ["a.til"], ["a.obj"]);
        for (int round = 0; round < 500; round++)
        {
            var data = (byte[])valid.Clone();
            for (int k = 0; k < 6; k++)
                data[random.Next(data.Length)] = (byte)random.Next(256);
            try
            {
                MapFile.Parse(data);
            }
            catch (MapFormatException)
            {
            }
        }
    }

    [Fact]
    public void A_tileset_frame_decodes_to_its_pixels()
    {
        var til = TileSetFile.Parse(Til((64, 32, 0xF800), (4, 2, 0x001F)));

        Assert.Equal(2, til.FrameCount);
        Assert.Equal(new SpriteFrameInfo(64, 32), til.Sprite.Frames[0]);
        var pixels = til.DecodeFrame(0);
        Assert.Equal(64 * 32, pixels.Length);
        Assert.All(pixels, p => Assert.Equal(SpriteFile.ToRgba(0xF800), p));
        Assert.Equal(SpriteFile.ToRgba(0x001F), til.DecodeFrame(1)[0]);
    }

    [Fact]
    public void A_tile_frame_takes_its_height_from_its_rows_not_from_the_height_table()
    {
        var data = Til((64, 32, 0xF800));
        int heightTable = TileSetFile.Signature.Length + 8 + 4 + 4;           // after the signature, the sheet size, the count and one width
        BitConverter.GetBytes(1u).CopyTo(data, heightTable);                  // the real files say 1, 16 or 24 for frames of 32 rows

        var til = TileSetFile.Parse(data);

        Assert.Equal(new SpriteFrameInfo(64, 32), til.Sprite.Frames[0]);
        Assert.Equal(64 * 32, til.DecodeFrame(0).Length);
    }

    [Fact]
    public void A_tileset_with_a_wrong_signature_or_a_huge_count_is_refused()
    {
        var data = Til((4, 2, 0));
        data[0] ^= 0xFF;
        Assert.Throws<MapFormatException>(() => TileSetFile.Parse(data));

        var many = Til((4, 2, 0));
        BitConverter.GetBytes(uint.MaxValue).CopyTo(many, TileSetFile.Signature.Length + 8);
        Assert.Throws<MapFormatException>(() => TileSetFile.Parse(many));
    }

    [Fact]
    public void Every_truncation_of_a_tileset_is_a_format_error()
    {
        var data = Til((4, 2, 0x1234), (3, 3, 0x4321));

        for (int length = 0; length < data.Length; length++)
            Assert.Throws<MapFormatException>(() => TileSetFile.Parse(data.AsSpan(0, length)));
    }

    [Fact]
    public void A_tile_frame_that_starts_outside_the_image_is_refused()
    {
        var data = Til((4, 2, 0));
        int offsetTable = TileSetFile.Signature.Length + 8 + 4 + 4 + 4;      // after the signature, the sheet size, the count, one width and one height
        BitConverter.GetBytes(0x7FFFFFFFu).CopyTo(data, offsetTable);

        Assert.Throws<MapFormatException>(() => TileSetFile.Parse(data));
    }

    [Fact]
    public void A_tile_frame_with_a_bad_row_count_fails_when_decoded_not_when_read()
    {
        var data = Til((4, 2, 0x1234));
        int firstWord = data.Length - Frame(4, 2, 0).Length * 2;
        BitConverter.GetBytes((ushort)9).CopyTo(data, firstWord);              // 9 rows for a 2-row frame

        var til = TileSetFile.Parse(data);

        Assert.Throws<SpriteFormatException>(() => til.DecodeFrame(0));
    }

    [Fact]
    public void An_object_has_its_image_anchor_and_second_image()
    {
        var obj = MapObjectFile.Parse(Obj(100, 90, 590, 333));

        Assert.Equal("tree", obj.Name);
        Assert.Equal((100, 90), (obj.Width, obj.Height));
        Assert.Equal((590, 333), (obj.AnchorX, obj.AnchorY));
        Assert.True(obj.HasShadow);
        Assert.Equal(100 * 90, obj.DecodeImage().Length);
        Assert.False(MapObjectFile.Parse(Obj(8, 8, 0, 0, second: false)).HasShadow);
    }

    [Fact]
    public void Every_truncation_of_an_object_is_a_format_error_or_drops_only_the_second_image()
    {
        var data = Obj(6, 5, 1, 2);
        int firstImageEnd = MapObjectFile.NameBytes + 8 + MapObjectFile.MaskBytes + 4 + Frame(6, 5, 0).Length * 2;

        for (int length = 0; length < data.Length; length++)
        {
            if (length is >= 0 && length < firstImageEnd)
                Assert.Throws<MapFormatException>(() => MapObjectFile.Parse(data.AsSpan(0, length)));
            else
                Assert.False(Record.Exception(() => MapObjectFile.Parse(data.AsSpan(0, length))) is { } e && e is not MapFormatException);
        }
    }

    [Fact]
    public void An_object_with_a_huge_size_or_word_count_is_refused()
    {
        var big = Obj(6, 5, 0, 0);
        BitConverter.GetBytes((ushort)ushort.MaxValue).CopyTo(big, MapObjectFile.NameBytes);
        Assert.Throws<MapFormatException>(() => MapObjectFile.Parse(big));

        var words = Obj(6, 5, 0, 0);
        BitConverter.GetBytes(uint.MaxValue).CopyTo(words, MapObjectFile.NameBytes + 8 + MapObjectFile.MaskBytes);
        Assert.Throws<MapFormatException>(() => MapObjectFile.Parse(words));
    }
}
