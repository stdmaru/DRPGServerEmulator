using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Sockets;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Net;

/// <summary>
/// One TCP connection to a game server (login, channel, map or global). A receive task cuts the stream into frames with
/// <see cref="PacketFramer"/>, decodes each into a typed packet and queues it; the game thread takes them with
/// <see cref="TryReceive"/>. A packet the decoder rejects, a closed socket or an I/O error ends the connection with a
/// <see cref="Failure"/> message (docs/arquitetura/cliente.md#erros-no-cliente). Sending is non-blocking: frames go to
/// a queue written by a send task.
/// </summary>
public sealed class ClientConnection : IDisposable
{
    /// <summary>More queued packets than this and the server is flooding us: the connection is closed.</summary>
    public const int MaxQueuedPackets = 1024;

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Func<IncomingFrame, object> _decode;
    private readonly ConcurrentQueue<object> _inbox = new();
    private readonly System.Threading.Channels.Channel<byte[]> _outbox = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
    private readonly CancellationTokenSource _stop = new();
    private volatile string? _failure;

    private ClientConnection(TcpClient client, Func<IncomingFrame, object> decode)
    {
        _client = client;
        _stream = client.GetStream();
        _decode = decode;
        Completion = Task.WhenAll(ReceiveLoopAsync(_stop.Token), SendLoopAsync(_stop.Token));
    }

    /// <summary>Completes when both loops ended (after <see cref="Dispose"/> or a failure). Never faults.</summary>
    public Task Completion { get; }

    /// <summary>Why the connection ended, or null while it is up.</summary>
    public string? Failure => _failure;

    /// <summary>Opens the connection. <paramref name="decode"/> turns a frame into a typed packet and throws
    /// <see cref="PacketFormatException"/> for an opcode or layout the client does not accept.</summary>
    public static async Task<ClientConnection> ConnectAsync(ServerAddress server, Func<IncomingFrame, object> decode, CancellationToken ct)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(server.Host, server.Port, ct);
            return new ClientConnection(client, decode);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Queues a packet for sending; ignored after the connection ended.</summary>
    public void Send<T>(T packet) where T : ITwoWayPacket<T> => _outbox.Writer.TryWrite(TwoWayPacket.Encode(packet));

    /// <summary>Takes the next received packet, if any. Game thread only.</summary>
    public bool TryReceive(out object packet)
    {
        if (_inbox.TryDequeue(out var next))
        {
            packet = next;
            return true;
        }
        packet = null!; // never read when false
        return false;
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[PacketHeader.MaxPacketSize * 2];
        int buffered = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await _stream.ReadAsync(buffer.AsMemory(buffered), ct);
                if (read == 0)
                {
                    Fail("The server closed the connection.");
                    return;
                }
                buffered += read;
                buffered = Drain(buffer, buffered);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or PacketFormatException)
        {
            Fail(e is PacketFormatException ? $"Invalid packet from the server: {e.Message}" : $"Connection lost: {e.Message}");
        }
    }

    /// <summary>Decodes every complete frame at the start of the buffer and returns how many bytes remain.</summary>
    private int Drain(byte[] buffer, int buffered)
    {
        while (true)
        {
            var sequence = new ReadOnlySequence<byte>(buffer, 0, buffered);
            var status = PacketFramer.TryRead(ref sequence, out var frame, out var error);
            if (status == FrameStatus.Invalid)
                throw new PacketFormatException(error ?? "invalid frame");
            if (status == FrameStatus.Incomplete)
                return buffered;
            int used = buffered - (int)sequence.Length;
            Buffer.BlockCopy(buffer, used, buffer, 0, buffered - used);
            buffered -= used;
            if (_inbox.Count >= MaxQueuedPackets)
                throw new PacketFormatException("too many packets queued");
            _inbox.Enqueue(_decode(frame!));
        }
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _outbox.Reader.ReadAllAsync(ct))
                await _stream.WriteAsync(frame, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
            Fail($"Connection lost: {e.Message}");
        }
    }

    private void Fail(string message) => _failure ??= message;

    /// <summary>Closes the socket; both tasks end by themselves (cancellation or the closed socket).</summary>
    public void Dispose()
    {
        _stop.Cancel();
        _outbox.Writer.TryComplete();
        _client.Dispose();
    }
}
