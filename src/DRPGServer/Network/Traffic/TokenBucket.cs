using DRPGServer.Configuration;

namespace DRPGServer.Network.Traffic;

/// <summary>
/// Balde de fichas: começa cheio, cada pacote gasta uma ficha e elas voltam a <see cref="RateLimitOptions.PerSecond"/>
/// por segundo, até <see cref="RateLimitOptions.Burst"/>. Não é thread-safe: cada sessão usa os seus só no laço de
/// recepção dela.
/// </summary>
public sealed class TokenBucket(RateLimitOptions limit, TimeProvider time)
{
    private double _tokens = limit.Burst;
    private long _refilledAt = time.GetTimestamp();

    public bool TryTake()
    {
        var now = time.GetTimestamp();
        _tokens = Math.Min(limit.Burst, _tokens + time.GetElapsedTime(_refilledAt, now).TotalSeconds * limit.PerSecond);
        _refilledAt = now;
        if (_tokens < 1) return false;
        _tokens -= 1;
        return true;
    }
}
