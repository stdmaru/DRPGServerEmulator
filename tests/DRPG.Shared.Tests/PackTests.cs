using System.Buffers.Binary;
using System.Text;
using DRPG.Shared.Formats.Pack;

namespace DRPG.Shared.Tests;

public sealed class PackTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("drpg-pack-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string P(string name) => Path.Combine(_dir, name);

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Written_pack_reads_back_every_entry()
    {
        PackWriter.Create(P("resource"), [("gui\\dr_title_000.spr", Bytes("title")), ("map/r001.map", Bytes("map"))]);

        var pack = PackArchive.Open(P("resource.hf"));

        Assert.Equal(PackVersion.Plain, pack.Index.Version);
        Assert.Equal(2, pack.Names.Count);
        Assert.Equal("title", Encoding.UTF8.GetString(pack.ReadEntry("GUI\\DR_TITLE_000.SPR")));
        Assert.Equal("map", Encoding.UTF8.GetString(pack.ReadEntry("map\\r001.map")));
        Assert.False(pack.Contains("gui\\missing.spr"));
    }

    [Fact]
    public void Reads_a_keyed_index_like_the_official_patches()
    {
        PackWriter.Create(P("patch_240"), [("gui\\a.spr", Bytes("new"))]);
        RewriteAsKeyed(P("patch_240.hf"));

        var pack = PackArchive.Open(P("patch_240.hf"));

        Assert.Equal(PackVersion.Keyed, pack.Index.Version);
        Assert.Equal("new", Encoding.UTF8.GetString(pack.ReadEntry("gui\\a.spr")));
    }

    [Fact]
    public void Index_pointing_outside_the_data_file_is_rejected()
    {
        PackWriter.Create(P("resource"), [("gui\\a.spr", Bytes("0123456789"))]);
        using (var f = new FileStream(P("resource.pf"), FileMode.Open)) f.SetLength(PackWriter.DataHeaderSize + 2);

        Assert.Throws<PackFormatException>(() => PackArchive.Open(P("resource.hf")));
    }

    [Fact]
    public void Truncated_index_is_rejected()
    {
        PackWriter.Create(P("resource"), [("gui\\a.spr", Bytes("a"))]);
        File.WriteAllBytes(P("resource.hf"), File.ReadAllBytes(P("resource.hf"))[..10]);

        Assert.Throws<PackFormatException>(() => PackArchive.Open(P("resource.hf")));
    }

    [Theory]
    [InlineData("..\\outside.spr")]
    [InlineData("gui\\..\\..\\x")]
    [InlineData("\\root.spr")]
    [InlineData("C:\\x.spr")]
    [InlineData("")]
    public void Unsafe_names_are_refused(string name)
    {
        Assert.False(PackPath.IsSafe(name));
        Assert.Throws<PackFormatException>(() => PackWriter.Create(P("bad"), [(name, Bytes("x"))]));
    }

    [Fact]
    public void Missing_pair_is_reported()
    {
        Assert.Throws<PackFormatException>(() => PackArchive.Open(P("nothing.hf")));
    }

    /// <summary>Re-encodes an index with the 0x20 codec (xmask(keyed(key, xorkey(key, revnot(zlib)))), as in drpg_pack.py.</summary>
    private static void RewriteAsKeyed(string hf)
    {
        var d = File.ReadAllBytes(hf);
        int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(12));
        var zlib = PackCodec.DecodeIndex(PackVersion.Plain, d.AsSpan(16, size).ToArray());
        var key = PackCodec.RandomKey();
        var xored = PackCodec.XorKey(key, PackCodec.Revnot(zlib));
        var keyed = new byte[4 + 4 * key.Length + 4 + xored.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(keyed, (uint)key.Length);
        for (int i = 0; i < key.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(keyed.AsSpan(4 + 4 * i), key[i]);
        BinaryPrimitives.WriteUInt32LittleEndian(keyed.AsSpan(4 + 4 * key.Length), (uint)xored.Length);
        xored.CopyTo(keyed.AsSpan(8 + 4 * key.Length));
        var payload = PackCodec.Xmask(keyed);

        var header = d[..16];
        BinaryPrimitives.WriteUInt32LittleEndian(header, PackVersion.Keyed);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)payload.Length);
        File.WriteAllBytes(hf, [.. header, .. payload]);
    }
}
