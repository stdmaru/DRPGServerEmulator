namespace DRPG.Engine.Rendering;

/// <summary>
/// ❓ The original draws UI text with GDI "굴림체" (GulimChe, cp949) at a 12 px cell (v322 <c>0x00b563f0</c>). Gulim
/// cannot be shipped (copyright), so the engine uses it when the system has it and otherwise the first fallback
/// found. <c>Fonts\123RF_123RF_TTF.ttf</c> from the install is a decorative face used elsewhere.
/// </summary>
public static class FontLocator
{
    /// <summary>❓ Em size in pixels; tuned by eye against the original screenshots, not measured.</summary>
    public const int UiPixelSize = 12;

    private static readonly string[] Preferred =
    [
        "gulimche.ttf", "gulim.ttc", "gulim.ttf",
        "NanumGothic.ttf", "NanumGothic-Regular.ttf",
        "NotoSansCJK-Regular.ttc", "NotoSansKR-Regular.ttf", "NotoSansKR-Regular.otf",
        "DejaVuSans.ttf", "LiberationSans-Regular.ttf", "arial.ttf", "segoeui.ttf",
    ];

    public static string? FindUiFont() => Find(SearchDirectories(), Preferred);

    /// <summary>The first <paramref name="names"/> entry found in any directory (searched recursively); order of
    /// <paramref name="names"/> wins over order of directories.</summary>
    public static string? Find(IEnumerable<string> directories, IReadOnlyList<string> names)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in directories.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 4 });
                foreach (var file in files)
                    found.TryAdd(Path.GetFileName(file), file);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        foreach (var name in names)
            if (found.TryGetValue(name, out var path))
                return path;
        return null;
    }

    private static IEnumerable<string> SearchDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
            yield break;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".local", "share", "fonts");
        yield return Path.Combine(home, ".fonts");
        yield return "/usr/local/share/fonts";
        yield return "/usr/share/fonts";
        yield return "/Library/Fonts";
        yield return "/System/Library/Fonts";
    }
}
