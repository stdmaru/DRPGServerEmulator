using System.Collections.Frozen;
using DRPGServer.Configuration;
using Microsoft.Extensions.Options;

namespace DRPGServer.Network.Traffic;

/// <summary>Monta os limites de tráfego de cada sessão a partir da seção <c>Network</c> (já validada na partida).</summary>
public sealed class TrafficLimits
{
    private readonly NetworkOptions _options;
    private readonly TimeProvider _time;
    private readonly FrozenDictionary<ServerRole, (ushort Opcode, RateLimitOptions Limit)[]> _opcodeLimits;

    public TrafficLimits(IOptions<NetworkOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
        _opcodeLimits = _options.OpcodeLimits.ToFrozenDictionary(
            byRole => byRole.Key,
            byRole => byRole.Value.Select(entry => (Resolve(byRole.Key, entry.Key), entry.Value)).ToArray());
    }

    public SessionTraffic Start(ServerRole role)
    {
        var opcodes = _opcodeLimits.TryGetValue(role, out var limits)
            ? limits.ToFrozenDictionary(l => l.Opcode, l => new TokenBucket(l.Limit, _time))
            : FrozenDictionary<ushort, TokenBucket>.Empty;
        var idle = role == ServerRole.Map ? _options.MapIdleTimeoutSeconds : _options.IdleTimeoutSeconds;
        return new SessionTraffic(_time, new TokenBucket(_options.Packets, _time), opcodes,
            TimeSpan.FromSeconds(_options.AuthTimeoutSeconds), TimeSpan.FromSeconds(idle), _options.MaxPendingWorldWork);
    }

    private static ushort Resolve(ServerRole role, string name) =>
        OpcodeNames.TryParse(role, name, out var opcode)
            ? opcode
            : throw new InvalidOperationException($"Network:OpcodeLimits:{role}:{name}: opcode desconhecido");
}
