using System.IO.Compression;
using System.Net;
using System.Text;
using DRPG.Launcher.Patching;
using DRPG.Shared.Formats.Pack;

namespace DRPG.Launcher.Tests;

public sealed class PatcherTests : IDisposable
{
    private readonly string _install = Directory.CreateTempSubdirectory("drpg-install-").FullName;
    private readonly string _build = Directory.CreateTempSubdirectory("drpg-build-").FullName;
    private readonly Dictionary<string, byte[]> _files = new();

    public PatcherTests()
    {
        PackWriter.Create(Path.Combine(_install, "resource"), [("gui\\a.spr", Bytes("old a"))]);
        VersionInfo.Write(Path.Combine(_install, "VersionInfo.ini"), 240);
    }

    public void Dispose()
    {
        Directory.Delete(_install, recursive: true);
        Directory.Delete(_build, recursive: true);
    }

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static string Config(int patchVersion, int min = 0, bool maintenance = false, string newsUrl = "") => $$"""
    {
      "ServerInfo": { "Maintenance": {{(maintenance ? "true" : "false")}}, "Message": { "Title": "Down", "Msg": "Back soon" } },
      "LanuncherInfo": { "RunExeFileName": "DigimonRPG.bin" },
      "PatchInfo": { "DownloadUrl": "http://patch.test/files", "MinPatchVersion": {{min}}, "PatchVersion": {{patchVersion}},
                     "DownloadFileHeader": "Patch_%d.zip", "PatchFileHeader": "Patch_%d" },
      "MainUrl": { "NewsPageUrl": "{{newsUrl}}" }
    }
    """;

    /// <summary>A zip shaped like the official ones: patch_n.hf/.pf at the root plus loose files.</summary>
    private void AddPatch(int version, string entry, string content, params (string Name, string Content)[] loose)
    {
        var dir = Path.Combine(_build, version.ToString());
        Directory.CreateDirectory(dir);
        PackWriter.Create(Path.Combine(dir, $"patch_{version}"), [(entry, Bytes(content))]);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntryFromFile(Path.Combine(dir, $"patch_{version}.hf"), $"patch_{version}.hf");
            zip.CreateEntryFromFile(Path.Combine(dir, $"patch_{version}.pf"), $"patch_{version}.pf");
            foreach (var (name, text) in loose)
                using (var w = new StreamWriter(zip.CreateEntry(name).Open()))
                    w.Write(text);
        }
        _files[$"/files/Patch_{version}.zip"] = ms.ToArray();
    }

    private Patcher Patcher(bool allowInsecure = true) => new(new HttpClient(new FakeServer(_files)), _install, allowInsecure);

    private async Task<PatchOutcome> Run(string config, List<PatchProgress>? log = null, bool allowInsecure = true)
    {
        _files["/DR_Configuration.json"] = Bytes(config);
        var progress = new SyncProgress(log ?? []);
        return await Patcher(allowInsecure).RunAsync("http://patch.test/DR_Configuration.json", progress, default);
    }

    [Fact]
    public async Task Applies_pending_patches_in_order_and_moves_the_version()
    {
        AddPatch(241, "gui\\a.spr", "a 241", ("table.hf", "table 241"), ("Language/Map_US.bin", "map"));
        AddPatch(242, "gui\\b.spr", "b 242");
        var log = new List<PatchProgress>();

        var outcome = await Run(Config(242), log);

        Assert.IsType<PatchOutcome.UpToDate>(outcome);
        var pack = PackArchive.Open(Path.Combine(_install, "resource.hf"));
        Assert.Equal("a 241", Encoding.UTF8.GetString(pack.ReadEntry("gui\\a.spr")));
        Assert.Equal("b 242", Encoding.UTF8.GetString(pack.ReadEntry("gui\\b.spr")));
        Assert.Equal("table 241", File.ReadAllText(Path.Combine(_install, "table.hf")));
        Assert.Equal("map", File.ReadAllText(Path.Combine(_install, "Language", "Map_US.bin")));
        Assert.Equal(242, VersionInfo.Read(Path.Combine(_install, "VersionInfo.ini")));
        Assert.False(Directory.Exists(Path.Combine(_install, "patch-download")));
        Assert.Contains(log, p => p is { Phase: PatchPhase.Downloading, Version: 242, Index: 1, Count: 2 });
    }

    [Fact]
    public async Task Up_to_date_install_downloads_nothing()
    {
        Assert.IsType<PatchOutcome.UpToDate>(await Run(Config(240)));
    }

    [Fact]
    public async Task Maintenance_stops_before_patching()
    {
        var outcome = await Run(Config(242, maintenance: true));
        Assert.Equal(new PatchOutcome.Maintenance("Down", "Back soon"), outcome);
    }

    [Fact]
    public async Task Fetches_dynamic_news_from_NewsPageUrl_even_during_maintenance()
    {
        _files["/news.json"] = Bytes("""
        [
          { "Category": "Event", "Title": "Weekend Boost", "Date": "11/26", "URL": "https://example.com/boost" }
        ]
        """);
        var outcome = await Run(Config(242, maintenance: true, newsUrl: "http://patch.test/news.json"));
        var maintenance = Assert.IsType<PatchOutcome.Maintenance>(outcome);
        Assert.NotNull(maintenance.News);
        Assert.Single(maintenance.News!);
        Assert.Equal("Weekend Boost", maintenance.News![0].Title);
        Assert.Equal("11/26", maintenance.News![0].Date);
        Assert.Equal("https://example.com/boost", maintenance.News![0].Url);
    }

    [Fact]
    public async Task Missing_news_endpoint_does_not_block_patching()
    {
        var outcome = await Run(Config(240, newsUrl: "http://patch.test/missing-news.json"));
        var up = Assert.IsType<PatchOutcome.UpToDate>(outcome);
        Assert.Null(up.News);
    }

    [Fact]
    public async Task Install_older_than_minimum_needs_reinstall()
    {
        Assert.Equal(new PatchOutcome.NeedsReinstall(240, 241), await Run(Config(250, min: 241)));
    }

    [Fact]
    public async Task Http_is_refused_outside_development_mode()
    {
        Assert.IsType<PatchOutcome.Failed>(await Run(Config(240), allowInsecure: false));
    }

    [Fact]
    public async Task Missing_patch_fails_and_keeps_the_version()
    {
        var outcome = await Run(Config(241));
        Assert.IsType<PatchOutcome.Failed>(outcome);
        Assert.Equal(240, VersionInfo.Read(Path.Combine(_install, "VersionInfo.ini")));
    }

    [Fact]
    public async Task Entry_escaping_the_install_is_refused()
    {
        AddPatch(241, "gui\\a.spr", "a", ("../evil.txt", "x"));
        Assert.IsType<PatchOutcome.Failed>(await Run(Config(241)));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_install)!, "evil.txt")));
        Assert.Equal(240, VersionInfo.Read(Path.Combine(_install, "VersionInfo.ini")));
    }

    [Theory]
    [InlineData("Language/Map_US.bin", true)]
    [InlineData("DigimonRPG.bin", true)]
    [InlineData("../x", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("C:/x", false)]
    [InlineData("a/./b", false)]
    [InlineData("resource.pf", false)]
    [InlineData("RESOURCE.HF", false)]
    public void Safe_target_rules(string name, bool allowed) =>
        Assert.Equal(allowed, Patching.Patcher.SafeTarget(_install, name) is not null);

    private sealed class FakeServer(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(files.TryGetValue(request.RequestUri!.AbsolutePath, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    /// <summary>Records reports synchronously (Progress&lt;T&gt; would post them later).</summary>
    private sealed class SyncProgress(List<PatchProgress> log) : IProgress<PatchProgress>
    {
        public void Report(PatchProgress value) => log.Add(value);
    }
}
