using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using DRPGServer.Game.Entities;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging;

namespace DRPGServer.Network;

/// <summary>
/// Uma conexão TCP com o cliente. Recebe em um laço próprio e envia por uma fila:
/// <see cref="Send"/> pode ser chamado de qualquer thread e nunca bloqueia.
/// </summary>
public sealed class Session
{
    private const int MaxQueuedPackets = 4096;
    private static long _nextId;

    private readonly Socket _socket;
    private readonly ILogger _log;
    private readonly PacketTrace _trace;
    private readonly Channel<byte[]> _outgoing = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(MaxQueuedPackets) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _closed = new();
    private string? _closeReason;

    public Session(Socket? socket, ServerRole role, ILogger log, PacketTrace trace)
    {
        _socket = socket!;
        _log = log;
        _trace = trace;
        Role = role;
        Id = Interlocked.Increment(ref _nextId);
        RemoteEndPoint = socket?.RemoteEndPoint as IPEndPoint;
    }

    /// <summary>Testes: tira da fila o que seria enviado.</summary>
    internal List<byte[]> DrainOutgoing()
    {
        var frames = new List<byte[]>();
        while (_outgoing.Reader.TryRead(out var frame)) frames.Add(frame);
        return frames;
    }

    public long Id { get; }
    public ServerRole Role { get; }
    public IPEndPoint? RemoteEndPoint { get; }
    public bool IsClosed => _closed.IsCancellationRequested;
    public CancellationToken ClosedToken => _closed.Token;

    /// <summary>Conta autenticada (preenchida pelo handler de autenticação de cada servidor).</summary>
    public AuthenticatedAccount? Account { get; set; }

    /// <summary>Personagem no mundo (servidor de mapa). Só ler e escrever na thread do mundo.</summary>
    public Player? Player { get; set; }

    /// <summary>✅ Chave AES-128 derivada do 0x0E global (para cifrar/decifrar 0x7B e 0x7C).</summary>
    public byte[]? GlobalAesKey { get; set; }

    /// <summary>✅ Chave AES-128 derivada do 0x0E do mapa (para cifrar Map 0xE6 de evento de EXP).</summary>
    public byte[]? MapAesKey { get; set; }

    public override string ToString() =>
        $"#{Id} {Role} {RemoteEndPoint}{(Account is null ? "" : $" [{Account.Username}]")}";

    public void Send(ServerPacket packet)
    {
        if (IsClosed) return;
        var frame = packet.Encode();
        _trace.Outgoing(this, packet.Opcode, frame);
        Enqueue(frame);
    }

    /// <summary>Envia um quadro já codificado (broadcast: codifica uma vez, envia para vários).</summary>
    public void SendFrame(byte[] frame)
    {
        if (IsClosed) return;
        Enqueue(frame);
    }

    private void Enqueue(byte[] frame)
    {
        if (!_outgoing.Writer.TryWrite(frame))
            Close("fila de envio cheia");
    }

    public void Close(string reason)
    {
        if (_closed.IsCancellationRequested) return;
        _closeReason = reason;
        _outgoing.Writer.TryComplete();
        _closed.Cancel();
    }

    /// <summary>Roda até a conexão fechar. Cada pacote completo vai para <paramref name="onFrame"/>, em ordem.</summary>
    internal async Task RunAsync(Func<Session, IncomingFrame, ValueTask> onFrame, CancellationToken stopping)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping, _closed.Token);
        var stream = new NetworkStream(_socket, ownsSocket: false);
        var sender = SendLoopAsync(stream);
        try
        {
            var reader = PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: 16 * 1024));
            await ReceiveLoopAsync(reader, onFrame, linked.Token);
            await reader.CompleteAsync();
        }
        catch (OperationCanceledException) { }
        catch (IOException) { _closeReason ??= "conexão encerrada pelo cliente"; }
        catch (SocketException e) { _closeReason ??= $"erro de socket: {e.SocketErrorCode}"; }
        finally
        {
            Close(_closeReason ?? "conexão encerrada pelo cliente");
            // deixa a fila terminar de sair (ex.: erro antes do fechamento), mas sem esperar para sempre
            await Task.WhenAny(sender, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None));
            try { _socket.Shutdown(SocketShutdown.Both); } catch { }
            _socket.Dispose();
            await stream.DisposeAsync();
            _log.LogInformation("Sessão {Session} fechada: {Reason}", this, _closeReason);
        }
    }

    private async Task ReceiveLoopAsync(PipeReader reader, Func<Session, IncomingFrame, ValueTask> onFrame, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var result = await reader.ReadAsync(ct);
            var buffer = result.Buffer;
            while (true)
            {
                var status = PacketFramer.TryRead(ref buffer, out var frame, out var error);
                if (status == FrameStatus.Incomplete) break;
                if (status == FrameStatus.Invalid)
                {
                    Close($"fluxo corrompido: {error}");
                    return;
                }
                _trace.Incoming(this, frame!);
                await onFrame(this, frame!);
                if (IsClosed) return;
            }
            reader.AdvanceTo(buffer.Start, buffer.End);
            if (result.IsCompleted) return;
        }
    }

    private async Task SendLoopAsync(NetworkStream stream)
    {
        try
        {
            await foreach (var frame in _outgoing.Reader.ReadAllAsync())
                await stream.WriteAsync(frame);
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
            Close("falha ao enviar");
        }
    }
}
