using DRPGServer.Configuration;
using DRPGServer.Network.Traffic;
using Microsoft.Extensions.Options;

namespace DRPGServer.Game.Accounts;

/// <summary>
/// Launcher login attempts per address and per login name (token buckets). Shared by every LoginServer session, so
/// access is serialized with a lock; this is network-edge code, not game state. The table is cleared when it grows past
/// <see cref="MaxTracked"/>, which only resets the limits early.
/// </summary>
public sealed class LoginAttemptLimiter(IOptions<LauncherLoginOptions> options, TimeProvider time)
{
    private const int MaxTracked = 50_000;

    private readonly object _gate = new();
    private readonly Dictionary<string, TokenBucket> _addresses = [];
    private readonly Dictionary<string, TokenBucket> _logins = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Spends one attempt for both keys; false when either is exhausted.</summary>
    public bool TryAttempt(string address, string login)
    {
        lock (_gate)
        {
            var byAddress = Bucket(_addresses, address, options.Value.AttemptsPerAddress);
            var byLogin = Bucket(_logins, login, options.Value.AttemptsPerLogin);
            return byAddress.TryTake() & byLogin.TryTake();
        }
    }

    private TokenBucket Bucket(Dictionary<string, TokenBucket> table, string key, RateLimitOptions limit)
    {
        if (table.TryGetValue(key, out var bucket)) return bucket;
        if (table.Count >= MaxTracked) table.Clear();
        return table[key] = new TokenBucket(limit, time);
    }
}
