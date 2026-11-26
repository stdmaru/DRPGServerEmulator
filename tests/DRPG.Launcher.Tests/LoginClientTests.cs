using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using DRPG.Launcher.Login;
using DRPG.Shared.Protocol;
using DRPG.Shared.Protocol.Launcher;

namespace DRPG.Launcher.Tests;

public sealed class LoginClientTests : IAsyncDisposable
{
    private readonly ECDiffieHellman _key = LauncherCrypto.CreateKey();
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private int _connections;

    public LoginClientTests() => _listener.Start();

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        _key.Dispose();
    }

    private string Fingerprint => LauncherCrypto.Fingerprint(_key.PublicKey.ExportSubjectPublicKeyInfo());

    private LoginClient Client(string? fingerprint) => new(new LoginClient.Options(
        LoginClient.Tcp("127.0.0.1", ((IPEndPoint)_listener.LocalEndpoint).Port), 1, fingerprint, TimeSpan.FromSeconds(5)));

    /// <summary>Minimal LoginServer: hello, then one login per connection (or closes right after hello).</summary>
    private Task Serve(bool dropFirstAfterHello = false, LauncherHelloStatus status = LauncherHelloStatus.Ok) => Task.Run(async () =>
    {
        while (!_stop.IsCancellationRequested)
        {
            using var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
            var stream = socket.GetStream();
            int n = Interlocked.Increment(ref _connections);
            await Read<LauncherHello>(stream);
            var nonce = RandomNumberGenerator.GetBytes(LauncherLimits.NonceBytes);
            await Write(stream, new LauncherHelloReply(LauncherLimits.ProtocolVersion, status, nonce, _key.PublicKey.ExportSubjectPublicKeyInfo()));
            if (dropFirstAfterHello && n == 1)
                continue;
            var request = await Read<LauncherLogin>(stream);
            var credentials = LauncherCredentials.Decode(LauncherCrypto.Open(_key, nonce, request));
            var result = credentials.Password == "right"
                ? new LauncherLoginResult(LauncherLoginStatus.Ok, "tamer", new string('A', 40), 18000)
                : LauncherLoginResult.Failure(LauncherLoginStatus.InvalidCredentials);
            await Write(stream, LauncherCrypto.SealReply(credentials.ResponseKey, result.Encode()));
        }
    });

    private static async Task<T> Read<T>(NetworkStream stream) where T : ITwoWayPacket<T>
    {
        var buffer = new byte[PacketHeader.MaxPacketSize];
        int have = 0;
        while (true)
        {
            var seq = new ReadOnlySequence<byte>(buffer, 0, have);
            if (PacketFramer.TryRead(ref seq, out var frame, out _) == FrameStatus.Complete)
            {
                Assert.Equal(T.Opcode, frame!.Opcode);
                return ClientPacket.Parse<T>(frame.Payload);
            }
            int n = await stream.ReadAsync(buffer.AsMemory(have));
            if (n == 0) throw new IOException("closed");
            have += n;
        }
    }

    private static async Task Write<T>(NetworkStream stream, T packet) where T : ITwoWayPacket<T> =>
        await stream.WriteAsync(TwoWayPacket.Encode(packet));

    [Fact]
    public async Task Handshake_and_login_return_the_game_key()
    {
        _ = Serve();
        await using var client = Client(Fingerprint);

        Assert.Equal(HandshakeResult.Ready, await client.HandshakeAsync(default));
        var result = await client.LoginAsync("tamer@example.com", "right", default);

        Assert.Equal(LauncherLoginStatus.Ok, result!.Status);
        Assert.Equal("tamer", result.GameUsername);
        Assert.Equal(40, result.GameKey.Length);
    }

    [Fact]
    public async Task Wrong_password_is_reported_and_a_retry_uses_a_new_handshake()
    {
        _ = Serve();
        await using var client = Client(Fingerprint);
        await client.HandshakeAsync(default);

        Assert.Equal(LauncherLoginStatus.InvalidCredentials, (await client.LoginAsync("tamer", "wrong", default))!.Status);
        Assert.Equal(LauncherLoginStatus.Ok, (await client.LoginAsync("tamer", "right", default))!.Status);
        Assert.Equal(2, _connections);
    }

    [Fact]
    public async Task Different_key_is_refused()
    {
        _ = Serve();
        await using var client = Client(new string('0', 64));

        Assert.Equal(HandshakeResult.FingerprintMismatch, await client.HandshakeAsync(default));
        Assert.False(client.IsReady);
    }

    [Fact]
    public async Task Development_mode_skips_the_fingerprint()
    {
        _ = Serve();
        await using var client = Client(fingerprint: null);

        Assert.Equal(HandshakeResult.Ready, await client.HandshakeAsync(default));
    }

    [Fact]
    public async Task Dropped_connection_reconnects_and_repeats_the_handshake()
    {
        _ = Serve(dropFirstAfterHello: true);
        await using var client = Client(Fingerprint);
        await client.HandshakeAsync(default);

        var result = await client.LoginAsync("tamer", "right", default);

        Assert.Equal(LauncherLoginStatus.Ok, result!.Status);
        Assert.Equal(2, _connections);
    }

    [Fact]
    public async Task Disabled_server_is_reported()
    {
        _ = Serve(status: LauncherHelloStatus.Disabled);
        await using var client = Client(Fingerprint);

        Assert.Equal(HandshakeResult.Disabled, await client.HandshakeAsync(default));
    }

    [Fact]
    public async Task Nobody_listening_is_unreachable()
    {
        _listener.Stop();
        await using var client = Client(Fingerprint);

        Assert.Equal(HandshakeResult.Unreachable, await client.HandshakeAsync(default));
    }
}
