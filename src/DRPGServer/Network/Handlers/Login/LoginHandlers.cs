using DRPGServer.Network.Packets.Login;

namespace DRPGServer.Network.Handlers;

/// <summary>0x05 (login): primeiro contato; responde com o usuário.</summary>
[PacketHandler(LoginOpcode.Handshake)]
public sealed class LoginHandshakeHandler : SessionHandler<LoginHandshakeRequest>
{
    protected override ValueTask HandleAsync(Session session, LoginHandshakeRequest request, CancellationToken ct)
    {
        session.Send(new HandshakePacket(request.Username));
        return ValueTask.CompletedTask;
    }
}
