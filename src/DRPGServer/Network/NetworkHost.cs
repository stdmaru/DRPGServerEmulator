using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DRPGServer.Configuration;
using DRPGServer.Network.Handlers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DRPGServer.Network;

/// <summary>Abre os quatro servidores TCP e roda uma sessão por conexão.</summary>
public sealed class NetworkHost(
    IOptions<ServersOptions> options,
    PacketDispatcher dispatcher,
    IEnumerable<ISessionLifecycle> lifecycles,
    PacketTrace trace,
    ILoggerFactory loggers) : BackgroundService
{
    private readonly ILogger _log = loggers.CreateLogger<NetworkHost>();
    private readonly ConcurrentDictionary<long, Session> _sessions = new();
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
        await Task.WhenAll(listeners.Select(l => ListenAsync(l.Role, l.Endpoint, stopping)));
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
            socket.NoDelay = true;
            _ = RunSessionAsync(new Session(socket, role, _log, trace), stopping);
        }
    }

    private async Task RunSessionAsync(Session session, CancellationToken stopping)
    {
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
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var session in _sessions.Values)
            session.Close("servidor desligando");
        await base.StopAsync(cancellationToken);
    }
}
