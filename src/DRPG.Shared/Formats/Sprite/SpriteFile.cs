using System.Buffers.Binary;

namespace DRPG.Shared.Formats.Sprite;

/// <summary>A <c>.spr</c> file is truncated or does not match the format.</summary>
public sealed class SpriteFormatException(string message) : Exception(message);

/// <summary>Size of one frame. A zero-sized frame has no pixels.</summary>
public readonly record struct SpriteFrameInfo(int Width, int Height);

/// <summary>
/// One run-length encoded layer: a shared RGB565 word buffer and the word offset of each frame inside it.
/// </summary>
public sealed class SpriteLayer
{
    internal SpriteLayer(ushort[] words, int[] offsets)
    {
        Words = words;
        Offsets = offsets;
    }

    internal ushort[] Words { get; }
    internal int[] Offsets { get; }
}

/// <summary>
/// Reader for the client's <c>.spr</c> sprites. Layout recovered from the v322 loader at <c>0x00871e80</c>
/// (see docs/cliente/formato-spr.md): <c>u32 frameCount, u32 wordCount</c>, then per-frame tables of
/// <c>u32 offset</c>, <c>u32 width</c> and <c>u32 height</c>, <c>wordCount</c> RGB565 words, and two optional
/// layers (<c>u32 wordCount</c>; when non-zero, a per-frame offset table and the words). The first optional
/// layer is the shadow; the second is not used by any GUI sprite.
/// </summary>
/// <remarks>
/// Each frame is encoded as <c>u16 rows</c>, then per row <c>u16 runs</c> and per run
/// <c>u16 skip, u16 length</c> followed by <c>length</c> pixels. Skipped pixels are transparent.
/// </remarks>
public sealed class SpriteFile
{
    private const int MaxSide = 8192;

    private SpriteFile(SpriteFrameInfo[] frames, SpriteLayer image, SpriteLayer? shadow, SpriteLayer? extra)
    {
        Frames = frames;
        Image = image;
        Shadow = shadow;
        Extra = extra;
    }

    public IReadOnlyList<SpriteFrameInfo> Frames { get; }
    public SpriteLayer Image { get; }
    public SpriteLayer? Shadow { get; }
    public SpriteLayer? Extra { get; }

    public static SpriteFile Parse(ReadOnlySpan<byte> data)
    {
        int pos = 0;
        int count = ReadCount(data, ref pos, "frame count");
        int words = ReadCount(data, ref pos, "word count");

        var offsets = ReadTable(data, ref pos, count);
        var widths = ReadTable(data, ref pos, count);
        var heights = ReadTable(data, ref pos, count);
        var frames = new SpriteFrameInfo[count];
        for (int i = 0; i < count; i++)
        {
            if (widths[i] > MaxSide || heights[i] > MaxSide)
                throw new SpriteFormatException($"frame {i} is {widths[i]}x{heights[i]}");
            frames[i] = new SpriteFrameInfo(widths[i], heights[i]);
        }

        var image = new SpriteLayer(ReadWords(data, ref pos, words), offsets);
        var shadow = ReadOptionalLayer(data, ref pos, count);
        var extra = ReadOptionalLayer(data, ref pos, count);
        if (pos != data.Length)
            throw new SpriteFormatException($"{data.Length - pos} trailing bytes");
        return new SpriteFile(frames, image, shadow, extra);
    }

    /// <summary>
    /// Decodes one frame of <paramref name="layer"/> to RGBA8888 (R in the lowest byte), transparent pixels as 0.
    /// <paramref name="destination"/> must hold <c>Width * Height</c> pixels.
    /// </summary>
    public void Decode(SpriteLayer layer, int frame, Span<uint> destination)
    {
        var info = Frames[frame];
        int size = info.Width * info.Height;
        if (destination.Length < size)
            throw new ArgumentException($"needs {size} pixels", nameof(destination));
        destination[..size].Clear();
        if (size == 0)
            return;

        var words = layer.Words;
        int i = layer.Offsets[frame];
        int rows = Word(words, ref i);
        if (rows != info.Height)
            throw new SpriteFormatException($"frame {frame} has {rows} rows, expected {info.Height}");

        for (int y = 0; y < rows; y++)
        {
            var line = destination.Slice(y * info.Width, info.Width);
            int runs = Word(words, ref i);
            int x = 0;
            for (int r = 0; r < runs; r++)
            {
                x += Word(words, ref i);
                int length = Word(words, ref i);
                if (x + length > info.Width || i + length > words.Length)
                    throw new SpriteFormatException($"frame {frame} row {y} run overflows");
                for (int k = 0; k < length; k++)
                    line[x + k] = ToRgba(words[i + k]);
                i += length;
                x += length;
            }
        }
    }

    public uint[] Decode(SpriteLayer layer, int frame)
    {
        var info = Frames[frame];
        var pixels = new uint[info.Width * info.Height];
        Decode(layer, frame, pixels);
        return pixels;
    }

    /// <summary>RGB565 to opaque RGBA8888 with bit replication, so 0x1F becomes 0xFF.</summary>
    public static uint ToRgba(ushort c)
    {
        uint r = (uint)(c >> 11) & 0x1F, g = (uint)(c >> 5) & 0x3F, b = (uint)c & 0x1F;
        r = (r << 3) | (r >> 2);
        g = (g << 2) | (g >> 4);
        b = (b << 3) | (b >> 2);
        return r | (g << 8) | (b << 16) | 0xFF000000u;
    }

    private static int Word(ushort[] words, ref int i)
    {
        if ((uint)i >= (uint)words.Length)
            throw new SpriteFormatException("frame data ends early");
        return words[i++];
    }

    private static SpriteLayer? ReadOptionalLayer(ReadOnlySpan<byte> data, ref int pos, int count)
    {
        if (data.Length - pos < 4)
            return null;
        int words = ReadCount(data, ref pos, "layer word count");
        if (words == 0)
            return null;
        var offsets = ReadTable(data, ref pos, count);
        return new SpriteLayer(ReadWords(data, ref pos, words), offsets);
    }

    private static int ReadCount(ReadOnlySpan<byte> data, ref int pos, string what)
    {
        if (data.Length - pos < 4)
            throw new SpriteFormatException($"missing {what}");
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]);
        pos += 4;
        if (value > (uint)(data.Length - pos))
            throw new SpriteFormatException($"{what} {value} exceeds the file");
        return (int)value;
    }

    private static int[] ReadTable(ReadOnlySpan<byte> data, ref int pos, int count)
    {
        if ((long)count * 4 > data.Length - pos)
            throw new SpriteFormatException("frame table exceeds the file");
        var table = new int[count];
        for (int i = 0; i < count; i++, pos += 4)
            table[i] = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]), int.MaxValue);
        return table;
    }

    private static ushort[] ReadWords(ReadOnlySpan<byte> data, ref int pos, int count)
    {
        if ((long)count * 2 > data.Length - pos)
            throw new SpriteFormatException("pixel data exceeds the file");
        var words = new ushort[count];
        for (int i = 0; i < count; i++, pos += 2)
            words[i] = BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);
        return words;
    }
}
