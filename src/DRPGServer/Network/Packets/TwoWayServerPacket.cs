using DRPG.Shared.Protocol;

namespace DRPGServer.Network.Packets;

/// <summary>Sends a <see cref="ITwoWayPacket{TSelf}"/> from <c>DRPG.Shared</c> through <see cref="Session.Send"/>.</summary>
public sealed class TwoWayServerPacket<T>(T packet) : ServerPacket where T : ITwoWayPacket<T>
{
    public override ushort Opcode => T.Opcode;

    protected override void Write(PacketWriter w) => packet.Write(w);

    public override string ToString() => $"{typeof(T).Name}(0x{Opcode:X})";
}
