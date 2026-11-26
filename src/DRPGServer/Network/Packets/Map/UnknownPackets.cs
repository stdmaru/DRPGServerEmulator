using DRPG.Shared.Protocol;

namespace DRPGServer.Network.Packets.Map;

// Pacotes que o servidor oficial manda na entrada do mapa e cujo conteúdo ainda não foi decodificado.
// O cliente precisa deles para entrar. Conteúdo copiado das capturas (v242).

/// <summary>0x05: 164 bytes zero.</summary>
public sealed class Unknown05Packet : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.Unknown05;
    protected override void Write(PacketWriter w) => w.WriteZeros(164);
}

/// <summary>0x73: 280 bytes; 192..199 parecem handles do oficial.</summary>
public sealed class Unknown73Packet : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.Unknown73;
    protected override void Write(PacketWriter w) => w.WriteSparse(280, (192, [0xFA, 0x13, 0x00, 0x00, 0x50, 0x5B, 0x55, 0x68]));
}

/// <summary>0x74: 60 bytes zero.</summary>
public sealed class Unknown74Packet : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.Unknown74;
    protected override void Write(PacketWriter w) => w.WriteZeros(60);
}

/// <summary>0x7E: mandado 7 vezes com o índice 0x29..0x2F. ❓ páginas de alguma lista.</summary>
public sealed class Unknown7EPacket(int index) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.Unknown7E;

    protected override void Write(PacketWriter w)
    {
        w.WriteI32(index);
        w.WriteZeros(1936);                     // ✅ 1940 bytes nas capturas (o protótipo mandava 1820)
    }
}

/// <summary>0x7F: mandado 7 vezes com o índice 0x3D..0x43.</summary>
public sealed class Unknown7FPacket(int index) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.Unknown7F;

    protected override void Write(PacketWriter w)
    {
        w.WriteI32(index);
        w.WriteZeros(2704);
    }
}

/// <summary>0x121: resposta ao C2S 0x121 (template capturado de 10304 bytes).</summary>
public sealed class Unknown121Packet : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.Unknown121;
    protected override void Write(PacketWriter w) => w.WriteBytes(PayloadTemplates.Get("map_0x121"));
}

/// <summary>0x152: entrada no mapa (template capturado de 7048 bytes).</summary>
public sealed class Unknown152Packet : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.Unknown152;
    protected override void Write(PacketWriter w) => w.WriteBytes(PayloadTemplates.Get("map_0x152"));
}
