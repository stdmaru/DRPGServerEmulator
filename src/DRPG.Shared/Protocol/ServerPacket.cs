namespace DRPG.Shared.Protocol;

/// <summary>
/// An outgoing packet. Subclasses only describe the layout in <see cref="Write"/>;
/// serialization happens when it is sent, on the caller's thread (on the map server: the world thread),
/// so the packet reflects the state at that moment.
/// </summary>
public abstract class ServerPacket
{
    public abstract ushort Opcode { get; }

    protected abstract void Write(PacketWriter w);

    public byte[] Encode()
    {
        var writer = new PacketWriter();
        Write(writer);
        return writer.ToFrame(Opcode);
    }

    /// <summary>Payload only (tests and comparison with captures).</summary>
    public byte[] EncodePayload()
    {
        var writer = new PacketWriter();
        Write(writer);
        return writer.Payload.ToArray();
    }

    public override string ToString() => $"{GetType().Name}(0x{Opcode:X})";
}
