using System.Collections.Frozen;

namespace DRPG.Server.Network.Traffic;

/// <summary>O que um pacote recebido faz depois de passar pelos limites da sessão.</summary>
public enum TrafficVerdict
{
    Accept,

    /// <summary>Passou do teto do opcode: o pacote é ignorado e a sessão continua.</summary>
    Drop,

    /// <summary>Passou do teto geral: a sessão fecha.</summary>
    Close,
}

/// <summary>
/// Limites de tráfego de uma sessão (docs/emulador/seguranca.md, limites de recurso): balde geral, baldes por opcode,
/// prazos de autenticação e inatividade e trabalhos pendentes no mundo. Criado por <see cref="TrafficLimits"/>.
/// <see cref="Admit"/> roda só no laço de recepção; <see cref="ExpiredReason"/> e os trabalhos do mundo podem vir de
/// outras threads.
/// </summary>
public sealed class SessionTraffic
{
    private readonly TimeProvider _time;
    private readonly TokenBucket _packets;
    private readonly FrozenDictionary<ushort, TokenBucket> _opcodes;
    private readonly TimeSpan _authTimeout;
    private readonly TimeSpan _idleTimeout;
    private readonly int _maxPendingWorldWork;
    private readonly long _connectedAt;
    private long _lastReceivedAt;
    private int _pendingWorldWork;

    internal SessionTraffic(TimeProvider time, TokenBucket packets, FrozenDictionary<ushort, TokenBucket> opcodes,
        TimeSpan authTimeout, TimeSpan idleTimeout, int maxPendingWorldWork)
    {
        _time = time;
        _packets = packets;
        _opcodes = opcodes;
        _authTimeout = authTimeout;
        _idleTimeout = idleTimeout;
        _maxPendingWorldWork = maxPendingWorldWork;
        _connectedAt = time.GetTimestamp();
        _lastReceivedAt = _connectedAt;
    }

    public int PendingWorldWork => Volatile.Read(ref _pendingWorldWork);

    /// <summary>Conta o pacote recebido: renova o prazo de inatividade e gasta uma ficha do geral e a do opcode.</summary>
    public TrafficVerdict Admit(ushort opcode)
    {
        Volatile.Write(ref _lastReceivedAt, _time.GetTimestamp());
        if (!_packets.TryTake()) return TrafficVerdict.Close;
        if (_opcodes.TryGetValue(opcode, out var bucket) && !bucket.TryTake()) return TrafficVerdict.Drop;
        return TrafficVerdict.Accept;
    }

    /// <summary>Motivo para fechar a sessão por prazo vencido, ou <c>null</c> se ela está em dia.</summary>
    public string? ExpiredReason(bool authenticated)
    {
        var now = _time.GetTimestamp();
        if (!authenticated && _time.GetElapsedTime(_connectedAt, now) > _authTimeout)
            return $"sem autenticar em {_authTimeout.TotalSeconds:0} s";
        if (_time.GetElapsedTime(Volatile.Read(ref _lastReceivedAt), now) > _idleTimeout)
            return $"inativa por {_idleTimeout.TotalSeconds:0} s";
        return null;
    }

    /// <summary>Reserva um lugar na fila do mundo. <c>false</c>: a sessão já tem trabalhos demais esperando.</summary>
    public bool TryBeginWorldWork()
    {
        if (Interlocked.Increment(ref _pendingWorldWork) <= _maxPendingWorldWork) return true;
        Interlocked.Decrement(ref _pendingWorldWork);
        return false;
    }

    public void EndWorldWork() => Interlocked.Decrement(ref _pendingWorldWork);
}
