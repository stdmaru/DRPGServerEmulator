using DRPG.Engine.Assets;
using DRPG.Shared.Formats.Pack;

namespace DRPG.Client.Startup;

/// <summary>
/// The real work behind the loading screen: opens <c>resource</c> (already open, it draws the screen itself), opens
/// <c>table</c>, and reads <c>Language\*.bin</c>, reporting to <see cref="LoadingProgress"/> after each step. Runs on
/// a pool thread; the screen only reads the progress and, at the end, the result.
/// ❓ The original also builds its object caches here (<c>0x00ad3d30</c>); that comes with the scenes that need them.
/// </summary>
public sealed class StartupLoader(GameInstall install, PackArchive resource, LoadingProgress progress)
{
    /// <summary>❓ The weights of the three steps are ours: the original reports from many more steps.</summary>
    private const float AfterResource = 25f, AfterTable = 50f, AfterLanguage = 100f;

    public Task<GameData> RunAsync(CancellationToken ct) => Task.Run(() => Run(ct), ct);

    private GameData Run(CancellationToken ct)
    {
        // The resource pack is open; its index is checked to be non-empty so a wrong install fails here, not later.
        if (resource.Names.Count == 0)
            throw new PackFormatException("resource.hf has no entries");
        progress.Raise(AfterResource);
        ct.ThrowIfCancellationRequested();

        var table = PackArchive.Open(install.TablePath);
        progress.Raise(AfterTable);
        ct.ThrowIfCancellationRequested();

        var language = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var files = Directory.Exists(install.LanguageDirectory)
            ? Directory.GetFiles(install.LanguageDirectory, "*.bin").Order(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        for (int i = 0; i < files.Length; i++)
        {
            language[Path.GetFileName(files[i])] = File.ReadAllBytes(files[i]);
            progress.Raise(AfterTable + (AfterLanguage - AfterTable) * (i + 1) / files.Length);
            ct.ThrowIfCancellationRequested();
        }
        progress.Raise(AfterLanguage);
        return new GameData(resource, table, language);
    }
}
