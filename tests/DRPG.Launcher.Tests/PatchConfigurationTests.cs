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
      "MainUrl": { "HomePageUrl": "http://a", "MainPageUrl": "http://b" },
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
        Assert.Null(c.LoginServer);
        Assert.Empty(c.News);
    }

    [Fact]
    public void Normalizes_news_categories_and_skips_blank_titles()
    {
        var c = PatchConfiguration.Parse("""
        {
          "PatchInfo": { "PatchVersion": 1 },
          "News": [
            { "Category": "event", "Title": "  Double EXP  " },
            { "Category": "UPDATE", "Title": "Patch 322" },
            { "Category": "unknown", "Title": "General notice" },
            { "Category": "Info", "Title": "   " }
          ]
        }
        """);

        Assert.Equal(3, c.News.Count);
        Assert.Equal(("Event", "Double EXP"), (c.News[0].Category, c.News[0].Title));
        Assert.Equal(("Update", "Patch 322"), (c.News[1].Category, c.News[1].Title));
        Assert.Equal(("Info", "General notice"), (c.News[2].Category, c.News[2].Title));
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
