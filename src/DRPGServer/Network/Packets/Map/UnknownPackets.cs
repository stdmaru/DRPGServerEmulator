using DRPGServer.Network.Protocol;

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

/// <summary>0xE6: 32 bytes capturados (❓ chave).</summary>
public sealed class UnknownE6Packet : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.UnknownE6;

    protected override void Write(PacketWriter w) => w.WriteBytes([
        0x42, 0xB0, 0xAE, 0x11, 0x87, 0x90, 0x92, 0x67, 0xE6, 0xD6, 0x21, 0x1A, 0x2A, 0x7F, 0x98, 0xEA,
        0xAC, 0x9B, 0xE3, 0xBA, 0x9E, 0x0C, 0x7C, 0x21, 0xCE, 0x1E, 0xD5, 0x34, 0xD3, 0xE1, 0xD6, 0x89]);
}

/// <summary>0xF0: resposta ao C2S 0xF0.</summary>
public sealed class UnknownF0Packet : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.UnknownF0;
    protected override void Write(PacketWriter w) => w.WriteSparse(28, (0, [0x02, 0x01]));
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
