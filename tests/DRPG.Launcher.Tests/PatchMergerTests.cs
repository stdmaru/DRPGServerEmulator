using System.Text;
using DRPG.Launcher.Patching;
using DRPG.Shared.Formats.Pack;

namespace DRPG.Launcher.Tests;

public sealed class PatchMergerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("drpg-merge-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string P(string name) => Path.Combine(_dir, name);

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Merge_appends_streams_replaces_same_names_and_adds_new_ones()
    {
        PackWriter.Create(P("resource"), [("gui\\a.spr", Bytes("old a")), ("gui\\b.spr", Bytes("b"))]);
        PackWriter.Create(P("patch_241"), [("GUI\\A.SPR", Bytes("new a")), ("gui\\c.spr", Bytes("c"))]);
        long pfBefore = new FileInfo(P("resource.pf")).Length;

        var result = PatchMerger.Merge(P("resource.hf"), P("patch_241.hf"));

        Assert.Equal(new PatchMerger.Result(Replaced: 1, Added: 1), result);
        var pack = PackArchive.Open(P("resource.hf"));
        Assert.Equal(["gui\\a.spr", "gui\\b.spr", "gui\\c.spr"], pack.Index.Entries.Select(e => e.Name));
        Assert.Equal("new a", Encoding.UTF8.GetString(pack.ReadEntry("gui\\a.spr")));
        Assert.Equal("b", Encoding.UTF8.GetString(pack.ReadEntry("gui\\b.spr")));
        Assert.Equal("c", Encoding.UTF8.GetString(pack.ReadEntry("gui\\c.spr")));
        Assert.True(pack.Index.Entries.Single(e => e.Name == "gui\\a.spr").Offset >= pfBefore);
        Assert.False(File.Exists(P("resource.hf.tmp")));
    }
}
