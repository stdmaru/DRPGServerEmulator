using System.Buffers.Binary;

namespace DRPG.Shared.Formats.Pack;

/// <summary>Builds a new <c>.hf/.pf</c> pair (version 0x30 index) from in-memory files.</summary>
public static class PackWriter
{
    /// <summary>Header of every <c>.pf</c>: <c>u32 0x10, u32 0</c>.</summary>
    public const int DataHeaderSize = 8;

    public static void Create(string path, IEnumerable<(string Name, byte[] Content)> files)
    {
        var (hf, pf) = PackPath.Pair(path);
        var entries = new List<PackEntry>();
        using (var f = new FileStream(pf, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            f.Write(DataHeader());
            foreach (var (name, content) in files)
            {
                if (!PackPath.IsSafe(name)) throw new PackFormatException($"unsafe entry name: {name}");
                var blob = PackCodec.Deflate(content);
                entries.Add(new PackEntry(PackPath.Normalize(name), f.Position, blob.Length));
                f.Write(blob);
            }
            f.Flush(flushToDisk: true);
        }
        new PackIndex(PackVersion.Plain, 0, 1, entries).WritePlain(hf);
    }

    internal static byte[] DataHeader()
    {
        var header = new byte[DataHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x10);
        return header;
    }
}
