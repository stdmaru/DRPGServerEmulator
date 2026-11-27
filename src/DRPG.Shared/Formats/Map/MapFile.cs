using System.Buffers.Binary;

namespace DRPG.Shared.Formats.Map;

/// <summary>One cell of a map (24 bytes).</summary>
/// <param name="Object">Index in <see cref="MapFile.ObjectNames"/> of the object anchored here, or <see cref="MapCell.None"/>.</param>
/// <param name="Type">✅ +8: 1..998 areas, 1000..1005 and 9001..9021 ground effects, 8999 time warning, −1 nothing.</param>
/// <param name="TileSet">Index in <see cref="MapFile.TileNames"/> of the ground tileset, or <see cref="MapCell.None"/>.</param>
/// <param name="TileFrame">Frame of the tileset (0..199).</param>
/// <param name="Flags">✅ +0x14: bit 0 = walkable; ❓ bit 8 changes the draw mode of the ground.</param>
public readonly record struct MapCell(int Object, int Type, int TileSet, int TileFrame, uint Flags)
{
    public const int None = -1;

    public bool Walkable => (Flags & 1) != 0;
}

/// <summary>
/// A <c>map\*.map</c> file (✅ v322 loader <c>0x00b35fb0</c>, docs/cliente/mapas.md): width and height in cells, one
/// layer of 24-byte cells, then the names of the <c>.til</c> and <c>.obj</c> files it uses. Every size is checked against
/// the data before anything is read; a bad file is a <see cref="MapFormatException"/>. A cell index that points outside the
/// name lists is turned into <see cref="MapCell.None"/> and counted in <see cref="InvalidCellReferences"/> (the original warns
/// and draws nothing there; the old maps carry <c>0xBAADF0xx</c> junk in the high bits).
/// </summary>
public sealed class MapFile
{
    public const int CellBytes = 24, NameBytes = 20;

    /// <summary>✅ <c>0x00b35fb0</c>: at most 200 tilesets and 160 objects.</summary>
    public const int MaxTileSets = 200, MaxObjects = 160;

    /// <summary>✅ The cell size in pixels, read from the file (always 64 × 32).</summary>
    public const int CellWidth = 64, CellHeight = 32;

    /// <summary>❓ A limit of the new reader (the largest map of the game is 263 cells).</summary>
    public const int MaxSide = 2048;

    private const int HeaderBytes = 16;
    private const int MaxFrame = 199;

    private readonly MapCell[] _cells;

    private MapFile(int width, int height, MapCell[] cells, string[] tileNames, string[] objectNames, int invalid)
    {
        Width = width;
        Height = height;
        _cells = cells;
        TileNames = tileNames;
        ObjectNames = objectNames;
        InvalidCellReferences = invalid;
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<string> TileNames { get; }
    public IReadOnlyList<string> ObjectNames { get; }

    /// <summary>How many cell fields pointed outside the lists and were cleared.</summary>
    public int InvalidCellReferences { get; }

    public MapCell this[int x, int y] => (uint)x < (uint)Width && (uint)y < (uint)Height
        ? _cells[y * Width + x]
        : new MapCell(MapCell.None, -1, MapCell.None, 0, 0);

    public static MapFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderBytes)
            throw new MapFormatException("map: file too short for the header");
        uint width = BinaryPrimitives.ReadUInt32LittleEndian(data);
        uint height = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        if (width is 0 or > MaxSide || height is 0 or > MaxSide)
            throw new MapFormatException($"map: size {width}x{height} is outside 1..{MaxSide}");
        uint cellWidth = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        uint cellHeight = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        if (cellWidth != CellWidth || cellHeight != CellHeight)
            throw new MapFormatException($"map: cell size {cellWidth}x{cellHeight}, expected {CellWidth}x{CellHeight}");

        long cellBytes = (long)width * height * CellBytes;
        if (cellBytes > data.Length - HeaderBytes)
            throw new MapFormatException($"map: {width}x{height} cells need {cellBytes} bytes, the file has {data.Length - HeaderBytes}");
        int pos = HeaderBytes + (int)cellBytes;
        var tileNames = ReadNames(data, ref pos, MaxTileSets, "tileset");
        var objectNames = ReadNames(data, ref pos, MaxObjects, "object");

        var cells = new MapCell[(int)(width * height)];
        int invalid = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            var raw = data[(HeaderBytes + i * CellBytes)..];
            int obj = BinaryPrimitives.ReadInt32LittleEndian(raw[4..]);
            int type = BinaryPrimitives.ReadInt32LittleEndian(raw[8..]);
            int tileSet = BinaryPrimitives.ReadInt16LittleEndian(raw[0xC..]);
            uint frame = BinaryPrimitives.ReadUInt32LittleEndian(raw[0x10..]);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(raw[0x14..]);
            if (obj != MapCell.None && (uint)obj >= (uint)objectNames.Length)
            {
                obj = MapCell.None;
                invalid++;
            }
            if (tileSet != MapCell.None && (uint)tileSet >= (uint)tileNames.Length)
            {
                tileSet = MapCell.None;
                invalid++;
            }
            if (frame > MaxFrame)
            {
                tileSet = MapCell.None;                 // a frame the tileset cannot have: nothing to draw
                invalid++;
            }
            cells[i] = new MapCell(obj, type, tileSet, (int)Math.Min(frame, MaxFrame), flags);
        }
        return new MapFile((int)width, (int)height, cells, tileNames, objectNames, invalid);
    }

    private static string[] ReadNames(ReadOnlySpan<byte> data, ref int pos, int max, string what)
    {
        if (data.Length - pos < 2)
            throw new MapFormatException($"map: missing the {what} count");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);
        pos += 2;
        if (count > max)
            throw new MapFormatException($"map: {count} {what} names, the limit is {max}");
        if ((long)count * NameBytes > data.Length - pos)
            throw new MapFormatException($"map: {count} {what} names exceed the file");
        var names = new string[count];
        for (int i = 0; i < count; i++, pos += NameBytes)
        {
            names[i] = LegacyText.FixedString(data.Slice(pos, NameBytes));
        }
        return names;
    }
}
