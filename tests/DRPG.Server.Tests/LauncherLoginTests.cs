using System.Security.Cryptography;
using DRPG.Shared.Protocol;
using DRPG.Shared.Protocol.Launcher;
using DRPG.Server.Configuration;
using DRPG.Server.Data.Repositories;
using DRPG.Server.Game.Accounts;
using DRPG.Server.Game.Entities;
using DRPG.Server.Network;
using DRPG.Server.Network.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DRPG.Server.Tests;

/// <summary>Launcher login on the LoginServer (docs/protocolo/launcher.md).</summary>
public sealed class LauncherLoginTests : IDisposable
{
    private sealed class FakeAccounts : ILauncherAccountStore
    {
        public Dictionary<string, (uint Uid, string? Email, string? Password)> Rows { get; } = [];
        public Dictionary<uint, (string Hash, long ExpiresAt)> Issued { get; } = [];

        public Task<LauncherAccountRecord?> FindByLoginAsync(string login, CancellationToken ct)
        {
            var match = Rows.FirstOrDefault(kv => string.Equals(kv.Key, login, StringComparison.OrdinalIgnoreCase)
                || string.Equals(kv.Value.Email, login, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(match.Key is null ? null
                : new LauncherAccountRecord(new AuthenticatedAccount(match.Value.Uid, match.Key, Authority.None), match.Value.Password));
        }

        public Task<bool> StorePasswordHashAsync(uint accountUid, string previous, string hash, CancellationToken ct)
        {
            var (name, row) = Rows.Single(kv => kv.Value.Uid == accountUid);
            if (row.Password != previous) return Task.FromResult(false);
            Rows[name] = row with { Password = hash };
            return Task.FromResult(true);
        }

        public Task IssueAuthKeyAsync(uint accountUid, string keyHash, long expiresAt, CancellationToken ct)
        {
            Issued[accountUid] = (keyHash, expiresAt);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<(uint Uid, string PasswordHash)>> PlainPasswordsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<(uint, string)>>(Rows.Values
                .Where(r => r.Password?.StartsWith(LauncherLoginService.PlainPrefix) == true)
                .Select(r => (r.Uid, r.Password!)).ToList());
    }

    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), $"drpg-launcher-{Guid.NewGuid():N}.pem");
    private readonly FakeAccounts _accounts = new();
    private readonly LauncherLoginOptions _options = new();

    public LauncherLoginTests()
    {
        _options.KeyPath = _keyPath;
        _accounts.Rows["tamer"] = (1, "tamer@example.com", PasswordHash.Hash("right"));
        _accounts.Rows["legacy"] = (2, null, LauncherLoginService.PlainPrefix + "old");
    }

    public void Dispose() => File.Delete(_keyPath);

    private LauncherLoginService Service() =>
        new(_accounts, new LoginAttemptLimiter(Options.Create(_options), TimeProvider.System), Options.Create(_options),
            TestSupport.Clock, NullLogger<LauncherLoginService>.Instance);

    private static LauncherCredentials Credentials(string login, string password) =>
        new(new byte[32], new byte[32], 1, login, password);

    [Fact]
    public async Task Right_password_issues_a_40_hex_key_valid_for_5_hours()
    {
        var result = await Service().LoginAsync(Credentials("tamer@example.com", "right"), "10.0.0.1", default);

        Assert.Equal(LauncherLoginStatus.Ok, result.Status);
        Assert.Equal("tamer", result.GameUsername);
        Assert.Matches("^[0-9A-F]{40}$", result.GameKey);
        Assert.Equal(5u * 3600, result.ValidSeconds);
        var (hash, expiresAt) = _accounts.Issued[1];
        Assert.True(AuthKeyHash.Verify(result.GameKey, hash));
        Assert.InRange(expiresAt - TestSupport.Clock.UnixNow, 5 * 3600 - 5, 5 * 3600);
    }

    [Fact]
    public async Task Plain_password_from_migration_works_once_and_is_converted()
    {
        var result = await Service().LoginAsync(Credentials("legacy", "old"), "10.0.0.1", default);

        Assert.Equal(LauncherLoginStatus.Ok, result.Status);
        var stored = _accounts.Rows["legacy"].Password!;
        Assert.StartsWith(Pbkdf2Hash.Scheme, stored);
        Assert.True(PasswordHash.Verify("old", stored));
    }

    [Fact]
    public async Task Startup_conversion_hashes_every_plain_password()
    {
        Assert.Equal(1, await Service().ConvertPlainPasswordsAsync(default));
        Assert.True(PasswordHash.Verify("old", _accounts.Rows["legacy"].Password!));
    }

    [Theory]
    [InlineData("tamer", "wrong")]
    [InlineData("nobody", "right")]
    [InlineData("legacy", "OLD")]
    [InlineData("", "right")]
    public async Task Wrong_password_and_unknown_login_get_the_same_answer(string login, string password)
    {
        var result = await Service().LoginAsync(Credentials(login, password), "10.0.0.1", default);

        Assert.Equal(LauncherLoginResult.Failure(LauncherLoginStatus.InvalidCredentials), result);
        Assert.Empty(_accounts.Issued);
    }

    [Fact]
    public async Task Attempts_past_the_burst_are_refused()
    {
        _options.AttemptsPerLogin = new RateLimitOptions { PerSecond = 0.001, Burst = 2 };
        var service = Service();

        await service.LoginAsync(Credentials("tamer", "wrong"), "10.0.0.1", default);
        await service.LoginAsync(Credentials("tamer", "wrong"), "10.0.0.2", default);
        var third = await service.LoginAsync(Credentials("tamer", "right"), "10.0.0.3", default);

        Assert.Equal(LauncherLoginStatus.TooManyAttempts, third.Status);
    }

    [Fact]
    public async Task Outdated_launcher_is_refused_before_checking_the_password()
    {
        _options.MinLauncherVersion = 5;
        var result = await Service().LoginAsync(Credentials("tamer", "right"), "10.0.0.1", default);
        Assert.Equal(LauncherLoginStatus.OutdatedLauncher, result.Status);
    }

    [Fact]
    public async Task Hello_then_sealed_login_returns_an_encrypted_key_and_the_nonce_is_single_use()
    {
        var key = new LauncherServerKey(Options.Create(_options), NullLogger<LauncherServerKey>.Instance);
        var session = TestSupport.NewSession(ServerRole.Login);

        await new LauncherHelloHandler(key, Options.Create(_options))
            .HandleAsync(session, TwoWayPacket.EncodePayload(new LauncherHello(LauncherLimits.ProtocolVersion, 1)), default);
        var hello = ClientPacket.Parse<LauncherHelloReply>(Payload(session.DrainOutgoing().Single()));
        Assert.Equal(LauncherHelloStatus.Ok, hello.Status);
        Assert.True(LauncherCrypto.MatchesFingerprint(hello.PublicKey, key.Fingerprint));

        var responseKey = RandomNumberGenerator.GetBytes(LauncherCrypto.KeyBytes);
        var credentials = new LauncherCredentials(hello.Nonce, responseKey, 1, "tamer", "right");
        var login = TwoWayPacket.EncodePayload(LauncherCrypto.Seal(hello.PublicKey, hello.Nonce, credentials.Encode()));
        var handler = new LauncherLoginHandler(key, Service());
        await handler.HandleAsync(session, login, default);

        var reply = ClientPacket.Parse<LauncherLoginReply>(Payload(session.DrainOutgoing().Single()));
        var result = LauncherLoginResult.Decode(LauncherCrypto.OpenReply(responseKey, reply));
        Assert.Equal(LauncherLoginStatus.Ok, result.Status);
        Assert.False(session.IsClosed);

        await handler.HandleAsync(session, login, default);   // replay on the same connection
        Assert.True(session.IsClosed);
    }

    [Fact]
    public async Task Login_body_that_does_not_decrypt_closes_the_connection()
    {
        var key = new LauncherServerKey(Options.Create(_options), NullLogger<LauncherServerKey>.Instance);
        using var impostor = LauncherCrypto.CreateKey();
        var session = TestSupport.NewSession(ServerRole.Login);
        await new LauncherHelloHandler(key, Options.Create(_options))
            .HandleAsync(session, TwoWayPacket.EncodePayload(new LauncherHello(LauncherLimits.ProtocolVersion, 1)), default);
        var hello = ClientPacket.Parse<LauncherHelloReply>(Payload(session.DrainOutgoing().Single()));

        var sealedForImpostor = LauncherCrypto.Seal(impostor.PublicKey.ExportSubjectPublicKeyInfo(), hello.Nonce,
            new LauncherCredentials(hello.Nonce, new byte[32], 1, "tamer", "right").Encode());
        await new LauncherLoginHandler(key, Service()).HandleAsync(session, TwoWayPacket.EncodePayload(sealedForImpostor), default);

        Assert.True(session.IsClosed);
        Assert.Empty(_accounts.Issued);
    }

    [Fact]
    public async Task Channel_refuses_an_expired_game_key()
    {
        var expired = new AccountRecord(new AuthenticatedAccount(1, "tamer", Authority.None), null, AuthKeyHash.Hash("K"),
            TestSupport.Clock.UnixNow - 1);
        var valid = expired with { AuthKeyExpiresAt = TestSupport.Clock.UnixNow + 60 };

        Assert.Null(await Authenticator(expired).AuthenticateAsync("tamer", "K", default));
        Assert.NotNull(await Authenticator(valid).AuthenticateAsync("tamer", "K", default));
    }

    private static AccountAuthenticator Authenticator(AccountRecord record) =>
        new(new SingleAccount(record), TestSupport.Clock, NullLogger<AccountAuthenticator>.Instance);

    private sealed class SingleAccount(AccountRecord record) : IAccountStore
    {
        public Task<AccountRecord?> FindByUsernameAsync(string username, CancellationToken ct) =>
            Task.FromResult<AccountRecord?>(username == record.Account.Username ? record : null);

        public Task<bool> StoreAuthKeyHashAsync(uint accountUid, string plainKey, string hash, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<(uint Uid, string AuthKey)>> PlainAuthKeysAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<(uint, string)>>([]);
    }

    private static byte[] Payload(byte[] frame) => frame[PacketHeader.Length..];
}
