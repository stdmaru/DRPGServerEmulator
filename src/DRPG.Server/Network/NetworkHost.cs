using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DRPG.Server.Configuration;
using DRPG.Server.Network.Handlers;
using DRPG.Server.Network.Traffic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DRPG.Server.Network;

/// <summary>
/// Abre os quatro servidores TCP e roda uma sessão por conexão. Recusa conexão acima dos limites por IP e no total, e
/// fecha a cada segundo as sessões que passaram do prazo de autenticação ou de inatividade.
/// </summary>
public sealed class NetworkHost(
    IOptions<ServersOptions> options,
    PacketDispatcher dispatcher,
    IEnumerable<ISessionLifecycle> lifecycles,
    PacketTrace trace,
    ConnectionLimiter connections,
    TrafficLimits traffic,
    TimeProvider time,
    ILoggerFactory loggers) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RejectionLogInterval = TimeSpan.FromMinutes(1);

    private readonly ILogger _log = loggers.CreateLogger<NetworkHost>();
    private readonly ConcurrentDictionary<IPAddress, long> _rejectionLoggedAt = new();
    private readonly ConcurrentDictionary<long, Session> _sessions = new();
    private readonly ConcurrentDictionary<long, Task> _running = new();
    private readonly ILookup<ServerRole, ISessionLifecycle> _lifecycles = lifecycles.ToLookup(l => l.Role);

    public IEnumerable<Session> Sessions => _sessions.Values;

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        var o = options.Value;
        var listeners = new (ServerRole Role, EndpointOptions Endpoint)[]
        {
            (ServerRole.Login, o.Login), (ServerRole.Channel, o.Channel), (ServerRole.Map, o.Map), (ServerRole.Global, o.Global),
        };
        _log.LogInformation("{Count} handlers de pacote registrados", dispatcher.Count);
        await Task.WhenAll(listeners.Select(l => ListenAsync(l.Role, l.Endpoint, stopping)).Append(SweepAsync(stopping)));
    }

    private async Task ListenAsync(ServerRole role, EndpointOptions endpoint, CancellationToken stopping)
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Bind(new IPEndPoint(IPAddress.Parse(endpoint.BindIP), endpoint.Port));
        listener.Listen(128);
        _log.LogInformation("Servidor {Role} ouvindo em {Ip}:{Port}", role, endpoint.BindIP, endpoint.Port);

        while (!stopping.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await listener.AcceptAsync(stopping);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            if (Admit(socket, role) is not { } address) continue;
            socket.NoDelay = true;
            var session = new Session(socket, role, traffic.Start(role), _log, trace);
            _running[session.Id] = RunSessionAsync(session, address, stopping);   // acompanhada: a parada espera o fim de cada sessão
        }
    }

    /// <summary>Confere os limites de conexão. Recusada, o socket é fechado e o resultado é <c>null</c>.</summary>
    private IPAddress? Admit(Socket socket, ServerRole role)
    {
        var address = (socket.RemoteEndPoint as IPEndPoint)?.Address;
        if (address is { IsIPv4MappedToIPv6: true }) address = address.MapToIPv4();
        var rejection = address is null ? ConnectionRejection.TooManyTotal : connections.TryAcquire(address);
        if (rejection == ConnectionRejection.None) return address;

        if (address is not null && ShouldLogRejection(address))
            _log.LogWarning("Conexão {Role} de {Address} recusada: {Reason} ({Total} abertas)", role, address,
                rejection == ConnectionRejection.TooManyFromAddress ? "conexões demais deste IP" : "conexões demais no servidor",
                connections.Total);
        try { socket.Close(); } catch (SocketException) { }
        return null;
    }

    /// <summary>Um log por IP por minuto: quem insiste em conectar não enche o log.</summary>
    private bool ShouldLogRejection(IPAddress address)
    {
        var now = time.GetTimestamp();
        if (_rejectionLoggedAt.TryGetValue(address, out var last) && time.GetElapsedTime(last, now) < RejectionLogInterval)
            return false;
        _rejectionLoggedAt[address] = now;
        return true;
    }

    private async Task SweepAsync(CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(SweepInterval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stopping))
            {
                foreach (var session in _sessions.Values)
                {
                    if (session.Traffic.ExpiredReason(authenticated: session.Account is not null) is not { } reason) continue;
                    _log.LogWarning("Sessão {Session} cortada: {Reason}", session, reason);
                    session.Close(reason);
                }
                var now = time.GetTimestamp();
                foreach (var (address, last) in _rejectionLoggedAt)
                    if (time.GetElapsedTime(last, now) >= RejectionLogInterval) _rejectionLoggedAt.TryRemove(address, out _);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunSessionAsync(Session session, IPAddress address, CancellationToken stopping)
    {
        await Task.Yield();     // devolve a Task antes de rodar: ela entra em _running antes do TryRemove do fim
        _sessions[session.Id] = session;
        _log.LogInformation("Conexão {Session}", session);
        try
        {
            foreach (var lifecycle in _lifecycles[session.Role])
                lifecycle.OnConnected(session);
            await session.RunAsync((s, frame) => dispatcher.DispatchAsync(s, frame, stopping), stopping);
        }
        catch (Exception e)
        {
            _log.LogError(e, "Falha na sessão {Session}", session);
            session.Close("erro interno");
        }
        finally
        {
            _sessions.TryRemove(session.Id, out _);
            foreach (var lifecycle in _lifecycles[session.Role])
            {
                try { lifecycle.OnDisconnected(session); }
                catch (Exception e) { _log.LogError(e, "Falha ao desconectar {Session}", session); }
            }
            connections.Release(address);
            _running.TryRemove(session.Id, out _);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var session in _sessions.Values)
            session.Close("servidor desligando");
        await base.StopAsync(cancellationToken);
        // a saída de cada sessão (OnDisconnected) posta o Leave no mundo, que ainda está de pé
        await Task.WhenAll(_running.Values).WaitAsync(cancellationToken);
    }
}
