using System.Buffers;
using System.Net.Sockets;
using System.Security.Cryptography;
using DRPG.Shared.Protocol;
using DRPG.Shared.Protocol.Launcher;

namespace DRPG.Launcher.Login;

/// <summary>Outcome of the handshake, shown in the login screen's status box.</summary>
public enum HandshakeResult
{
    Ready,
    Unreachable,
    UnsupportedProtocol,
    Disabled,
    FingerprintMismatch,
    BadReply,
}

/// <summary>
/// Launcher side of the LoginServer login (docs/protocolo/launcher.md): Hello → HelloReply with the server key and
/// nonce, then Login sealed to that key. The server closes idle connections after 30 s, so a login on a dropped
/// connection reconnects and repeats the handshake once, without the user noticing.
/// </summary>
public sealed class LoginClient(LoginClient.Options options) : IAsyncDisposable
{
    public sealed record Options(
        Func<CancellationToken, Task<Stream>> Connect,
        uint LauncherVersion,
        string? KeyFingerprint, // null = development mode: the key is not checked
        TimeSpan Timeout);

    public static Func<CancellationToken, Task<Stream>> Tcp(string host, int port) => async ct =>
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(host, port, ct);
            return client.GetStream();
        }
        catch
        {
            client.Dispose();
            throw;
        }
    };

    private Stream? _stream;
    private byte[] _serverKey = [];
    private byte[] _nonce = [];
    private readonly byte[] _buffer = new byte[PacketHeader.MaxPacketSize];
    private int _buffered;

    public bool IsReady => _stream is not null;

    public async Task<HandshakeResult> HandshakeAsync(CancellationToken ct)
    {
        await CloseAsync();
        using var timeout = Deadline(ct);
        try
        {
            _stream = await options.Connect(timeout.Token);
            await SendAsync(new LauncherHello(LauncherLimits.ProtocolVersion, options.LauncherVersion), timeout.Token);
            var reply = await ReceiveAsync<LauncherHelloReply>(timeout.Token);
            var result = Check(reply);
            if (result != HandshakeResult.Ready)
            {
                await CloseAsync();
                return result;
            }
            _serverKey = reply.PublicKey;
            _nonce = reply.Nonce;
            return HandshakeResult.Ready;
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            await CloseAsync();
            return HandshakeResult.Unreachable;
        }
        catch (PacketFormatException)
        {
            await CloseAsync();
            return HandshakeResult.BadReply;
        }
    }

    private HandshakeResult Check(LauncherHelloReply reply)
    {
        if (reply.Status == LauncherHelloStatus.Disabled)
            return HandshakeResult.Disabled;
        if (reply.Status != LauncherHelloStatus.Ok || reply.Protocol != LauncherLimits.ProtocolVersion)
            return HandshakeResult.UnsupportedProtocol;
        if (options.KeyFingerprint is { } expected && !LauncherCrypto.MatchesFingerprint(reply.PublicKey, expected))
            return HandshakeResult.FingerprintMismatch;
        return HandshakeResult.Ready;
    }

    /// <summary>
    /// Sends the credentials. Returns null when the server cannot be reached even after a fresh handshake,
    /// or when the handshake now fails (for example a different key).
    /// </summary>
    public async Task<LauncherLoginResult?> LoginAsync(string login, string password, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (!IsReady && await HandshakeAsync(ct) != HandshakeResult.Ready)
                return null;
            try
            {
                return await SendLoginAsync(login, password, ct);
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                await CloseAsync(); // dropped (idle timeout or network): reconnect once
            }
        }
        return null;
    }

    private async Task<LauncherLoginResult> SendLoginAsync(string login, string password, CancellationToken ct)
    {
        using var timeout = Deadline(ct);
        var responseKey = RandomNumberGenerator.GetBytes(LauncherCrypto.KeyBytes);
        try
        {
            var credentials = new LauncherCredentials(_nonce, responseKey, options.LauncherVersion, login, password);
            var plaintext = credentials.Encode();
            try
            {
                await SendAsync(LauncherCrypto.Seal(_serverKey, _nonce, plaintext), timeout.Token);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
            var reply = await ReceiveAsync<LauncherLoginReply>(timeout.Token);
            return LauncherLoginResult.Decode(LauncherCrypto.OpenReply(responseKey, reply));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(responseKey);
            await CloseAsync(); // the nonce is single-use: a new attempt needs a new handshake
        }
    }

    private CancellationTokenSource Deadline(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(options.Timeout);
        return cts;
    }

    private async Task SendAsync<T>(T packet, CancellationToken ct) where T : ITwoWayPacket<T>
    {
        var stream = _stream ?? throw new IOException("not connected");
        await stream.WriteAsync(TwoWayPacket.Encode(packet), ct);
        await stream.FlushAsync(ct);
    }

    private async Task<T> ReceiveAsync<T>(CancellationToken ct) where T : ITwoWayPacket<T>
    {
        var stream = _stream ?? throw new IOException("not connected");
        while (true)
        {
            var sequence = new ReadOnlySequence<byte>(_buffer, 0, _buffered);
            var status = PacketFramer.TryRead(ref sequence, out var frame, out var error);
            if (status == FrameStatus.Invalid)
                throw new PacketFormatException(error ?? "invalid frame");
            if (status == FrameStatus.Complete)
            {
                int used = _buffered - (int)sequence.Length;
                Buffer.BlockCopy(_buffer, used, _buffer, 0, _buffered - used);
                _buffered -= used;
                if (frame!.Opcode != T.Opcode)
                    throw new PacketFormatException($"expected 0x{T.Opcode:X4}, got 0x{frame.Opcode:X4}");
                return ClientPacket.Parse<T>(frame.Payload);
            }
            int n = await stream.ReadAsync(_buffer.AsMemory(_buffered), ct);
            if (n == 0)
                throw new IOException("connection closed");
            _buffered += n;
        }
    }

    private async Task CloseAsync()
    {
        _buffered = 0;
        _serverKey = [];
        _nonce = [];
        if (_stream is { } s)
        {
            _stream = null;
            await s.DisposeAsync();
        }
    }

    public ValueTask DisposeAsync() => new(CloseAsync());
}
