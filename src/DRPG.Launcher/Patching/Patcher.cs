using System.Globalization;
using System.IO.Compression;

namespace DRPG.Launcher.Patching;

/// <summary>What the patch screen shows. <see cref="Fraction"/> is the bar (0..1) of the current step.</summary>
public sealed record PatchProgress(PatchPhase Phase, int Version, int Index, int Count, long Done, long Total)
{
    public double Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : 0;
}

public enum PatchPhase { FetchingConfiguration, Downloading, Applying }

public abstract record PatchOutcome
{
    public sealed record UpToDate(PatchConfiguration Configuration) : PatchOutcome;
    public sealed record Maintenance(string Title, string Message) : PatchOutcome;
    public sealed record NeedsReinstall(int Installed, int Minimum) : PatchOutcome;
    public sealed record Failed(string Reason) : PatchOutcome;
}

/// <summary>
/// Brings the install up to date the way the official launcher does: reads <c>DR_Configuration.json</c>, then for
/// each pending version downloads <c>Patch_&lt;n&gt;.zip</c>, extracts <c>patch_&lt;n&gt;.hf/.pf</c>, merges them into
/// <c>resource</c> and only then writes <c>VersionInfo.ini</c>. A crash between steps resumes at the same version:
/// the merge is crash-safe (<see cref="PatchMerger"/>) and the version moves only after it.
/// ❓ The official JSON has no hashes, so a patch is trusted on the strength of HTTPS alone.
/// </summary>
public sealed class Patcher(HttpClient http, string installDirectory, bool allowInsecure)
{
    private const int BufferBytes = 1 << 16;
    private const long MaxPatchBytes = 4L << 30;

    private string WorkDirectory => Path.Combine(installDirectory, "patch-download");

    public async Task<PatchOutcome> RunAsync(string configUrl, IProgress<PatchProgress> progress, CancellationToken ct)
    {
        try
        {
            progress.Report(new PatchProgress(PatchPhase.FetchingConfiguration, 0, 0, 0, 0, 0));
            CheckScheme(configUrl);
            var configuration = PatchConfiguration.Parse(await http.GetStringAsync(configUrl, ct));
            if (configuration.ServerInfo.Maintenance)
                return new PatchOutcome.Maintenance(configuration.ServerInfo.Message.Title, configuration.ServerInfo.Message.Msg);

            int installed = VersionInfo.Read(Path.Combine(installDirectory, "VersionInfo.ini"));
            var steps = configuration.PendingPatches(installed);
            if (steps is null)
                return new PatchOutcome.NeedsReinstall(installed, configuration.PatchInfo.MinPatchVersion);

            for (int i = 0; i < steps.Count; i++)
                await ApplyAsync(steps[i], i, steps.Count, progress, ct);
            if (Directory.Exists(WorkDirectory))
                Directory.Delete(WorkDirectory, recursive: true);
            return new PatchOutcome.UpToDate(configuration);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException
                                      or PatchConfigurationException or Shared.Formats.Pack.PackFormatException
                                      or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new PatchOutcome.Failed(e.Message);
        }
    }

    private async Task ApplyAsync(PatchStep step, int index, int count, IProgress<PatchProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(WorkDirectory);
        var zipPath = Path.Combine(WorkDirectory, $"{step.PackName}.zip");
        await DownloadAsync(step, zipPath, index, count, progress, ct);

        progress.Report(new PatchProgress(PatchPhase.Applying, step.Version, index, count, 0, 1));
        var hf = ApplyZip(zipPath, step.PackName);
        PatchMerger.Merge(Path.Combine(installDirectory, "resource.hf"), hf);
        VersionInfo.Write(Path.Combine(installDirectory, "VersionInfo.ini"), step.Version);
        File.Delete(zipPath);
        progress.Report(new PatchProgress(PatchPhase.Applying, step.Version, index, count, 1, 1));
    }

    private async Task DownloadAsync(PatchStep step, string target, int index, int count, IProgress<PatchProgress> progress, CancellationToken ct)
    {
        CheckScheme(step.Url);
        using var response = await http.GetAsync(step.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? 0;
        if (total > MaxPatchBytes)
            throw new InvalidDataException($"{step.Url} is {total} bytes");

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(target);
        var buffer = new byte[BufferBytes];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            done += read;
            if (done > MaxPatchBytes)
                throw new InvalidDataException($"{step.Url} is larger than {MaxPatchBytes} bytes");
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            progress.Report(new PatchProgress(PatchPhase.Downloading, step.Version, index, count, done, total));
        }
    }

    /// <summary>
    /// Applies the zip the way the official patches are built (✅ <c>Patch_240.zip</c>): <c>patch_&lt;n&gt;.hf/.pf</c>
    /// at the root are extracted to the work folder and returned for the merge; every other file
    /// (<c>DigimonRPG.bin</c>, <c>table.hf/.pf</c>, <c>Language/*</c>, <c>Sound/*</c>) replaces the one in the install.
    /// Each file is written to a temporary name and moved over the old one, so a crash never leaves half a file.
    /// Unsafe names (absolute, <c>..</c>, drive letters) and the <c>resource</c> pack itself are refused.
    /// </summary>
    private string ApplyZip(string zipPath, string packName)
    {
        var outDir = Path.Combine(WorkDirectory, packName);
        Directory.CreateDirectory(outDir);
        string hf = Path.Combine(outDir, "patch.hf"), pf = Path.Combine(outDir, "patch.pf");
        bool gotHf = false, gotPf = false;
        var root = Path.GetFullPath(installDirectory);
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/'))
                continue; // folder entry
            if (name.Equals(packName + ".hf", StringComparison.OrdinalIgnoreCase))
            {
                entry.ExtractToFile(hf, overwrite: true);
                gotHf = true;
                continue;
            }
            if (name.Equals(packName + ".pf", StringComparison.OrdinalIgnoreCase))
            {
                entry.ExtractToFile(pf, overwrite: true);
                gotPf = true;
                continue;
            }
            var target = SafeTarget(root, name)
                ?? throw new InvalidDataException($"{Path.GetFileName(zipPath)}: refused entry {name}");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temp = target + ".patch-tmp";
            entry.ExtractToFile(temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
        }
        if (!gotHf || !gotPf)
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(zipPath)} has no {packName}.hf/.pf"));
        return hf;
    }

    /// <summary>Install path for a zip entry, or null when the name could escape the install or touch the resource pack.</summary>
    internal static string? SafeTarget(string installRoot, string entryName)
    {
        if (entryName.Length == 0 || entryName.StartsWith('/') || entryName.Contains(':')
            || entryName.Split('/').Any(part => part is "" or "." or ".."))
            return null;
        var file = Path.GetFileName(entryName);
        if (file.Equals("resource.hf", StringComparison.OrdinalIgnoreCase) || file.Equals("resource.pf", StringComparison.OrdinalIgnoreCase))
            return null;
        var full = Path.GetFullPath(Path.Combine(installRoot, entryName));
        return full.StartsWith(installRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }

    private void CheckScheme(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new PatchConfigurationException($"bad URL {url}");
        if (uri.Scheme == "http" && !allowInsecure)
            throw new PatchConfigurationException($"{url} is not HTTPS");
    }
}
