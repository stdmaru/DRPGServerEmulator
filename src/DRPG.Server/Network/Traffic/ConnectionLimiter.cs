using System.Net;
using DRPG.Server.Configuration;
using Microsoft.Extensions.Options;

namespace DRPG.Server.Network.Traffic;

public enum ConnectionRejection
{
    None,
    TooManyTotal,
    TooManyFromAddress,
}

/// <summary>
/// Conexões abertas, no total e por IP, somando os quatro servidores. Cada <see cref="TryAcquire"/> aceito precisa de um
/// <see cref="Release"/> quando a conexão fecha. Seguro de qualquer thread.
/// </summary>
public sealed class ConnectionLimiter(IOptions<NetworkOptions> options)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<IPAddress, int> _byAddress = [];
    private int _total;

    public int Total
    {
        get { lock (_lock) return _total; }
    }

    public ConnectionRejection TryAcquire(IPAddress address)
    {
        var o = options.Value;
        lock (_lock)
        {
            if (_total >= o.MaxConnections) return ConnectionRejection.TooManyTotal;
            _byAddress.TryGetValue(address, out var count);
            if (count >= o.MaxConnectionsPerIp) return ConnectionRejection.TooManyFromAddress;
            _byAddress[address] = count + 1;
            _total++;
            return ConnectionRejection.None;
        }
    }

    public void Release(IPAddress address)
    {
        lock (_lock)
        {
            if (!_byAddress.TryGetValue(address, out var count)) return;
            if (count <= 1) _byAddress.Remove(address);
            else _byAddress[address] = count - 1;
            _total--;
        }
    }
}
