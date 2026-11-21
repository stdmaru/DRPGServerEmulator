using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Handlers;

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
/// Handler que roda no laço de recepção da própria sessão (em ordem, um pacote por vez).
/// Use para o que não toca o mundo: login, channel, acesso a banco.
/// </summary>
public interface ISessionPacketHandler
{
    ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct);
}

/// <summary>
/// Handler que roda na thread do mundo, junto com os ticks. Pode ler e mudar zonas, jogadores
/// e batalhas sem trava. Síncrono de propósito: nada de I/O aqui (use a fila de persistência).
/// </summary>
public interface IWorldPacketHandler
{
    void Handle(Session session, PacketReader packet);
}

/// <summary>Eventos de conexão por servidor (pacote de boas-vindas, limpeza ao desconectar).</summary>
public interface ISessionLifecycle
{
    ServerRole Role { get; }
    void OnConnected(Session session);
    void OnDisconnected(Session session);
}
