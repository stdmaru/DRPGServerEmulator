namespace DRPG.Launcher.Tests;

public sealed class GameStarterTests : IDisposable
{
    private readonly string _install = Directory.CreateTempSubdirectory("drpg-start-").FullName;
    private const string Key = "3849E5DC7F1205975B1EEF92A753B97E718D653F";

    public GameStarterTests() => File.WriteAllText(Path.Combine(_install, "DigimonRPG.bin"), "");

    public void Dispose() => Directory.Delete(_install, recursive: true);

    [Fact]
    public void Direct_start_passes_username_and_key()
    {
        var info = GameStarter.Build(_install, "DigimonRPG.bin", null, "player1", Key);
        Assert.Equal(Path.Combine(_install, "DigimonRPG.bin"), info.FileName);
        Assert.Equal(["player1", Key], info.ArgumentList);
        Assert.Equal(_install, info.WorkingDirectory);
        Assert.False(info.UseShellExecute);
    }

    [Fact]
    public void Runner_gets_the_executable_first()
    {
        var info = GameStarter.Build(_install, "DigimonRPG.bin", "wine", "player1", Key);
        Assert.Equal("wine", info.FileName);
        Assert.Equal([Path.Combine(_install, "DigimonRPG.bin"), "player1", Key], info.ArgumentList);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../DigimonRPG.bin")]
    [InlineData("/bin/sh")]
    [InlineData("sub\\x.exe")]
    [InlineData("resource.pf")]
    [InlineData("missing.bin")]
    public void Bad_executable_names_are_refused(string name) =>
        Assert.Throws<GameStartException>(() => GameStarter.Build(_install, name, null, "player1", Key));

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("abcdefghijklmnopqrstu")] // 21: longer than the game's username[21] field allows
    [InlineData("nul\0")]
    public void Bad_usernames_are_refused(string user) =>
        Assert.Throws<GameStartException>(() => GameStarter.Build(_install, "DigimonRPG.bin", null, user, Key));

    [Theory]
    [InlineData("")]
    [InlineData("3849E5DC7F1205975B1EEF92A753B97E718D653")]
    [InlineData("3849E5DC7F1205975B1EEF92A753B97E718D653FF")]
    [InlineData("3849E5DC7F1205975B1EEF92A753B97E718D653G")]
    [InlineData("3849E5DC7F1205975B1EEF92A753B97E718D65 F")]
    public void Bad_keys_are_refused(string key) =>
        Assert.Throws<GameStartException>(() => GameStarter.Build(_install, "DigimonRPG.bin", null, "player1", key));
}
