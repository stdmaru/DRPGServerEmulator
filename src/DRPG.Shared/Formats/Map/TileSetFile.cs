using System.Buffers.Binary;
using System.Text;
using DRPG.Shared.Formats.Sprite;

namespace DRPG.Shared.Formats.Map;

/// <summary>
/// A <c>map\*.til</c> tileset (✅ v322 <c>0x00b5dbd0</c>, docs/cliente/mapas.md#til-tileset): the signature <c>DCTILE0.03</c>,
/// width and height of the sheet, then per frame a width, a height and a word offset, the number of words and the
/// RGB565 image in the row format of the <c>.spr</c>. The optional 2000-byte attribute block is not read (❓ its use).
/// </summary>
public sealed class TileSetFile
{
    public const string Signature = "DCTILE0.03";

    /// <summary>❓ A limit of the new reader (the files of the game have 200 frames).</summary>
    public const int MaxFrames = 1024;

    private TileSetFile(SpriteFile sprite) => Sprite = sprite;

    /// <summary>The frames; <see cref="SpriteFile.Decode(SpriteLayer, int)"/> with <see cref="SpriteFile.Image"/> gives the pixels.</summary>
    public SpriteFile Sprite { get; }

    public int FrameCount => Sprite.Frames.Count;

    public uint[] DecodeFrame(int frame) => Sprite.Decode(Sprite.Image, frame);

    public static TileSetFile Parse(ReadOnlySpan<byte> data)
    {
        var signature = Encoding.ASCII.GetBytes(Signature);
        if (data.Length < signature.Length || !data[..signature.Length].SequenceEqual(signature))
            throw new MapFormatException("til: wrong signature");
        int pos = signature.Length;
        pos += 8;                                       // sheet width and height (❓ unused)
        if (pos + 4 > data.Length)
            throw new MapFormatException("til: missing the frame count");
        uint count = ReadU32(data, ref pos);
        if (count > MaxFrames)
            throw new MapFormatException($"til: {count} frames, the limit is {MaxFrames}");
        if ((long)count * 12 + 4 > data.Length - pos)
            throw new MapFormatException("til: the frame tables exceed the file");

        var widths = Table(data, ref pos, (int)count);
        _ = Table(data, ref pos, (int)count);               // ❓ the heights of the file are not used (see below)
        var offsets = Table(data, ref pos, (int)count);
        uint words = ReadU32(data, ref pos);
        if ((long)words * 2 > data.Length - pos)
            throw new MapFormatException($"til: {words} words exceed the file");
        var image = new ushort[words];
        for (int i = 0; i < image.Length; i++, pos += 2)
            image[i] = BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);

        var frames = new SpriteFrameInfo[count];
        var wordOffsets = new int[count];
        for (int i = 0; i < frames.Length; i++)
        {
            if (widths[i] > 4096)
                throw new MapFormatException($"til: frame {i} is {widths[i]} wide");
            if (widths[i] != 0 && offsets[i] >= words)
                throw new MapFormatException($"til: frame {i} starts at word {offsets[i]} of {words}");
            // ❓ The height table of the file is not the frame's height: some files say 1, 16 or 24 for frames of 32 rows. The
            // frame's own row count (its first word) is the height, as for the .spr.
            int rows = offsets[i] < words ? image[offsets[i]] : 0;
            if (rows > 4096)
                throw new MapFormatException($"til: frame {i} has {rows} rows");
            frames[i] = new SpriteFrameInfo((int)widths[i], rows);
            wordOffsets[i] = (int)Math.Min(offsets[i], int.MaxValue);
        }
        try
        {
            return new TileSetFile(SpriteFile.FromFrames(frames, image, wordOffsets));
        }
        catch (SpriteFormatException e)
        {
            throw new MapFormatException($"til: {e.Message}");
        }
    }

    private static uint ReadU32(ReadOnlySpan<byte> data, ref int pos)
    {
        if (data.Length - pos < 4)
            throw new MapFormatException("til: the file ends early");
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]);
        pos += 4;
        return value;
    }

    private static uint[] Table(ReadOnlySpan<byte> data, ref int pos, int count)
    {
        var table = new uint[count];
        for (int i = 0; i < count; i++)
            table[i] = ReadU32(data, ref pos);
        return table;
    }
}
