using System.Net;
using DRPGServer.Configuration;
using DRPGServer.Game.Entities;
using Microsoft.Extensions.Options;

namespace DRPGServer.Network;

/// <summary>Por que o mapa ou o global recusou a conta (vai para o log).</summary>
public enum ClaimRejection
{
    None,
    /// <summary>A conta não autenticou no channel (ou o ticket foi substituído por outro login).</summary>
    NoTicket,
    /// <summary>A última sessão da conta fechou há mais tempo que <see cref="NetworkOptions.AccountTicketSeconds"/>.</summary>
    Expired,
    /// <summary>A conexão vem de um IP diferente do da conexão do channel.</summary>
    WrongAddress,
    /// <summary>O ticket já foi usado neste servidor.</summary>
    AlreadyUsed,
    /// <summary>Só mapa e global reivindicam tickets.</summary>
    WrongRole,
}

/// <summary>Resultado de <see cref="AccountRegistry.Claim"/>.</summary>
public readonly record struct AccountClaim(AuthenticatedAccount? Account, ClaimRejection Rejection)
{
    public bool Accepted => Account is not null;
}

/// <summary>
/// Liga as conexões dos quatro servidores à mesma conta (docs/emulador/seguranca.md#autenticação-entre-servidores).
/// <list type="bullet">
/// <item>O channel autentica (usuário + auth key) e emite um <b>ticket</b> da conta, amarrado ao IP da conexão do
/// channel.</item>
/// <item>Mapa e global aceitam a conta só com ticket válido, do mesmo IP, e <b>uma vez por servidor</b>: o ticket não
/// serve para uma segunda conexão de mapa, nem depois que a primeira fecha. Entrar de novo exige autenticar no channel
/// (✅ o cliente mantém o channel aberto durante o jogo e fecha as três conexões juntas ao sair).</item>
/// <item>O ticket não vence enquanto alguma sessão da conta está aberta. Depois que a última fecha, ele vale por
/// <see cref="NetworkOptions.AccountTicketSeconds"/>.</item>
/// <item>Um novo login no channel substitui o ticket e derruba as sessões do anterior.</item>
/// </list>
/// O username do pacote do mapa e do global só serve para achar o ticket.
/// </summary>
public sealed class AccountRegistry(IOptions<NetworkOptions> options, TimeProvider time)
{
    private sealed class Ticket(AuthenticatedAccount account, Session channel, IPAddress address)
    {
        public AuthenticatedAccount Account { get; } = account;
        public IPAddress Address { get; } = address;
        public Session? Channel { get; set; } = channel;
        public Session? Map { get; set; }
        public Session? Global { get; set; }
        public bool MapUsed { get; set; }
        public bool GlobalUsed { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }

        public IEnumerable<Session> Sessions =>
            new[] { Channel, Map, Global }.Where(s => s is not null).Select(s => s!);

        public bool Holds(Session session) => session == Channel || session == Map || session == Global;
    }

    private readonly Dictionary<string, Ticket> _tickets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private TimeSpan Validity => TimeSpan.FromSeconds(options.Value.AccountTicketSeconds);

    /// <summary>
    /// Login no channel: emite o ticket da conta. Um ticket anterior é substituído e as sessões dele fecham (login
    /// duplo). Falha se a sessão não tem IP.
    /// </summary>
    public bool SignIn(AuthenticatedAccount account, Session channelSession)
    {
        if (channelSession.Role != ServerRole.Channel || channelSession.RemoteAddress is not { } address) return false;

        List<Session> kick = [];
        lock (_lock)
        {
            Purge();
            if (_tickets.TryGetValue(account.Username, out var previous))
                kick.AddRange(previous.Sessions.Where(s => s != channelSession));
            _tickets[account.Username] = new Ticket(account, channelSession, address);
        }
        channelSession.Account = account;
        foreach (var s in kick)
            s.Close("login da mesma conta em outra conexão");
        return true;
    }

    /// <summary>Mapa/global: aceita a conexão com o ticket da conta. Aceita, preenche <see cref="Session.Account"/>.</summary>
    public AccountClaim Claim(string username, Session session)
    {
        if (session.Role is not (ServerRole.Map or ServerRole.Global)) return new(null, ClaimRejection.WrongRole);

        lock (_lock)
        {
            if (!_tickets.TryGetValue(username, out var ticket)) return new(null, ClaimRejection.NoTicket);
            if (ticket.ExpiresAt is { } expires && time.GetUtcNow() > expires)
            {
                _tickets.Remove(username);
                return new(null, ClaimRejection.Expired);
            }
            if (session.RemoteAddress is null || !session.RemoteAddress.Equals(ticket.Address))
                return new(null, ClaimRejection.WrongAddress);

            bool map = session.Role == ServerRole.Map;
            if (map ? ticket.MapUsed : ticket.GlobalUsed) return new(null, ClaimRejection.AlreadyUsed);

            if (map)
            {
                ticket.MapUsed = true;
                ticket.Map = session;
            }
            else
            {
                ticket.GlobalUsed = true;
                ticket.Global = session;
            }
            ticket.ExpiresAt = null;
            session.Account = ticket.Account;
            return new(ticket.Account, ClaimRejection.None);
        }
    }

    /// <summary>A sessão fechou: libera o lugar dela no ticket. Sem sessão aberta, o ticket começa a vencer.</summary>
    public void Release(Session session)
    {
        if (session.Account is not { } account) return;
        lock (_lock)
        {
            if (_tickets.TryGetValue(account.Username, out var ticket) && ticket.Holds(session))
            {
                if (ticket.Channel == session) ticket.Channel = null;
                if (ticket.Map == session) ticket.Map = null;
                if (ticket.Global == session) ticket.Global = null;
                if (!ticket.Sessions.Any())
                    ticket.ExpiresAt = time.GetUtcNow() + Validity;
            }
            Purge();
        }
    }

    /// <summary>
    /// Sessões abertas do ticket atual da conta (diagnóstico e testes). Para chegar no personagem, use
    /// <c>PlayerSystem.FindByAccount</c>.
    /// </summary>
    internal IReadOnlyList<Session> SessionsOf(string username)
    {
        lock (_lock)
            return _tickets.TryGetValue(username, out var ticket) ? ticket.Sessions.ToList() : [];
    }

    private void Purge()
    {
        var now = time.GetUtcNow();
        foreach (var (name, _) in _tickets.Where(t => t.Value.ExpiresAt < now).ToList())
            _tickets.Remove(name);
    }
}
