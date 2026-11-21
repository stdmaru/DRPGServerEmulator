namespace DRPGServer.Network.Protocol;

/// <summary>
/// Pacote enviado pelo servidor. Subclasses só descrevem o layout em <see cref="Write"/>;
/// a serialização acontece no envio, na thread de quem chama (no mapa: a thread do mundo),
/// então o pacote reflete o estado daquele instante.
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

    /// <summary>Só o payload (testes e comparação com capturas).</summary>
    public byte[] EncodePayload()
    {
        var writer = new PacketWriter();
        Write(writer);
        return writer.Payload.ToArray();
    }

    public override string ToString() => $"{GetType().Name}(0x{Opcode:X})";
}
