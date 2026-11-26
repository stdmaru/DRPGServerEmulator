using DRPG.Shared.Protocol;

namespace DRPG.Server.Network.Packets.Login;

/// <summary>✅ 0x05 (C2S 42 B, v322): u32 1, u32 0, usuário[20], 14 B finais.</summary>
public sealed record LoginHandshakeRequest(string Username) : IClientPacket<LoginHandshakeRequest>
{
    public static PacketLength Length => PacketLength.Exact(42);

    public static LoginHandshakeRequest Read(PacketReader r)
    {
        r.ReadU32();                            // 1
        r.ReadU32();                            // 0
        var username = r.ReadFixedString(20);
        r.Skip(14);
        return new(username);
    }
}
