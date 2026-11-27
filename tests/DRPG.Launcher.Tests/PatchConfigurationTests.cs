using DRPG.Launcher.Patching;

namespace DRPG.Launcher.Tests;

public sealed class PatchConfigurationTests
{
    // Same shape as the official file, plus LoginServer.
    private const string Official = """
    {
      "ServerInfo": { "Maintenance": false, "Message": { "Title": "Server Maintenance", "Msg": "Hello" } },
      "LanuncherInfo": { "DownloadPath": "https://x/Launcher", "CurrentVersion": 8, "FileName": "DigimonRPG_6.zip",
                         "LauncherFileName": "DigimonRPG.exe", "RunExeFileName": "DigimonRPG.bin" },
      "PatchInfo": { "DownloadUrl": "https://x/PatchFiles/Live/", "MinPatchVersion": 0, "PatchVersion": 242,
                     "DownloadFileHeader": "Patch_%d.zip", "PatchFileHeader": "Patch_%d" },
      "MainUrl": { "HomePageUrl": "http://a", "MainPageUrl": "http://b", "NewsPageUrl": "https://x/news.json" },
      "LoginServer": { "Host": "127.0.0.1", "Port": 7001,
                       "KeyFingerprint": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" }
    }
    """;

    [Fact]
    public void Parses_the_official_format()
    {
        var c = PatchConfiguration.Parse(Official);

        Assert.Equal("DigimonRPG.bin", c.LauncherInfo.RunExeFileName);
        Assert.Equal(242, c.PatchInfo.PatchVersion);
        Assert.Equal(7001, c.LoginServer!.Port);
        Assert.Equal("https://x/news.json", c.MainUrl.NewsPageUrl);
        Assert.False(c.ServerInfo.Maintenance);
    }

    [Fact]
    public void Lists_pending_patches_in_order()
    {
        var steps = PatchConfiguration.Parse(Official).PendingPatches(240)!;

        Assert.Equal([241, 242], steps.Select(s => s.Version));
        Assert.Equal("https://x/PatchFiles/Live/Patch_241.zip", steps[0].Url);
        Assert.Equal("Patch_242", steps[1].PackName);
        Assert.Empty(PatchConfiguration.Parse(Official).PendingPatches(323)!);
    }

    [Fact]
    public void Install_older_than_minimum_needs_reinstall()
    {
        var c = PatchConfiguration.Parse(Official.Replace("\"MinPatchVersion\": 0", "\"MinPatchVersion\": 100"));
        Assert.Null(c.PendingPatches(99));
    }

    [Theory]
    [InlineData("\"Port\": 7001", "\"Port\": 0")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", "ABC")]
    [InlineData("\"Patch_%d.zip\"", "\"Patch.zip\"")]
    [InlineData("\"MinPatchVersion\": 0", "\"MinPatchVersion\": 300")]
    public void Rejects_bad_values(string from, string to) =>
        Assert.Throws<PatchConfigurationException>(() => PatchConfiguration.Parse(Official.Replace(from, to)));

    [Fact]
    public void Missing_or_null_fields_get_defaults()
    {
        var c = PatchConfiguration.Parse("""{ "ServerInfo": null, "PatchInfo": { "PatchVersion": 3, "DownloadFileHeader": null } }""");

        Assert.Equal("Patch_%d.zip", c.PatchInfo.DownloadFileHeader);
        Assert.Equal("", c.ServerInfo.Message.Title);
        Assert.Equal("", c.LauncherInfo.RunExeFileName);
        Assert.Equal("", c.MainUrl.NewsPageUrl);
        Assert.Null(c.LoginServer);
    }

    [Fact]
    public void NewsFeed_parses_normalizes_and_sanitizes_urls()
    {
        var items = NewsFeed.Parse("""
        [
          { "Category": "event", "Title": "  Double EXP  ", "Date": " 11/26 ", "URL": "https://example.com/event" },
          { "Category": "UPDATE", "Title": "Patch 322", "Date": "11/25", "URL": "http://example.com/patch" },
          { "Category": "unknown", "Title": "Unsafe link", "Date": "", "URL": "javascript:alert(1)" },
          { "Category": "Info", "Title": "   ", "Date": "11/24", "URL": "https://example.com/skip" }
        ]
        """);

        Assert.Equal(3, items.Count);
        Assert.Equal(("Event", "Double EXP", "11/26", "https://example.com/event"),
            (items[0].Category, items[0].Title, items[0].Date, items[0].Url));
        Assert.Equal(("Update", "Patch 322", "11/25", "http://example.com/patch"),
            (items[1].Category, items[1].Title, items[1].Date, items[1].Url));
        Assert.Equal(("Info", "Unsafe link", "", ""),
            (items[2].Category, items[2].Title, items[2].Date, items[2].Url));
    }

    [Fact]
    public void Rejects_invalid_json() =>
        Assert.Throws<PatchConfigurationException>(() => PatchConfiguration.Parse("{ nope"));

    [Fact]
    public void Version_info_round_trips_as_utf16()
    {
        var path = Path.GetTempFileName();
        try
        {
            VersionInfo.Write(path, 323);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal([0xFF, 0xFE], bytes[..2]);
            Assert.Equal(323, VersionInfo.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
