using System.Net;
using DRPG.Server.Configuration;
using DRPG.Server.Network;
using DRPG.Server.Network.Traffic;
using DRPG.Shared.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DRPG.Server.Tests;

/// <summary>Limites de recurso da rede (S6): baldes de pacote, prazos, fila do mundo e conexões por IP.</summary>
public class TrafficTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private static (SessionTraffic Traffic, ManualTime Time) NewTraffic(ServerRole role, Action<NetworkOptions>? configure = null)
    {
        var time = new ManualTime(Start);
        return (TestSupport.Traffic(configure, time).Start(role), time);
    }

    [Fact]
    public void Bucket_allows_the_burst_then_refills_at_the_rate()
    {
        var time = new ManualTime(Start);
        var bucket = new TokenBucket(new RateLimitOptions { PerSecond = 2, Burst = 3 }, time);

        Assert.True(bucket.TryTake());
        Assert.True(bucket.TryTake());
        Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());

        time.Now += TimeSpan.FromMilliseconds(500);     // 1 ficha
        Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());

        time.Now += TimeSpan.FromHours(1);               // não passa do burst
        for (var i = 0; i < 3; i++) Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());
    }

    [Fact]
    public void Packets_above_the_session_ceiling_close_the_session()
    {
        var (traffic, _) = NewTraffic(ServerRole.Map, o => o.Packets = new() { PerSecond = 10, Burst = 5 });

        for (var i = 0; i < 5; i++)
            Assert.Equal(TrafficVerdict.Accept, traffic.Admit((ushort)MapOpcode.KeepAlive));
        Assert.Equal(TrafficVerdict.Close, traffic.Admit((ushort)MapOpcode.KeepAlive));
    }

    [Fact]
    public void Official_peak_fits_the_default_ceiling()
    {
        // ✅ pico medido nas capturas: 7 pacotes no mesmo segundo
        var (traffic, time) = NewTraffic(ServerRole.Map);
        for (var second = 0; second < 120; second++)
        {
            for (var i = 0; i < 7; i++)
                Assert.Equal(TrafficVerdict.Accept, traffic.Admit((ushort)MapOpcode.BattleActionReq));
            time.Now += TimeSpan.FromSeconds(1);
        }
    }

    [Fact]
    public void Chat_flood_is_dropped_without_closing()
    {
        var (traffic, time) = NewTraffic(ServerRole.Map);
        var chat = (ushort)MapOpcode.ChatNormal;

        for (var i = 0; i < 5; i++) Assert.Equal(TrafficVerdict.Accept, traffic.Admit(chat));
        Assert.Equal(TrafficVerdict.Drop, traffic.Admit(chat));
        Assert.Equal(TrafficVerdict.Accept, traffic.Admit((ushort)MapOpcode.MoveCharacter));   // outro opcode segue

        time.Now += TimeSpan.FromSeconds(1);
        Assert.Equal(TrafficVerdict.Accept, traffic.Admit(chat));
    }

    [Fact]
    public void Opcode_limits_are_per_server()
    {
        // 0x21 no global não é o chat do mapa
        var (traffic, _) = NewTraffic(ServerRole.Global);
        for (var i = 0; i < 20; i++)
            Assert.Equal(TrafficVerdict.Accept, traffic.Admit((ushort)MapOpcode.ChatNormal));
    }

    [Fact]
    public void Unauthenticated_session_expires_after_the_auth_deadline()
    {
        var (traffic, time) = NewTraffic(ServerRole.Channel);

        time.Now += TimeSpan.FromSeconds(29);
        traffic.Admit((ushort)ChannelOpcode.ChannelSelect);
        Assert.Null(traffic.ExpiredReason(authenticated: false));

        time.Now += TimeSpan.FromSeconds(2);
        traffic.Admit((ushort)ChannelOpcode.KeepAlive);       // mandar pacote não adia o prazo de autenticar
        Assert.NotNull(traffic.ExpiredReason(authenticated: false));
        Assert.Null(traffic.ExpiredReason(authenticated: true));
    }

    [Fact]
    public void Map_idle_timeout_is_shorter_than_channel()
    {
        var (map, mapTime) = NewTraffic(ServerRole.Map);
        var (channel, channelTime) = NewTraffic(ServerRole.Channel);

        mapTime.Now += TimeSpan.FromSeconds(61);
        channelTime.Now += TimeSpan.FromSeconds(61);
        Assert.NotNull(map.ExpiredReason(authenticated: true));
        Assert.Null(channel.ExpiredReason(authenticated: true));

        channelTime.Now += TimeSpan.FromSeconds(540);
        Assert.NotNull(channel.ExpiredReason(authenticated: true));
    }

    [Fact]
    public void Keepalive_cadence_keeps_the_session_alive()
    {
        // ✅ mapa: 0xC4 a cada ~5 s; channel/global: a cada ~167 s
        var (map, mapTime) = NewTraffic(ServerRole.Map);
        var (global, globalTime) = NewTraffic(ServerRole.Global);
        for (var i = 0; i < 20; i++)
        {
            mapTime.Now += TimeSpan.FromSeconds(6.6);
            map.Admit((ushort)MapOpcode.KeepAlive);
            Assert.Null(map.ExpiredReason(authenticated: true));

            globalTime.Now += TimeSpan.FromSeconds(168);
            global.Admit((ushort)GlobalOpcode.KeepAlive);
            Assert.Null(global.ExpiredReason(authenticated: true));
        }
    }

    [Fact]
    public void World_work_is_bounded_per_session()
    {
        var (traffic, _) = NewTraffic(ServerRole.Map, o => o.MaxPendingWorldWork = 8);

        for (var i = 0; i < 8; i++) Assert.True(traffic.TryBeginWorldWork());
        Assert.False(traffic.TryBeginWorldWork());
        Assert.Equal(8, traffic.PendingWorldWork);

        traffic.EndWorldWork();
        Assert.True(traffic.TryBeginWorldWork());
    }

    [Fact]
    public void Connections_are_limited_per_address_and_in_total()
    {
        var limiter = new ConnectionLimiter(TestSupport.NetworkOptions(o =>
        {
            o.MaxConnections = 3;
            o.MaxConnectionsPerIp = 2;
        }));
        var a = IPAddress.Parse("10.0.0.1");
        var b = IPAddress.Parse("10.0.0.2");
        var c = IPAddress.Parse("10.0.0.3");

        Assert.Equal(ConnectionRejection.None, limiter.TryAcquire(a));
        Assert.Equal(ConnectionRejection.None, limiter.TryAcquire(a));
        Assert.Equal(ConnectionRejection.TooManyFromAddress, limiter.TryAcquire(a));
        Assert.Equal(ConnectionRejection.None, limiter.TryAcquire(b));
        Assert.Equal(ConnectionRejection.TooManyTotal, limiter.TryAcquire(c));

        limiter.Release(a);
        Assert.Equal(2, limiter.Total);
        Assert.Equal(ConnectionRejection.None, limiter.TryAcquire(c));
        Assert.Equal(ConnectionRejection.TooManyTotal, limiter.TryAcquire(a));
    }

    [Fact]
    public void Release_of_unknown_address_does_not_corrupt_the_count()
    {
        var limiter = new ConnectionLimiter(TestSupport.NetworkOptions());
        limiter.Release(IPAddress.Loopback);
        Assert.Equal(0, limiter.Total);
    }

    [Fact]
    public void Network_defaults_are_valid()
    {
        Assert.Empty(new NetworkOptionsValidator().Validate(null, new NetworkOptions()).Failures ?? []);
    }

    [Fact]
    public void Opcode_limits_reject_unknown_names_and_numbers()
    {
        var o = new NetworkOptions();
        o.OpcodeLimits[ServerRole.Map]["NoSuchOpcode"] = new() { PerSecond = 1, Burst = 1 };
        o.OpcodeLimits[ServerRole.Map]["33"] = new() { PerSecond = 1, Burst = 1 };
        o.OpcodeLimits[ServerRole.Global] = new() { ["ChatNormal"] = new() { PerSecond = 1, Burst = 1 } };   // é do mapa
        o.Packets = new() { PerSecond = 0, Burst = 0 };

        var failures = new NetworkOptionsValidator().Validate(null, o).Failures?.ToList() ?? [];
        Assert.Contains(failures, f => f.Contains("NoSuchOpcode"));
        Assert.Contains(failures, f => f.Contains(":33:"));
        Assert.Contains(failures, f => f.Contains("Global:ChatNormal"));
        Assert.Contains(failures, f => f.Contains("Packets:PerSecond"));
        Assert.Contains(failures, f => f.Contains("Packets:Burst"));
    }

    [Fact]
    public void Idle_timeouts_cannot_undercut_the_keepalive()
    {
        var o = new NetworkOptions { IdleTimeoutSeconds = 120, MapIdleTimeoutSeconds = 5 };
        var failures = new NetworkOptionsValidator().Validate(null, o).Failures?.ToList() ?? [];
        Assert.Contains(failures, f => f.Contains("IdleTimeoutSeconds"));
        Assert.Contains(failures, f => f.Contains("MapIdleTimeoutSeconds"));
    }

    [Fact]
    public void Shipped_appsettings_network_section_binds_and_is_valid()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .Build();
        var network = config.GetSection(NetworkOptions.Section).Get<NetworkOptions>() ?? new();

        Assert.Empty(new NetworkOptionsValidator().Validate(null, network).Failures ?? []);
        Assert.Equal(1, network.OpcodeLimits[ServerRole.Map]["ChatNormal"].PerSecond);
        _ = new TrafficLimits(Options.Create(network), TimeProvider.System).Start(ServerRole.Map);
    }

    [Fact]
    public void Configuration_overrides_one_opcode_and_keeps_the_other_defaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Network:OpcodeLimits:Map:ChatNormal:PerSecond"] = "3",
                ["Network:OpcodeLimits:Map:ChatNormal:Burst"] = "9",
            })
            .Build();
        var network = config.GetSection(NetworkOptions.Section).Get<NetworkOptions>() ?? new();

        Assert.Equal(3, network.OpcodeLimits[ServerRole.Map]["ChatNormal"].PerSecond);
        Assert.Equal(10, network.OpcodeLimits[ServerRole.Map]["MoveCharacter"].PerSecond);
    }
}
