using DRPG.Shared.Packets.Login;
using DRPG.Shared.Protocol;

namespace DRPG.Server.Network.Handlers;

/// <summary>0x05 (login): primeiro contato; responde com o usuário.</summary>
[PacketHandler(LoginOpcode.Handshake)]
public sealed class LoginHandshakeHandler : SessionHandler<LoginHandshakeRequest>
{
    protected override ValueTask HandleAsync(Session session, LoginHandshakeRequest request, CancellationToken ct)
    {
        session.Send(new LoginHandshakeReply(request.Username));
        return ValueTask.CompletedTask;
    }
}
