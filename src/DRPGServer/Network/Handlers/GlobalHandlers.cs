using DRPGServer.Network.Packets.Global;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging;

namespace DRPGServer.Network.Handlers;

/// <summary>0x64: primeiro pacote no global (usuário + personagem).</summary>
[PacketHandler(GlobalOpcode.UserAuth)]
public sealed class GlobalUserAuthHandler(AccountRegistry accounts, ILogger<GlobalUserAuthHandler> log) : ISessionPacketHandler
{
    public ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        var username = packet.ReadFixedString(21);
        packet.ReadFixedString(21);             // personagem
        if (session.Account is null && accounts.Claim(username, session) is null)
        {
            log.LogWarning("{Session}: {Username} no global sem login no channel", session, username);
            session.Close("sem autenticação no channel");
            return ValueTask.CompletedTask;
        }
        session.Send(new UnknownE1Packet());
        session.Send(new Unknown65Packet());
        return ValueTask.CompletedTask;
    }
}

/// <summary>0x7B: o cliente manda duas chaves e recebe a enciclopédia montada com elas.</summary>
[PacketHandler(GlobalOpcode.EncyclopediaData)]
public sealed class EncyclopediaDataHandler : ISessionPacketHandler
{
    public ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        var key1 = packet.ReadBytes(16);
        var key2 = packet.ReadBytes(16);
        session.Send(new EncyclopediaDataPacket(key1, key2));
        return ValueTask.CompletedTask;
    }
}

/// <summary>0x7C: coleção da enciclopédia.</summary>
[PacketHandler(GlobalOpcode.EncyclopediaCollection)]
public sealed class EncyclopediaCollectionHandler : ISessionPacketHandler
{
    public ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        session.Send(new EncyclopediaCollectionPacket());
        return ValueTask.CompletedTask;
    }
}
