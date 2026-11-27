using DRPG.Shared.Protocol;

namespace DRPG.Server.Network.Packets;

/// <summary>Sends a <see cref="ITwoWayPacket{TSelf}"/> from <c>DRPG.Shared</c> through <see cref="Session.Send"/>.</summary>
public sealed class TwoWayServerPacket<T>(T packet) : ServerPacket where T : ITwoWayPacket<T>
{
    public override ushort Opcode => T.Opcode;

    protected override void Write(PacketWriter w) => packet.Write(w);

    public override string ToString() => $"{typeof(T).Name}(0x{Opcode:X})";
}

public static class TwoWayServerPacket
{
    /// <summary>Wraps a two-way packet to be queued as a <see cref="ServerPacket"/> (packet sequences, tests).</summary>
    public static ServerPacket Of<T>(T packet) where T : ITwoWayPacket<T> => new TwoWayServerPacket<T>(packet);
}
