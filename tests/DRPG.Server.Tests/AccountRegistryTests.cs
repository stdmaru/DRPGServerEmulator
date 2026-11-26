using DRPG.Server.Game.Entities;
using DRPG.Server.Network;
using DRPG.Server.Network.Handlers;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPG.Server.Tests;

/// <summary>Ticket do channel para mapa e global (docs/emulador/seguranca.md#autenticação-entre-servidores).</summary>
public class AccountRegistryTests
{
    private static readonly AuthenticatedAccount Account = new(1, "tester", Authority.None);

    private static (AccountRegistry Registry, ManualTime Time) NewRegistry(int ticketSeconds = 60)
    {
        var time = new ManualTime(DateTimeOffset.UnixEpoch);
        return (new AccountRegistry(TestSupport.NetworkOptions(o => o.AccountTicketSeconds = ticketSeconds), time), time);
    }

    [Fact]
    public void Map_and_global_need_a_channel_login()
    {
        var (registry, _) = NewRegistry();
        var map = TestSupport.NewSession(ServerRole.Map);

        Assert.Equal(ClaimRejection.NoTicket, registry.Claim("tester", map).Rejection);
        Assert.Null(map.Account);
    }

    [Fact]
    public void Ticket_is_claimed_by_map_and_global_from_the_same_address()
    {
        var (registry, _) = NewRegistry();
        Assert.True(registry.SignIn(Account, TestSupport.NewSession(ServerRole.Channel)));
        var map = TestSupport.NewSession(ServerRole.Map);
        var global = TestSupport.NewSession(ServerRole.Global);

        Assert.Equal(Account, registry.Claim("TESTER", map).Account);
        Assert.Equal(Account, registry.Claim("tester", global).Account);
        Assert.Equal(Account, map.Account);
        Assert.Equal(3, registry.SessionsOf("tester").Count);
    }

    [Fact]
    public void Claim_from_another_address_is_refused()
    {
        var (registry, _) = NewRegistry();
        registry.SignIn(Account, TestSupport.NewSession(ServerRole.Channel, "10.0.0.1"));
        var map = TestSupport.NewSession(ServerRole.Map, "10.0.0.2");

        Assert.Equal(ClaimRejection.WrongAddress, registry.Claim("tester", map).Rejection);
        Assert.Null(map.Account);
        // o ticket continua valendo para o dono
        Assert.True(registry.Claim("tester", TestSupport.NewSession(ServerRole.Map, "10.0.0.1")).Accepted);
    }

    [Fact]
    public void Ipv4_mapped_address_matches_plain_ipv4()
    {
        var (registry, _) = NewRegistry();
        registry.SignIn(Account, TestSupport.NewSession(ServerRole.Channel, "::ffff:10.0.0.1"));

        Assert.True(registry.Claim("tester", TestSupport.NewSession(ServerRole.Map, "10.0.0.1")).Accepted);
    }

    [Fact]
    public void Ticket_is_used_once_per_server()
    {
        var (registry, _) = NewRegistry();
        registry.SignIn(Account, TestSupport.NewSession(ServerRole.Channel));
        var first = TestSupport.NewSession(ServerRole.Map);
        Assert.True(registry.Claim("tester", first).Accepted);

        // segunda conexão enquanto a primeira está aberta
        Assert.Equal(ClaimRejection.AlreadyUsed, registry.Claim("tester", TestSupport.NewSession(ServerRole.Map)).Rejection);

        // e depois que a primeira fecha
        registry.Release(first);
        Assert.Equal(ClaimRejection.AlreadyUsed, registry.Claim("tester", TestSupport.NewSession(ServerRole.Map)).Rejection);

        // o global tem a vaga dele
        Assert.True(registry.Claim("tester", TestSupport.NewSession(ServerRole.Global)).Accepted);
    }

    [Fact]
    public void New_channel_login_issues_a_fresh_ticket()
    {
        var (registry, _) = NewRegistry();
        var firstChannel = TestSupport.NewSession(ServerRole.Channel);
        registry.SignIn(Account, firstChannel);
        var firstMap = TestSupport.NewSession(ServerRole.Map);
        registry.Claim("tester", firstMap);

        var secondChannel = TestSupport.NewSession(ServerRole.Channel);
        registry.SignIn(Account, secondChannel);

        Assert.True(firstChannel.IsClosed);
        Assert.True(firstMap.IsClosed);
        Assert.False(secondChannel.IsClosed);
        Assert.True(registry.Claim("tester", TestSupport.NewSession(ServerRole.Map)).Accepted);

        // a saída das sessões antigas não mexe no ticket novo
        registry.Release(firstChannel);
        registry.Release(firstMap);
        Assert.Equal(2, registry.SessionsOf("tester").Count);
    }

    [Fact]
    public void Ticket_expires_after_the_last_session_closes()
    {
        var (registry, time) = NewRegistry(ticketSeconds: 60);
        var channel = TestSupport.NewSession(ServerRole.Channel);
        registry.SignIn(Account, channel);

        // aberto, o channel segura o ticket
        time.Now += TimeSpan.FromHours(1);
        registry.Release(channel);                     // o channel cai antes de o mapa conectar
        time.Now += TimeSpan.FromSeconds(30);
        Assert.True(registry.Claim("tester", TestSupport.NewSession(ServerRole.Map)).Accepted);

        var (other, otherTime) = NewRegistry(ticketSeconds: 60);
        var otherChannel = TestSupport.NewSession(ServerRole.Channel);
        other.SignIn(Account, otherChannel);
        other.Release(otherChannel);
        otherTime.Now += TimeSpan.FromSeconds(61);
        Assert.Equal(ClaimRejection.Expired, other.Claim("tester", TestSupport.NewSession(ServerRole.Map)).Rejection);
    }

    [Fact]
    public void Only_channel_signs_in_and_only_map_and_global_claim()
    {
        var (registry, _) = NewRegistry();
        Assert.False(registry.SignIn(Account, TestSupport.NewSession(ServerRole.Map)));
        registry.SignIn(Account, TestSupport.NewSession(ServerRole.Channel));
        Assert.Equal(ClaimRejection.WrongRole, registry.Claim("tester", TestSupport.NewSession(ServerRole.Channel)).Rejection);
    }

    [Fact]
    public async Task Global_auth_without_a_valid_ticket_closes_the_session()
    {
        var (registry, _) = NewRegistry();
        registry.SignIn(Account, TestSupport.NewSession(ServerRole.Channel, "10.0.0.1"));
        var handler = new GlobalUserAuthHandler(registry, NullLogger<GlobalUserAuthHandler>.Instance);

        var stranger = TestSupport.NewSession(ServerRole.Global, "10.0.0.9");
        await handler.DispatchAsync(stranger, AuthPayload("tester"));
        Assert.True(stranger.IsClosed);

        var owner = TestSupport.NewSession(ServerRole.Global, "10.0.0.1");
        await handler.DispatchAsync(owner, AuthPayload("tester"));
        Assert.False(owner.IsClosed);
        Assert.Equal(Account, owner.Account);

        // repetir o 0x64 na mesma conexão não usa o ticket de novo
        owner.DrainOutgoing();
        await handler.DispatchAsync(owner, AuthPayload("tester"));
        Assert.False(owner.IsClosed);
        Assert.NotEmpty(owner.DrainOutgoing());
    }

    private static byte[] AuthPayload(string username)
    {
        var payload = new byte[114];                    // usuário em 21 bytes, o resto zerado
        System.Text.Encoding.ASCII.GetBytes(username).CopyTo(payload, 0);
        return payload;
    }
}
