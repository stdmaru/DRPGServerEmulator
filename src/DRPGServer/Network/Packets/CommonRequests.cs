using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets;

/// <summary>✅ 0xC4 no channel e no global (C2S 14 B, v322): ping do cliente, 14 B zerados.</summary>
public sealed record KeepAliveRequest : IClientPacket<KeepAliveRequest>
{
    public static PacketLength Length => PacketLength.Exact(14);

    public static KeepAliveRequest Read(PacketReader r)
    {
        r.Skip(14);
        return new();
    }
}
