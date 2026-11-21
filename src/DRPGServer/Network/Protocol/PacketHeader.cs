using System.Buffers.Binary;

namespace DRPGServer.Network.Protocol;

/// <summary>
/// Cabeçalho de 14 bytes de todo pacote (ver docs/protocolo/cabecalho.md).
/// </summary>
public readonly record struct PacketHeader(ushort Opcode, uint Size, uint Sequence, ushort Crc)
{
    public const int Length = 14;
    public const ushort Signature = 0x00CC;

    /// <summary>O dispatcher do cliente rejeita pacotes maiores que isso.</summary>
    public const int MaxPacketSize = 0x3080;

    public int PayloadLength => (int)Size - Length;

    public static PacketHeader Read(ReadOnlySpan<byte> src) => new(
        BinaryPrimitives.ReadUInt16LittleEndian(src[2..]),
        BinaryPrimitives.ReadUInt32LittleEndian(src[4..]),
        BinaryPrimitives.ReadUInt32LittleEndian(src[8..]),
        BinaryPrimitives.ReadUInt16LittleEndian(src[12..]));

    public void Write(Span<byte> dst)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(dst, Signature);
        BinaryPrimitives.WriteUInt16LittleEndian(dst[2..], Opcode);
        BinaryPrimitives.WriteUInt32LittleEndian(dst[4..], Size);
        BinaryPrimitives.WriteUInt32LittleEndian(dst[8..], Sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(dst[12..], Crc);
    }
}
