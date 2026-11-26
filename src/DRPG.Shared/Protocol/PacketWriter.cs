using System.Buffers.Binary;

namespace DRPG.Shared.Protocol;

/// <summary>Sequential little-endian writer over a growing buffer. Room for the header is reserved at the start.</summary>
public sealed class PacketWriter
{
    private byte[] _buffer;
    private int _length;

    public PacketWriter(int capacity = 256)
    {
        _buffer = new byte[Math.Max(capacity, PacketHeader.Length)];
        _length = PacketHeader.Length;
    }

    /// <summary>Position inside the payload (without the header).</summary>
    public int Position => _length - PacketHeader.Length;

    private Span<byte> Grow(int count)
    {
        int needed = _length + count;
        if (needed > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(needed, _buffer.Length * 2));
        var span = _buffer.AsSpan(_length, count);
        _length = needed;
        return span;
    }

    public void WriteU8(byte value) => Grow(1)[0] = value;
    public void WriteBool(bool value) => WriteU8(value ? (byte)1 : (byte)0);
    public void WriteI16(short value) => BinaryPrimitives.WriteInt16LittleEndian(Grow(2), value);
    public void WriteU16(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Grow(2), value);
    public void WriteI32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Grow(4), value);
    public void WriteU32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Grow(4), value);
    public void WriteI64(long value) => BinaryPrimitives.WriteInt64LittleEndian(Grow(8), value);
    public void WriteU64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Grow(8), value);
    public void WriteF64(double value) => BinaryPrimitives.WriteDoubleLittleEndian(Grow(8), value);
    public void WriteBytes(ReadOnlySpan<byte> value) => value.CopyTo(Grow(value.Length));
    public void WriteZeros(int count) => Grow(count).Clear();

    /// <summary>Fixed-length string. Truncated to leave at least one \0 at the end.</summary>
    public void WriteFixedString(string? value, int length)
    {
        var span = Grow(length);
        span.Clear();
        if (string.IsNullOrEmpty(value))
            return;
        int max = Math.Min(value.Length, length - 1);
        FixedString.Encoding.GetBytes(value.AsSpan(0, max), span);
    }

    /// <summary>
    /// Writes a zeroed block of <paramref name="length"/> bytes except for the given runs
    /// (offsets relative to the block start). For captured regions not understood yet.
    /// </summary>
    public void WriteSparse(int length, params ReadOnlySpan<(int Offset, byte[] Bytes)> runs)
    {
        var span = Grow(length);
        span.Clear();
        foreach (var (offset, bytes) in runs)
            bytes.CopyTo(span[offset..]);
    }

    /// <summary>Closes the packet: fills the header and returns the bytes ready to send.</summary>
    public byte[] ToFrame(ushort opcode, uint sequence = 0)
    {
        if (_length > PacketHeader.MaxPacketSize)
            throw new InvalidOperationException($"packet 0x{opcode:X} with {_length} bytes is over the {PacketHeader.MaxPacketSize} limit");
        var payload = _buffer.AsSpan(PacketHeader.Length, _length - PacketHeader.Length);
        new PacketHeader(opcode, (uint)_length, sequence, Crc16.Compute(payload)).Write(_buffer);
        return _buffer.AsSpan(0, _length).ToArray();
    }

    public ReadOnlySpan<byte> Payload => _buffer.AsSpan(PacketHeader.Length, _length - PacketHeader.Length);
}
