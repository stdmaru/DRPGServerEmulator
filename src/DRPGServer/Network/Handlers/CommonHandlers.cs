using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Packets.Login;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Handlers;

/// <summary>0xC4: keep-alive. O cliente manda 14 bytes zero; o oficial responde vazio (capturas).</summary>
[PacketHandler(ChannelOpcode.KeepAlive)]
[PacketHandler(MapOpcode.KeepAlive)]
[PacketHandler(GlobalOpcode.KeepAlive)]
public sealed class KeepAliveHandler : ISessionPacketHandler
{
    public ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        session.Send(new KeepAlivePacket((ushort)ChannelOpcode.KeepAlive));
        return ValueTask.CompletedTask;
    }
}

/// <summary>Opcodes que o cliente manda e que não pedem resposta.</summary>
[PacketHandler(MapOpcode.Hello)]                     // eco do 0x0E
[PacketHandler(GlobalOpcode.Hello)]
[PacketHandler(ChannelOpcode.MapConnectionConfirm)]  // usuário + personagem antes de ir para o mapa
[PacketHandler(ChannelOpcode.ClientHardwareInfo)]
[PacketHandler(GlobalOpcode.CharacterJoin)]
[PacketHandler(MapOpcode.SingleDigimonInfo)]         // ❓ o pedido C2S ainda não foi entendido
public sealed class IgnoreHandler : ISessionPacketHandler
{
    public ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct) => ValueTask.CompletedTask;
}

/// <summary>0x05 (login): primeiro contato; responde com o usuário.</summary>
[PacketHandler(LoginOpcode.Handshake)]
public sealed class LoginHandshakeHandler : ISessionPacketHandler
{
    public ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        packet.ReadU32();                       // 1
        packet.ReadU32();                       // 0
        var username = packet.ReadFixedString(20);
        session.Send(new HandshakePacket(username));
        return ValueTask.CompletedTask;
    }
}

/// <summary>Boas-vindas (0x0E) no mapa e no global; ao desconectar, tira do mundo e solta a conta.</summary>
public sealed class MapLifecycle(GameWorld world, PlayerSystem players, AccountRegistry accounts) : ISessionLifecycle
{
    public ServerRole Role => ServerRole.Map;

    public void OnConnected(Session session) => session.Send(new HelloPacket((ushort)MapOpcode.Hello));

    public void OnDisconnected(Session session)
    {
        world.Post(() => players.Leave(session));
        accounts.Release(session);
    }
}

public sealed class GlobalLifecycle(AccountRegistry accounts) : ISessionLifecycle
{
    public ServerRole Role => ServerRole.Global;
    public void OnConnected(Session session) => session.Send(new HelloPacket((ushort)GlobalOpcode.Hello));
    public void OnDisconnected(Session session) => accounts.Release(session);
}

public sealed class ChannelLifecycle(AccountRegistry accounts) : ISessionLifecycle
{
    public ServerRole Role => ServerRole.Channel;
    public void OnConnected(Session session) { }
    public void OnDisconnected(Session session) => accounts.Release(session);
}
