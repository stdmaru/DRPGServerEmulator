namespace DRPG.Shared.Protocol;

/// <summary>
/// A packet that both sides of a connection read and write with one layout: <see cref="IClientPacket{TSelf}.Read"/>
/// and <see cref="Write"/> must round-trip (docs/arquitetura/shared.md).
/// </summary>
public interface ITwoWayPacket<TSelf> : IClientPacket<TSelf> where TSelf : ITwoWayPacket<TSelf>
{
    static abstract ushort Opcode { get; }

    void Write(PacketWriter writer);
}

public static class TwoWayPacket
{
    /// <summary>Whole frame (header + payload) ready to send.</summary>
    public static byte[] Encode<T>(T packet) where T : ITwoWayPacket<T>
    {
        var writer = new PacketWriter();
        packet.Write(writer);
        return writer.ToFrame(T.Opcode);
    }

    /// <summary>Payload only (tests).</summary>
    public static byte[] EncodePayload<T>(T packet) where T : ITwoWayPacket<T>
    {
        var writer = new PacketWriter();
        packet.Write(writer);
        return writer.Payload.ToArray();
    }
}
