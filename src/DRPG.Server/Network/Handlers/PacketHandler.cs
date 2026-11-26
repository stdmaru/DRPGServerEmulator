using System.Reflection;
using DRPG.Server.Game.Entities;
using DRPG.Shared.Protocol;

namespace DRPG.Server.Network.Handlers;

/// <summary>Liga um handler a um opcode de um servidor. Pode repetir (mesmo handler em vários servidores).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class PacketHandlerAttribute : Attribute
{
    public PacketHandlerAttribute(LoginOpcode opcode) : this(ServerRole.Login, (ushort)opcode) { }
    public PacketHandlerAttribute(ChannelOpcode opcode) : this(ServerRole.Channel, (ushort)opcode) { }
    public PacketHandlerAttribute(MapOpcode opcode) : this(ServerRole.Map, (ushort)opcode) { }
    public PacketHandlerAttribute(GlobalOpcode opcode) : this(ServerRole.Global, (ushort)opcode) { }

    private PacketHandlerAttribute(ServerRole role, ushort opcode)
    {
        Role = role;
        Opcode = opcode;
    }

    public ServerRole Role { get; }
    public ushort Opcode { get; }
}

/// <summary>
/// Estados do jogador em que um <see cref="PlayerHandler{TRequest}"/> aceita o pacote. Obrigatório em todo handler
/// de mapa: fora deles, o pacote vai para <see cref="PlayerHandler{TRequest}.OnRejected"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresStateAttribute(PlayerStates states) : Attribute
{
    public PlayerStates States { get; } = states;
}

/// <summary>Handler que roda no laço de recepção da própria sessão (em ordem, um pacote por vez).</summary>
public interface ISessionPacketHandler
{
    /// <summary>Lê o payload (lança <see cref="PacketFormatException"/>) e trata o pacote.</summary>
    ValueTask HandleAsync(Session session, ReadOnlyMemory<byte> payload, CancellationToken ct);
}

/// <summary>Handler que roda na thread do mundo, junto com os ticks.</summary>
public interface IWorldPacketHandler
{
    /// <summary>
    /// Lê o payload na thread da sessão (lança <see cref="PacketFormatException"/>) e devolve o trabalho a postar
    /// no mundo. Pacote malformado nunca chega à thread do mundo.
    /// </summary>
    Action Prepare(Session session, ReadOnlyMemory<byte> payload);
}

/// <summary>Eventos de conexão por servidor (pacote de boas-vindas, limpeza ao desconectar).</summary>
public interface ISessionLifecycle
{
    ServerRole Role { get; }
    void OnConnected(Session session);
    void OnDisconnected(Session session);
}

/// <summary>
/// Base dos handlers que rodam no laço da sessão: login, channel, global e o que vem antes de o jogador entrar no
/// mapa. Async; podem ir ao banco. Não tocam o estado do mundo (para isso, <c>GameWorld.Post</c>).
/// </summary>
public abstract class SessionHandler<TRequest> : ISessionPacketHandler where TRequest : IClientPacket<TRequest>
{
    public ValueTask HandleAsync(Session session, ReadOnlyMemory<byte> payload, CancellationToken ct) =>
        HandleAsync(session, ClientPacket.Parse<TRequest>(payload), ct);

    protected abstract ValueTask HandleAsync(Session session, TRequest request, CancellationToken ct);
}

/// <summary>
/// Base dos handlers de sessão que exigem a conta autenticada (channel depois do 0x04, global depois do 0x64).
/// Pacote antes da autenticação fecha a conexão.
/// </summary>
public abstract class AccountHandler<TRequest> : SessionHandler<TRequest> where TRequest : IClientPacket<TRequest>
{
    protected sealed override ValueTask HandleAsync(Session session, TRequest request, CancellationToken ct)
    {
        if (session.Account is not { } account)
        {
            session.Close("pacote antes da autenticação");
            return ValueTask.CompletedTask;
        }
        return HandleAsync(session, account, request, ct);
    }

    protected abstract ValueTask HandleAsync(Session session, AuthenticatedAccount account, TRequest request, CancellationToken ct);
}

/// <summary>
/// Base de todo handler de jogo no mapa. Roda na thread do mundo, síncrono de propósito: nada de I/O aqui
/// (use a fila de persistência). Confere que o jogador está no mundo e no estado declarado em
/// <see cref="RequiresStateAttribute"/> antes de chamar <see cref="Handle(Player, TRequest)"/>.
/// </summary>
public abstract class PlayerHandler<TRequest> : IWorldPacketHandler where TRequest : IClientPacket<TRequest>
{
    private readonly PlayerStates _allowed;

    protected PlayerHandler()
    {
        _allowed = GetType().GetCustomAttribute<RequiresStateAttribute>()?.States
            ?? throw new InvalidOperationException($"{GetType().Name} não declara [RequiresState]");
    }

    public Action Prepare(Session session, ReadOnlyMemory<byte> payload)
    {
        var request = ClientPacket.Parse<TRequest>(payload);
        return () => Handle(session, request);
    }

    private void Handle(Session session, TRequest request)
    {
        if (session.Player is not { } player)
        {
            OnNotInWorld(session, request);
            return;
        }
        if ((player.State & _allowed) == 0)
        {
            OnRejected(player, request);
            return;
        }
        Handle(player, request);
    }

    protected abstract void Handle(Player player, TRequest request);

    /// <summary>Jogador fora do estado declarado. Padrão: ignora (o oficial não responde).</summary>
    protected virtual void OnRejected(Player player, TRequest request) { }

    /// <summary>Pacote de jogo antes de o personagem entrar no mundo. Padrão: fecha a conexão.</summary>
    protected virtual void OnNotInWorld(Session session, TRequest request) =>
        session.Close("pacote de jogo antes de entrar no mapa");
}
