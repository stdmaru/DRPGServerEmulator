using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Login
{
    /// <summary>0x05: resposta ao primeiro pacote do login.</summary>
    public sealed class HandshakePacket(string username) : ServerPacket
    {
        public override ushort Opcode => (ushort)LoginOpcode.Handshake;

        protected override void Write(PacketWriter w)
        {
            w.WriteI32(3);
            w.WriteI32(0x68);
            w.WriteFixedString(username, 20);
        }
    }
}

namespace DRPGServer.Network.Packets.Global
{
    /// <summary>0xE1: 1342 bytes depois do 0x64 (só o último byte é 1). ❓</summary>
    public sealed class UnknownE1Packet : ServerPacket
    {
        public override ushort Opcode => (ushort)GlobalOpcode.UnknownE1;

        protected override void Write(PacketWriter w)
        {
            w.WriteZeros(1341);
            w.WriteU8(1);
        }
    }

    /// <summary>0x65: vazio, depois do 0xE1.</summary>
    public sealed class Unknown65Packet : ServerPacket
    {
        public override ushort Opcode => (ushort)GlobalOpcode.Unknown65;
        protected override void Write(PacketWriter w) { }
    }

    /// <summary>0x7B: dados da enciclopédia: a primeira chave e 68 vezes a segunda (✅ 1104 bytes nas capturas).</summary>
    public sealed class EncyclopediaDataPacket(byte[] key1, byte[] key2) : ServerPacket
    {
        public override ushort Opcode => (ushort)GlobalOpcode.EncyclopediaData;

        protected override void Write(PacketWriter w)
        {
            w.WriteBytes(key1);
            for (int i = 0; i < 68; i++)
                w.WriteBytes(key2);
        }
    }

    /// <summary>0x7C: coleção da enciclopédia (chaves capturadas).</summary>
    public sealed class EncyclopediaCollectionPacket : ServerPacket
    {
        public override ushort Opcode => (ushort)GlobalOpcode.EncyclopediaCollection;

        protected override void Write(PacketWriter w)
        {
            ReadOnlySpan<byte> key1 = [0xA5, 0xEF, 0xC3, 0x17, 0x40, 0x2A, 0xD9, 0x90, 0xEF, 0x76, 0xB2, 0x13, 0xCF, 0x81, 0xA8, 0x12];
            ReadOnlySpan<byte> key2 = [0x33, 0x09, 0x00, 0x87, 0xC7, 0xBF, 0xBA, 0xC2, 0xFA, 0x66, 0x7C, 0x07, 0x31, 0xD9, 0xFE, 0x1B];
            w.WriteBytes(key1);
            w.WriteBytes([0x8F, 0x11, 0x4E, 0x2F, 0xCB, 0x44, 0xE0, 0x66, 0x89, 0x74, 0x1F, 0x78, 0xA0, 0x4E, 0xF1, 0xC9]);
            w.WriteBytes(key2);
            w.WriteBytes(key2);
        }
    }
}
