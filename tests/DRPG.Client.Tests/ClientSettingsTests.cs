namespace DRPG.Client.Tests;

public sealed class ClientSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"drpg-client-{Guid.NewGuid():N}.ini");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void A_missing_file_means_the_local_emulator()
    {
        Assert.Equal(ClientSettings.Local, ClientSettings.Load(_path));
    }

    [Fact]
    public void Addresses_are_read_from_the_servers_section()
    {
        File.WriteAllText(_path, "[Servers]\nLogin=10.0.0.5:7101\nChannel=game.example.org:7100\n");

        var settings = ClientSettings.Load(_path);

        Assert.Equal(new ServerAddress("10.0.0.5", 7101), settings.Login);
        Assert.Equal(new ServerAddress("game.example.org", 7100), settings.Channel);
    }

    [Theory]
    [InlineData("Login=nonsense")]
    [InlineData("Login=host:0")]
    [InlineData("Login=host:70000")]
    [InlineData("Login=:7001")]
    [InlineData("Login=ho st:7001")]
    [InlineData("Login=host:abc")]
    public void A_malformed_address_is_an_error_not_a_default(string line)
    {
        File.WriteAllText(_path, $"[Servers]\n{line}\n");

        Assert.Throws<ClientSettingsException>(() => ClientSettings.Load(_path));
    }
}
