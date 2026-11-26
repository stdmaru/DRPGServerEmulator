using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using DRPG.Server.Game.World;
using DRPG.Shared.Protocol;
using DRPG.Server.Network.Traffic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DRPG.Server.Network.Handlers;

/// <summary>Entrega cada pacote ao handler do (servidor, opcode): direto na sessão ou na fila do mundo.</summary>
public sealed class PacketDispatcher
{
    private readonly FrozenDictionary<(ServerRole, ushort), object> _handlers;
    private readonly GameWorld _world;
    private readonly ILogger<PacketDispatcher> _log;
    private readonly ConcurrentDictionary<(ServerRole, ushort), bool> _reportedUnknown = new();

    public PacketDispatcher(IServiceProvider services, GameWorld world, ILogger<PacketDispatcher> log)
    {
        _world = world;
        _log = log;
        var map = new Dictionary<(ServerRole, ushort), object>();
        foreach (var (type, attr) in PacketHandlerRegistration.Discover())
        {
            var key = (attr.Role, attr.Opcode);
            if (!map.TryAdd(key, services.GetRequiredService(type)))
                throw new InvalidOperationException($"dois handlers para {attr.Role} {OpcodeNames.Of(attr.Role, attr.Opcode)}");
        }
        _handlers = map.ToFrozenDictionary();
    }

    public int Count => _handlers.Count;

    public async ValueTask DispatchAsync(Session session, IncomingFrame frame, CancellationToken ct)
    {
        switch (session.Traffic.Admit(frame.Opcode))
        {
            case TrafficVerdict.Close:
                Cut(session, "pacotes por segundo acima do teto");
                return;
            case TrafficVerdict.Drop:
                _log.LogDebug("{Opcode} de {Session} acima do teto do opcode: ignorado", OpcodeNames.Of(session.Role, frame.Opcode), session);
                return;
        }

        if (!_handlers.TryGetValue((session.Role, frame.Opcode), out var handler))
        {
            var level = _reportedUnknown.TryAdd((session.Role, frame.Opcode), true) ? LogLevel.Information : LogLevel.Debug;
            var name = OpcodeNames.Of(session.Role, frame.Opcode);
            _log.Log(level, "Sem handler para {Opcode} em {Role} ({Size}B)", name, session.Role, frame.Header.Size);
            if (session.Role == ServerRole.Map && session.Player is { IsAdmin: true } admin)
                admin.SendDebug($"Unhandled {session.Role} opcode {name} ({frame.Header.Size}B)");
            return;
        }

        switch (handler)
        {
            case ISessionPacketHandler sessionHandler:
                try
                {
                    await sessionHandler.HandleAsync(session, frame.Payload, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception e)
                {
                    Report(session, frame, e);
                }
                break;

            case IWorldPacketHandler worldHandler:
                Action work;
                try
                {
                    work = worldHandler.Prepare(session, frame.Payload);
                }
                catch (Exception e)
                {
                    Report(session, frame, e);
                    break;
                }
                if (!session.Traffic.TryBeginWorldWork())
                {
                    Cut(session, "fila do mundo cheia");
                    break;
                }
                _world.Post(() =>
                {
                    try
                    {
                        if (!session.IsClosed) work();
                    }
                    catch (Exception e)
                    {
                        Report(session, frame, e);
                    }
                    finally
                    {
                        session.Traffic.EndWorldWork();
                    }
                });
                break;
        }
    }

    /// <summary>Fecha a sessão por limite de recurso, com log de quem foi cortado.</summary>
    private void Cut(Session session, string reason)
    {
        _log.LogWarning("Sessão {Session} cortada: {Reason}", session, reason);
        session.Close(reason);
    }

    private void Report(Session session, IncomingFrame frame, Exception e)
    {
        var opcode = OpcodeNames.Of(session.Role, frame.Opcode);
        if (e is PacketFormatException)
        {
            // o cliente oficial nunca manda pacote fora do layout: é cliente modificado ou fluxo corrompido
            _log.LogWarning("Pacote {Opcode} malformado de {Session}: {Message}", opcode, session, e.Message);
            session.Close($"pacote {opcode} malformado");
        }
        else
            _log.LogError(e, "Erro tratando {Opcode} de {Session}", opcode, session);
    }
}

public static class PacketHandlerRegistration
{
    public static IEnumerable<(Type Type, PacketHandlerAttribute Attribute)> Discover() =>
        from type in typeof(PacketHandlerRegistration).Assembly.GetTypes()
        where type is { IsClass: true, IsAbstract: false }
        from attr in type.GetCustomAttributes<PacketHandlerAttribute>()
        select (type, attr);

    /// <summary>Registra todos os handlers (classes com [PacketHandler]) como singletons.</summary>
    public static IServiceCollection AddPacketHandlers(this IServiceCollection services)
    {
        foreach (var type in Discover().Select(d => d.Type).Distinct())
        {
            if (!typeof(ISessionPacketHandler).IsAssignableFrom(type) && !typeof(IWorldPacketHandler).IsAssignableFrom(type))
                throw new InvalidOperationException($"{type.Name} tem [PacketHandler] mas não implementa ISessionPacketHandler nem IWorldPacketHandler");
            services.AddSingleton(type);
        }
        foreach (var type in typeof(PacketHandlerRegistration).Assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ISessionLifecycle).IsAssignableFrom(t)))
            services.AddSingleton(typeof(ISessionLifecycle), type);
        services.AddSingleton<PacketDispatcher>();
        return services;
    }
}
