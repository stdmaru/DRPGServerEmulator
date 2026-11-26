using DRPG.Launcher.Rendering;

namespace DRPG.Launcher.Tests;

public sealed class RememberedLoginTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("drpg-remember-").FullName;
    private string Ini => Path.Combine(_dir, "option.ini");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string GameOptions = "[SPRITE]\r\nalpha=1\r\n\r\n[Member]\r\nid=1\r\n[LANGUAGE]\r\nlanguage=0\r\n";

    [Fact]
    public void Set_keeps_the_game_sections_and_adds_ours()
    {
        File.WriteAllText(Ini, GameOptions);

        IniFile.Set(Ini, "DRPGLauncher", "Remember", "1");
        IniFile.Set(Ini, "DRPGLauncher", "Remember", "0");
        IniFile.Set(Ini, "SPRITE", "shadow", "1");

        var text = File.ReadAllText(Ini);
        Assert.StartsWith("[SPRITE]\r\nalpha=1\r\nshadow=1\r\n\r\n[Member]\r\nid=1\r\n", text);
        var values = IniFile.Read(Ini);
        Assert.Equal("0", values["drpglauncher.remember"]);
        Assert.Equal("1", values["member.id"]);
        Assert.Equal("0", values["language.language"]);
        Assert.Single(text.Split("Remember=").Skip(1));
    }

    [Fact]
    public void Set_refuses_multi_line_values()
    {
        Assert.Throws<ArgumentException>(() => IniFile.Set(Ini, "S", "k", "a\r\n[Evil]"));
        Assert.False(File.Exists(Ini));
    }

    [Fact]
    public void Checking_then_logging_in_remembers_the_login_and_unchecking_forgets_it()
    {
        File.WriteAllText(Ini, GameOptions);
        var state = RememberedLogin.Load(Ini);
        Assert.False(state.Remember);

        state.Toggle();
        state.LoggedIn("player1");
        var reloaded = RememberedLogin.Load(Ini);
        Assert.True(reloaded.Remember);
        Assert.Equal("player1", reloaded.Login);

        reloaded.Toggle();
        var cleared = RememberedLogin.Load(Ini);
        Assert.False(cleared.Remember);
        Assert.Equal("", cleared.Login);
        Assert.Equal("1", IniFile.Read(Ini)["member.id"]); // the official launcher's key is untouched
    }

    [Fact]
    public void Login_is_not_stored_while_unchecked()
    {
        var state = RememberedLogin.Load(Ini);
        state.LoggedIn("player1");
        Assert.False(File.Exists(Ini));
    }

    [Theory]
    [InlineData("a=b")]
    [InlineData("[x]")]
    [InlineData("a;b")]
    [InlineData("with space")]
    [InlineData("ção")]
    [InlineData("")]
    public void Hostile_logins_are_not_stored(string login)
    {
        var state = RememberedLogin.Load(Ini);
        state.Toggle();
        state.LoggedIn(login);
        Assert.Equal("", RememberedLogin.Load(Ini).Login);
    }

    [Fact]
    public void Hand_edited_hostile_login_is_ignored_on_load()
    {
        File.WriteAllText(Ini, $"[DRPGLauncher]\r\nRemember=1\r\nLogin={new string('a', 65)}\r\n");
        var state = RememberedLogin.Load(Ini);
        Assert.True(state.Remember);
        Assert.Equal("", state.Login);
    }

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
