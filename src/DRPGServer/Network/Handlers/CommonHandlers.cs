using DRPGServer.Network.Packets;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Handlers;

/// <summary>
/// 0xC4 no channel e no global: o cliente pinga (14 bytes zero) e o oficial responde vazio.
/// ✅ v322: no mapa é o contrário: o servidor pinga a cada ~5,2 s (Zone) e não responde ao 0xC4 do cliente.
/// </summary>
[PacketHandler(ChannelOpcode.KeepAlive)]
[PacketHandler(GlobalOpcode.KeepAlive)]
public sealed class KeepAliveHandler : SessionHandler<KeepAliveRequest>
{
    protected override ValueTask HandleAsync(Session session, KeepAliveRequest request, CancellationToken ct)
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
[PacketHandler(MapOpcode.KeepAlive)]                 // resposta do cliente ao ping do servidor
public sealed class IgnoreHandler : SessionHandler<IgnoredPayload>
{
    protected override ValueTask HandleAsync(Session session, IgnoredPayload request, CancellationToken ct) => ValueTask.CompletedTask;
}
