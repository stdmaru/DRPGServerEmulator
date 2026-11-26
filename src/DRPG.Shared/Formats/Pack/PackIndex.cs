using System.Buffers.Binary;
using System.Text;

namespace DRPG.Shared.Formats.Pack;

/// <summary>One file inside a pack: name as stored, offset and size of its zlib stream in the <c>.pf</c>.</summary>
public readonly record struct PackEntry(string Name, long Offset, int Size);

/// <summary>
/// The <c>.hf</c> index: <c>u32 version, u32 field1, u32 field2, u32 payload_size, payload</c>, where
/// payload = encode(zlib(index)) and index = <c>u32 count, count × { u32 name_len, utf16le name, u64 offset, u32 size }</c>.
/// Every count, length and offset read from disk is checked: the file may be corrupt or tampered with.
/// </summary>
public sealed record PackIndex(uint Version, uint Field1, uint Field2, IReadOnlyList<PackEntry> Entries)
{
    private const int HeaderSize = 16;
    private const int MaxEntries = 1_000_000;
    private const int MaxNameChars = 1024;

    public static PackIndex Read(string hfPath, long pfLength)
    {
        var d = File.ReadAllBytes(hfPath);
        if (d.Length < HeaderSize) throw new PackFormatException($"{hfPath}: header too short");
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(d);
        uint field1 = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(4));
        uint field2 = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(8));
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(12));
        if (size > d.Length - HeaderSize) throw new PackFormatException($"{hfPath}: payload past the end");

        var index = PackCodec.Inflate(PackCodec.DecodeIndex(version, d.AsSpan(HeaderSize, (int)size).ToArray()));
        return new PackIndex(version, field1, field2, ParseEntries(index, pfLength, hfPath));
    }

    private static List<PackEntry> ParseEntries(byte[] index, long pfLength, string source)
    {
        var r = new SpanCursor(index, source);
        uint count = r.U32();
        if (count > MaxEntries) throw new PackFormatException($"{source}: {count} entries");
        var entries = new List<PackEntry>((int)count);
        for (int i = 0; i < count; i++)
        {
            uint chars = r.U32();
            if (chars is 0 or > MaxNameChars) throw new PackFormatException($"{source}: entry {i} name length {chars}");
            var name = Encoding.Unicode.GetString(r.Bytes(2 * (int)chars));
            ulong offset = r.U64();
            uint length = r.U32();
            if (length > int.MaxValue || offset > (ulong)pfLength || (ulong)pfLength - offset < length)
                throw new PackFormatException($"{source}: {name} points outside the .pf");
            entries.Add(new PackEntry(name, (long)offset, (int)length));
        }
        return entries;
    }

    /// <summary>Writes the index as version 0x30 (the original client's format) into <paramref name="hfPath"/>.</summary>
    public void WritePlain(string hfPath)
    {
        using var raw = new MemoryStream();
        Span<byte> word = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(word, (uint)Entries.Count);
        raw.Write(word[..4]);
        foreach (var e in Entries)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(word, (uint)e.Name.Length);
            raw.Write(word[..4]);
            raw.Write(Encoding.Unicode.GetBytes(e.Name));
            BinaryPrimitives.WriteUInt64LittleEndian(word, (ulong)e.Offset);
            BinaryPrimitives.WriteUInt32LittleEndian(word[8..], (uint)e.Size);
            raw.Write(word);
        }
        var payload = PackCodec.EncodePlainIndex(PackCodec.Deflate(raw.ToArray()));

        var header = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, PackVersion.Plain);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), Field1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), Field2);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)payload.Length);
        using var f = new FileStream(hfPath, FileMode.Create, FileAccess.Write, FileShare.None);
        f.Write(header);
        f.Write(payload);
        f.Flush(flushToDisk: true);
    }

    /// <summary>Bounds-checked little-endian reader over a decoded buffer.</summary>
    private ref struct SpanCursor(ReadOnlySpan<byte> data, string source)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _pos;

        public ReadOnlySpan<byte> Bytes(int n)
        {
            if (n < 0 || _data.Length - _pos < n) throw new PackFormatException($"{source}: index truncated");
            var s = _data.Slice(_pos, n);
            _pos += n;
            return s;
        }

        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
        public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Bytes(8));
    }
}
