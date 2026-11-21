using DRPGServer.Configuration;
using DRPGServer.Game.Entities;
using Microsoft.Extensions.Options;

namespace DRPGServer.Network;

/// <summary>
/// Liga as conexões dos quatro servidores à mesma conta. O channel autentica (usuário + auth key)
/// e registra a conta; o mapa e o global só aceitam quem passou pelo channel. O registro vale
/// enquanto houver alguma sessão da conta aberta e mais um tempo de folga depois disso.
/// </summary>
public sealed class AccountRegistry(IOptions<GameOptions> options, TimeProvider time)
{
    private sealed class Entry(AuthenticatedAccount account)
    {
        public AuthenticatedAccount Account { get; set; } = account;
        public HashSet<Session> Sessions { get; } = [];
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private TimeSpan Grace => TimeSpan.FromSeconds(options.Value.AccountTicketGraceSeconds);

    /// <summary>Login no channel. Fecha outra conexão de channel da mesma conta (login duplo).</summary>
    public void SignIn(AuthenticatedAccount account, Session channelSession)
    {
        List<Session> kick;
        lock (_lock)
        {
            if (!_entries.TryGetValue(account.Username, out var entry))
                _entries[account.Username] = entry = new Entry(account);
            entry.Account = account;
            entry.ExpiresAt = null;
            kick = entry.Sessions.Where(s => s.Role == ServerRole.Channel && s != channelSession).ToList();
            entry.Sessions.Add(channelSession);
        }
        channelSession.Account = account;
        foreach (var s in kick)
            s.Close("login da mesma conta em outra conexão");
    }

    /// <summary>Mapa/global: aceita a conexão se a conta passou pelo channel. Null = recusar.</summary>
    public AuthenticatedAccount? Claim(string username, Session session)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(username, out var entry)) return null;
            if (entry.ExpiresAt is { } expires && time.GetUtcNow() > expires)
            {
                _entries.Remove(username);
                return null;
            }
            entry.ExpiresAt = null;
            entry.Sessions.Add(session);
            session.Account = entry.Account;
            return entry.Account;
        }
    }

    public void Release(Session session)
    {
        if (session.Account is not { } account) return;
        lock (_lock)
        {
            if (!_entries.TryGetValue(account.Username, out var entry)) return;
            entry.Sessions.Remove(session);
            if (entry.Sessions.Count == 0)
                entry.ExpiresAt = time.GetUtcNow() + Grace;
            foreach (var (name, stale) in _entries.Where(e => e.Value.ExpiresAt < time.GetUtcNow()).ToList())
                _entries.Remove(name);
        }
    }

    public IReadOnlyList<Session> SessionsOf(string username)
    {
        lock (_lock)
            return _entries.TryGetValue(username, out var entry) ? entry.Sessions.ToList() : [];
    }
}
