using System.Buffers.Binary;
using System.Text;

namespace DRPG.Shared.Formats.Pack;

/// <summary>
/// Read access to a <c>.hf/.pf</c> pair of the original client (<c>resource</c>, <c>table</c>, <c>patch_&lt;n&gt;</c>).
/// The <c>.pf</c> is <c>u32 0x10, u32 0</c> followed by one zlib stream per file. Never writes: see
/// <see cref="PatchMerger"/> and <see cref="PackWriter"/>.
/// </summary>
public sealed class PackArchive
{
    private readonly Dictionary<string, PackEntry> _byName;

    private PackArchive(string hfPath, string pfPath, PackIndex index)
    {
        HfPath = hfPath;
        PfPath = pfPath;
        Index = index;
        _byName = new Dictionary<string, PackEntry>(index.Entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var e in index.Entries) _byName[PackPath.Normalize(e.Name)] = e;
    }

    public string HfPath { get; }
    public string PfPath { get; }
    public PackIndex Index { get; }

    /// <summary>Normalized names (backslashes, no leading separator).</summary>
    public IReadOnlyCollection<string> Names => _byName.Keys;

    /// <summary>Opens a pair. <paramref name="path"/> may be the <c>.hf</c>, the <c>.pf</c> or the path without extension.</summary>
    public static PackArchive Open(string path)
    {
        var (hf, pf) = PackPath.Pair(path);
        if (!File.Exists(hf) || !File.Exists(pf)) throw new PackFormatException($"pack not found: {hf} / {pf}");
        return new PackArchive(hf, pf, PackIndex.Read(hf, new FileInfo(pf).Length));
    }

    public bool Contains(string name) => _byName.ContainsKey(PackPath.Normalize(name));

    public bool TryGetEntry(string name, out PackEntry entry) => _byName.TryGetValue(PackPath.Normalize(name), out entry);

    /// <summary>Reads an entry that has no extra codec (assets such as <c>gui\dr_title_000.spr</c>): zlib only.</summary>
    public byte[] ReadEntry(string name) => PackCodec.Inflate(ReadRaw(name));

    /// <summary>Reads the stored zlib stream as is (used to copy entries between packs).</summary>
    public byte[] ReadRaw(string name)
    {
        if (!TryGetEntry(name, out var entry)) throw new PackFormatException($"{name} is not in {HfPath}");
        return ReadRaw(entry);
    }

    public byte[] ReadRaw(PackEntry entry)
    {
        var raw = new byte[entry.Size];
        using var f = File.OpenRead(PfPath);
        f.Position = entry.Offset;
        f.ReadExactly(raw);
        return raw;
    }

    /// <summary>Reads an encrypted table <c>.dat</c> (e.g. <c>table\digimon_us.dat</c>) as rows of <c>|</c>-separated fields.</summary>
    public IReadOnlyList<string[]> ReadTable(string name)
    {
        var plain = PackCodec.Inflate(PackCodec.OldDecode(PackCodec.Inflate(ReadRaw(name))));
        var rows = new List<string[]>();
        if (plain.Length < 4) throw new PackFormatException($"{name}: table truncated");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(plain);
        int p = 4;
        for (int i = 0; i < count; i++)
        {
            if (plain.Length - p < 4) throw new PackFormatException($"{name}: table truncated");
            uint n = BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(p));
            p += 4;
            if (n > (plain.Length - p) / 2) throw new PackFormatException($"{name}: row {i} past the end");
            rows.Add(Encoding.Unicode.GetString(plain, p, 2 * (int)n).Split('|'));
            p += 2 * (int)n;
        }
        return rows;
    }

    /// <summary>Copies the <c>.hf</c> and the <c>.pf</c> to <paramref name="directory"/>.</summary>
    public void CopyTo(string directory)
    {
        Directory.CreateDirectory(directory);
        File.Copy(PfPath, Path.Combine(directory, Path.GetFileName(PfPath)), overwrite: true);
        File.Copy(HfPath, Path.Combine(directory, Path.GetFileName(HfPath)), overwrite: true);
    }
}

/// <summary>Names and paths inside and around packs.</summary>
public static class PackPath
{
    public const string IndexExtension = ".hf";
    public const string DataExtension = ".pf";

    public static string Normalize(string name) => name.Replace('/', '\\').TrimStart('\\');

    public static (string Hf, string Pf) Pair(string path)
    {
        var ext = Path.GetExtension(path);
        var basePath = ext.Equals(IndexExtension, StringComparison.OrdinalIgnoreCase)
            || ext.Equals(DataExtension, StringComparison.OrdinalIgnoreCase)
            ? Path.ChangeExtension(path, null)
            : path;
        return (basePath + IndexExtension, basePath + DataExtension);
    }

    /// <summary>True for a relative name with no empty, <c>.</c> or <c>..</c> part and no drive or root.</summary>
    public static bool IsSafe(string name)
    {
        if (name.Length == 0 || name.Contains(':') || name.Contains('\0')) return false;
        var parts = name.Replace('/', '\\').Split('\\');
        if (parts[0].Length == 0) return false;
        return parts.All(p => p.Length > 0 && p != "." && p != "..");
    }
}
