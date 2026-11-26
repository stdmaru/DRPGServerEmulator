using System.Buffers.Binary;
using System.Text;

namespace DRPG.Shared.Protocol;

public sealed class PacketFormatException(string message) : Exception(message);

/// <summary>Sequential little-endian reader over a payload. Throws <see cref="PacketFormatException"/> when data runs out.</summary>
public sealed class PacketReader(ReadOnlyMemory<byte> payload)
{
    private int _position;

    public int Position => _position;
    public int Length => payload.Length;
    public int Remaining => payload.Length - _position;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > Remaining)
            throw new PacketFormatException($"read of {count} bytes at position {_position}, {Remaining} left");
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

    /// <summary>Fixed-length, \0-terminated string (bytes after the \0 are garbage and ignored).</summary>
    public string ReadFixedString(int length)
    {
        var span = Take(length);
        int end = span.IndexOf((byte)0);
        return FixedString.Encoding.GetString(end < 0 ? span : span[..end]);
    }
}

public static class FixedString
{
    /// <summary>Latin-1: byte-for-byte round trip, nothing the client sends is lost.</summary>
    public static readonly Encoding Encoding = Encoding.Latin1;
}
