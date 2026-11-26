using DRPG.Engine.Rendering;

namespace DRPG.Engine.Tests;

public sealed class FontLocatorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("drpg-fonts-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Font_search_prefers_the_name_order_over_the_directory_order()
    {
        var a = Directory.CreateDirectory(Path.Combine(_dir, "a", "deep")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(_dir, "b")).FullName;
        File.WriteAllText(Path.Combine(a, "DejaVuSans.ttf"), "");
        File.WriteAllText(Path.Combine(b, "gulim.ttc"), "");

        Assert.Equal(Path.Combine(b, "gulim.ttc"),
            FontLocator.Find([Path.Combine(_dir, "a"), b, Path.Combine(_dir, "missing")], ["gulim.ttc", "DejaVuSans.ttf"]));
        Assert.Null(FontLocator.Find([b], ["nothing.ttf"]));
    }
}
