using DRPGServer.Configuration;
using DRPGServer.Game.World;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

/// <summary>Validação das opções na partida (N6).</summary>
public class OptionsValidationTests
{
    [Fact]
    public void Defaults_are_valid()
    {
        Assert.True(new ServersOptionsValidator().Validate(null, new ServersOptions()).Succeeded);
        Assert.True(new DatabaseOptionsValidator().Validate(null, new DatabaseOptions()).Succeeded);
        Assert.True(new GameOptionsValidator().Validate(null, new GameOptions()).Succeeded);
    }

    [Fact]
    public void Shipped_appsettings_is_valid()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .Build();
        var servers = config.GetSection(ServersOptions.Section).Get<ServersOptions>() ?? new();
        var database = config.GetSection(DatabaseOptions.Section).Get<DatabaseOptions>() ?? new();
        var game = config.GetSection(GameOptions.Section).Get<GameOptions>() ?? new();

        Assert.Empty(new ServersOptionsValidator().Validate(null, servers).Failures ?? []);
        Assert.Empty(new DatabaseOptionsValidator().Validate(null, database).Failures ?? []);
        Assert.Empty(new GameOptionsValidator().Validate(null, game).Failures ?? []);
    }

    [Fact]
    public void Servers_reject_bad_port_ip_and_duplicates()
    {
        var o = new ServersOptions();
        o.Map.Port = o.Channel.Port;
        o.Login.Port = 0;
        o.Global.BindIP = "localhost";
        var failures = new ServersOptionsValidator().Validate(null, o).Failures!.ToList();

        Assert.Contains(failures, f => f.Contains("Servers:Login:Port"));
        Assert.Contains(failures, f => f.Contains("Servers:Global:BindIP"));
        Assert.Contains(failures, f => f.Contains("portas diferentes"));
    }

    [Theory]
    [InlineData("drpg", true)]
    [InlineData("drpg_test2", true)]
    [InlineData("", false)]
    [InlineData("drpg`; DROP DATABASE x; --", false)]
    [InlineData("drpg-prod", false)]
    public void Database_name_is_safe_for_create_database(string name, bool valid)
    {
        var result = new DatabaseOptionsValidator().Validate(null, new DatabaseOptions { Name = name });
        Assert.Equal(valid, result.Succeeded);
    }

    [Fact]
    public void Game_rejects_out_of_range_values_and_lists_them_all()
    {
        var o = new GameOptions
        {
            CaptureChance = 1.5,
            ExpRate = double.NaN,
            MaxStackPerItem = 0,
            AutosaveSeconds = 0,
            FourthStarterDigimon = 9,
            DefaultDepositorySlots = 255,
        };
        var failures = new GameOptionsValidator().Validate(null, o).Failures!.ToList();

        Assert.Equal(6, failures.Count);
        foreach (var field in new[] { "CaptureChance", "ExpRate", "MaxStackPerItem", "AutosaveSeconds", "FourthStarterDigimon", "DefaultDepositorySlots" })
            Assert.Contains(failures, f => f.Contains($"Game:{field}"));
    }

    [Fact]
    public void Player_system_rejects_unknown_start_map()
    {
        var world = TestSupport.NewWorld();
        var zones = new ZoneSystem(TestSupport.Data, world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
        var battles = TestSupport.NewBattles(world);
        var options = TestSupport.GameOptions(o => o.StartMapId = 250);

        var e = Assert.Throws<InvalidOperationException>(() => new PlayerSystem(world, zones, battles,
            new Data.Persistence.PersistenceQueue(), new RecordingSnapshotStore(), options, NullLogger<PlayerSystem>.Instance));
        Assert.Contains("StartMapId", e.Message);
    }
}
