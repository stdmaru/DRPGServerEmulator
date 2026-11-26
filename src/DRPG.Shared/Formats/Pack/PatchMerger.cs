namespace DRPG.Shared.Formats.Pack;

/// <summary>
/// Merges a patch pack (<c>patch_&lt;n&gt;.hf/.pf</c>) into a target pack (<c>resource.hf/.pf</c>) the way the
/// official launcher does: each patch stream is <b>appended</b> to the end of the target <c>.pf</c>, and the target
/// index is rewritten (version 0x30) with the patch entries replacing same-name entries and new ones added at the end.
/// ✅ Evidence: the v322 <c>resource.pf</c> is 13 GB with entries pointing up to its last byte, and the 24 entries of
/// <c>Patch_240</c> all exist in it (docs/planos/cliente-novo.md#launcher-login--auto-patcher-marco-1).
/// <para>
/// Crash safety: the streams are appended and flushed before the index changes, and the new index is written to a
/// temporary file and moved over the old one. A crash leaves either the old index (the appended bytes are unreferenced)
/// or the new one, never a broken pack. The <c>.pf</c> is never rewritten, so a 13 GB copy is not needed.
/// </para>
/// </summary>
public static class PatchMerger
{
    private const int CopyBufferBytes = 1 << 20;

    public sealed record Result(int Replaced, int Added);

    public static Result Merge(string targetPath, string patchPath)
    {
        var target = PackArchive.Open(targetPath);
        var patch = PackArchive.Open(patchPath);
        foreach (var e in patch.Index.Entries)
            if (!PackPath.IsSafe(e.Name)) throw new PackFormatException($"{patch.HfPath}: unsafe entry name {e.Name}");

        var appended = AppendStreams(target.PfPath, patch);

        var entries = target.Index.Entries.ToList();
        var position = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entries.Count; i++) position[PackPath.Normalize(entries[i].Name)] = i;
        int replaced = 0, added = 0;
        foreach (var e in appended)
        {
            var key = PackPath.Normalize(e.Name);
            if (position.TryGetValue(key, out var i))
            {
                entries[i] = e with { Name = entries[i].Name };
                replaced++;
            }
            else
            {
                position[key] = entries.Count;
                entries.Add(e with { Name = key });
                added++;
            }
        }

        var temp = target.HfPath + ".tmp";
        new PackIndex(PackVersion.Plain, target.Index.Field1, target.Index.Field2, entries).WritePlain(temp);
        File.Move(temp, target.HfPath, overwrite: true);
        return new Result(replaced, added);
    }

    /// <summary>Copies every patch stream to the end of <paramref name="targetPf"/>; returns the entries with new offsets.</summary>
    private static List<PackEntry> AppendStreams(string targetPf, PackArchive patch)
    {
        var result = new List<PackEntry>(patch.Index.Entries.Count);
        var buffer = new byte[CopyBufferBytes];
        using var source = File.OpenRead(patch.PfPath);
        using var target = new FileStream(targetPf, FileMode.Open, FileAccess.Write, FileShare.Read);
        target.Seek(0, SeekOrigin.End);
        foreach (var e in patch.Index.Entries)
        {
            long offset = target.Position;
            source.Position = e.Offset;
            int left = e.Size;
            while (left > 0)
            {
                int n = source.Read(buffer, 0, Math.Min(left, buffer.Length));
                if (n == 0) throw new PackFormatException($"{patch.PfPath}: {e.Name} truncated");
                target.Write(buffer, 0, n);
                left -= n;
            }
            result.Add(new PackEntry(e.Name, offset, e.Size));
        }
        target.Flush(flushToDisk: true);
        return result;
    }
}
