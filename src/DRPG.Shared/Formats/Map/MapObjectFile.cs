using System.Buffers.Binary;
using DRPG.Shared.Formats.Sprite;

namespace DRPG.Shared.Formats.Map;

/// <summary>
/// A <c>map\*.obj</c> map object (✅ v322 <c>0x00b415b0</c>, docs/cliente/mapas.md#obj-objeto-do-mapa): name[20], width and
/// height, the anchor, a 400-byte mask (❓ covered cells) and two images, each a <c>u32</c> word count and the words of one frame
/// in the row format of the <c>.spr</c>. The second image (❓ shadow or top) is read but not drawn.
/// </summary>
public sealed class MapObjectFile
{
    public const int NameBytes = 20, MaskBytes = 400;

    /// <summary>❓ A limit of the new reader (the objects of the game are about 1000 × 900).</summary>
    public const int MaxSide = 4096;

    private readonly SpriteFile _image;
    private readonly SpriteFile? _second;

    private MapObjectFile(string name, int anchorX, int anchorY, SpriteFile image, SpriteFile? second)
    {
        Name = name;
        AnchorX = anchorX;
        AnchorY = anchorY;
        _image = image;
        _second = second;
    }

    public string Name { get; }
    public int Width => _image.Frames[0].Width;
    public int Height => _image.Frames[0].Height;

    /// <summary>✅ The drawing uses <c>AnchorX mod 64</c> and <c>AnchorY mod 32</c> (docs/cliente/mapas.md).</summary>
    public int AnchorX { get; }
    public int AnchorY { get; }

    public bool HasSecondImage => _second is not null;

    public uint[] DecodeImage() => _image.Decode(_image.Image, 0);

    public static MapObjectFile Parse(ReadOnlySpan<byte> data)
    {
        const int headerBytes = NameBytes + 8 + MaskBytes;
        if (data.Length < headerBytes + 4)
            throw new MapFormatException("obj: file too short for the header");
        var name = LegacyText.FixedString(data[..NameBytes]);
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data[NameBytes..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data[(NameBytes + 2)..]);
        int anchorX = BinaryPrimitives.ReadUInt16LittleEndian(data[(NameBytes + 4)..]);
        int anchorY = BinaryPrimitives.ReadUInt16LittleEndian(data[(NameBytes + 6)..]);
        if (width > MaxSide || height > MaxSide)
            throw new MapFormatException($"obj: image is {width}x{height}");

        int pos = headerBytes;
        var first = ReadImage(data, ref pos, width, height, "image")
            ?? throw new MapFormatException("obj: the image is empty");
        SpriteFile? second = null;
        if (data.Length - pos >= 4)
            second = ReadImage(data, ref pos, width, height, "second image");
        return new MapObjectFile(name, anchorX, anchorY, first, second);
    }

    private static SpriteFile? ReadImage(ReadOnlySpan<byte> data, ref int pos, int width, int height, string what)
    {
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]);
        pos += 4;
        if (count == 0)
            return null;
        if ((long)count * 2 > data.Length - pos)
            throw new MapFormatException($"obj: the {what} has {count} words, the file has {(data.Length - pos) / 2}");
        var words = new ushort[count];
        for (int i = 0; i < words.Length; i++, pos += 2)
            words[i] = BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);
        try
        {
            return SpriteFile.FromFrames([new SpriteFrameInfo(width, height)], words, [0]);
        }
        catch (SpriteFormatException e)
        {
            throw new MapFormatException($"obj: {e.Message}");
        }
    }
}
