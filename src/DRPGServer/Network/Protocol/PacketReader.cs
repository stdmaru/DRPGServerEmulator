using System.Buffers.Binary;
using System.Text;

namespace DRPGServer.Network.Protocol;

public sealed class PacketFormatException(string message) : Exception(message);

/// <summary>Leitura sequencial do payload, little-endian. Lança <see cref="PacketFormatException"/> se faltar dado.</summary>
public sealed class PacketReader(ReadOnlyMemory<byte> payload)
{
    private int _position;

    public int Position => _position;
    public int Length => payload.Length;
    public int Remaining => payload.Length - _position;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > Remaining)
            throw new PacketFormatException($"leitura de {count} bytes na posição {_position}, restam {Remaining}");
        var span = payload.Span.Slice(_position, count);
        _position += count;
        return span;
    }

    public byte ReadU8() => Take(1)[0];
    public bool ReadBool() => ReadU8() != 0;
    public short ReadI16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public ushort ReadU16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public int ReadI32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public uint ReadU32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public long ReadI64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
    public double ReadF64() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));
    public byte[] ReadBytes(int count) => Take(count).ToArray();
    public void Skip(int count) => Take(count);

    /// <summary>String de tamanho fixo terminada em \0 (bytes depois do \0 são lixo e são ignorados).</summary>
    public string ReadFixedString(int length)
    {
        var span = Take(length);
        int end = span.IndexOf((byte)0);
        return FixedString.Encoding.GetString(end < 0 ? span : span[..end]);
    }
}

public static class FixedString
{
    /// <summary>Latin-1: ida e volta byte a byte, sem perder nada que o cliente mande.</summary>
    public static readonly Encoding Encoding = Encoding.Latin1;
}
