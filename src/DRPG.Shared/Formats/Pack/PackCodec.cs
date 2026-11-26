using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace DRPG.Shared.Formats.Pack;

/// <summary>
/// Encoding stages of the original client's packs. Names and order follow the reference implementation
/// (work/tools/drpg_pack.py); format in docs/cliente/arquivos-de-dados.md.
/// </summary>
internal static class PackCodec
{
    private const int KeyWords = 16;

    /// <summary>Decodes a <c>.hf</c> index payload for the header <paramref name="version"/>.</summary>
    public static byte[] DecodeIndex(uint version, byte[] payload) => version switch
    {
        PackVersion.Plain => Xmask(Revnot(payload)),
        PackVersion.Keyed => DecodeKeyedIndex(payload),
        PackVersion.Legacy => OldDecode(payload, flags: 5),
        _ => throw new PackFormatException($"unsupported .hf version 0x{version:X}"),
    };

    /// <summary>Encodes an index payload as version 0x30, the one the original client writes.</summary>
    public static byte[] EncodePlainIndex(byte[] data) => Revnot(Xmask(data));

    private static byte[] DecodeKeyedIndex(byte[] payload)
    {
        var (key, data, _) = ReadKeyed(Xmask(payload));
        return Revnot(XorKey(key, data));
    }

    public static byte[] Inflate(ReadOnlySpan<byte> data)
    {
        try
        {
            using var input = new ZLibStream(new MemoryStream(data.ToArray()), CompressionMode.Decompress);
            using var output = new MemoryStream();
            input.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException e)
        {
            throw new PackFormatException("corrupt zlib stream", e);
        }
    }

    public static byte[] Deflate(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data);
        return output.ToArray();
    }

    private static uint[] Words(byte[] b)
    {
        var w = new uint[b.Length / 4];
        for (int i = 0; i < w.Length; i++) w[i] = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4 * i));
        return w;
    }

    /// <summary>Writes <paramref name="w"/> back over a copy of <paramref name="source"/>, keeping its trailing bytes.</summary>
    private static byte[] Pack(uint[] w, byte[] source)
    {
        var result = new byte[source.Length];
        for (int i = 0; i < w.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4 * i), w[i]);
        source.AsSpan(w.Length * 4).CopyTo(result.AsSpan(w.Length * 4));
        return result;
    }

    /// <summary>Reverses the dword order and applies NOT.</summary>
    public static byte[] Revnot(byte[] b)
    {
        var w = Words(b);
        var r = new uint[w.Length];
        for (int i = 0; i < w.Length; i++) r[i] = ~w[w.Length - 1 - i];
        return Pack(r, b);
    }

    /// <summary>Alternating XOR with 0xFFFF0000 / 0x0000FFFF.</summary>
    public static byte[] Xmask(byte[] b)
    {
        var w = Words(b);
        for (int i = 0; i < w.Length; i++) w[i] ^= (i & 1) == 0 ? 0xFFFF0000u : 0x0000FFFFu;
        return Pack(w, b);
    }

    /// <summary>Rotates dword i right by 1..31 (cyclic).</summary>
    public static byte[] RotRight(byte[] b)
    {
        var w = Words(b);
        int s = 1;
        for (int i = 0; i < w.Length; i++)
        {
            if (s >= 32) s = 1;
            w[i] = (w[i] >> s) | (w[i] << (32 - s));
            s++;
        }
        return Pack(w, b);
    }

    /// <summary>XOR of each dword with key[i % 16]; a partial tail goes in the high bytes of one more dword.</summary>
    public static byte[] XorKey(uint[] key, byte[] data)
    {
        var w = Words(data);
        int n = w.Length;
        for (int i = 0; i < n; i++) w[i] ^= key[i % key.Length];
        var result = new byte[data.Length];
        for (int i = 0; i < n; i++) BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4 * i), w[i]);
        int r = data.Length % 4;
        if (r != 0)
        {
            var tmp = new byte[4];
            data.AsSpan(n * 4).CopyTo(tmp.AsSpan(4 - r));
            uint t = BinaryPrimitives.ReadUInt32LittleEndian(tmp) ^ key[n % key.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(tmp, t);
            tmp.AsSpan(4 - r).CopyTo(result.AsSpan(n * 4));
        }
        return result;
    }

    /// <summary>Per 16-byte block, swaps blk[k &amp; 15] with blk[k &gt;&gt; 4] for each key byte (decode order).</summary>
    public static byte[] NibSwap(uint[] key, byte[] data)
    {
        var kb = new byte[key.Length * 4];
        for (int i = 0; i < key.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(kb.AsSpan(4 * i), key[i]);
        var result = (byte[])data.Clone();
        for (int o = 0; o + 16 <= data.Length; o += 16)
            foreach (var k in kb)
            {
                int i = o + (k & 15), j = o + (k >> 4);
                (result[i], result[j]) = (result[j], result[i]);
            }
        return result;
    }

    /// <summary>u32 n, n × u32 key, u32 length, data.</summary>
    public static (uint[] Key, byte[] Data, int Used) ReadKeyed(byte[] b)
    {
        if (b.Length < 4) throw new PackFormatException("keyed block too short");
        uint nk = BinaryPrimitives.ReadUInt32LittleEndian(b);
        if (nk is 0 or > 1024 || 8 + 4L * nk > b.Length) throw new PackFormatException("keyed block: invalid key");
        var key = new uint[nk];
        for (int i = 0; i < nk; i++) key[i] = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4 + 4 * i));
        int p = 4 + 4 * (int)nk;
        uint nd = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p));
        p += 4;
        if (p + (long)nd > b.Length) throw new PackFormatException("keyed block: data past the end");
        return (key, b.AsSpan(p, (int)nd).ToArray(), p + (int)nd);
    }

    /// <summary>Old codec of the encrypted table <c>.dat</c> files (flags 0xFF).</summary>
    public static byte[] OldDecode(byte[] b, int flags = 0xFF)
    {
        if ((flags & 0x10) != 0)
        {
            var (key, data, used) = ReadKeyed(b);
            if (used != b.Length) throw new PackFormatException("trailing bytes after keyed block");
            b = NibSwap(key, data);
        }
        if ((flags & 0x08) != 0) b = Xmask(b);
        if ((flags & 0x04) != 0) b = Revnot(b);
        if ((flags & 0x02) != 0) b = RotRight(b);
        if ((flags & 0x01) != 0)
        {
            var (key, data, _) = ReadKeyed(b);
            b = XorKey(key, data);
        }
        return b;
    }

    /// <summary>Random 16-word key for keyed stages.</summary>
    public static uint[] RandomKey()
    {
        var key = new uint[KeyWords];
        var bytes = RandomNumberGenerator.GetBytes(4 * KeyWords);
        for (int i = 0; i < KeyWords; i++) key[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4 * i));
        return key;
    }
}

/// <summary>Known <c>.hf</c> header versions.</summary>
public static class PackVersion
{
    /// <summary>revnot + xmask; what the original client and launcher write.</summary>
    public const uint Plain = 0x30;

    /// <summary>xmask + keyed XOR + revnot; used by official patch files.</summary>
    public const uint Keyed = 0x20;

    /// <summary>Old codec with flags 5 (old client builds).</summary>
    public const uint Legacy = 0x11;
}
