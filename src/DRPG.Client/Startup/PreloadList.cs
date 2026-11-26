using System.Reflection;

namespace DRPG.Client.Startup;

/// <summary>
/// The files the original reads into its sprite cache while the bar runs (docs/cliente/carga-inicial.md), kept as text
/// next to the code (<c>preload-*.txt</c>, embedded): one path per line, <c>#</c> comments, <c>[name]</c> section titles.
/// </summary>
public static class PreloadList
{
    public const string LoadingScreenGui = "preload-1-gui.txt";
    public const string GameInit = "preload-4-init.txt";
    public const string Title = "preload-6-title.txt";

    public static IReadOnlyList<string> Read(string resourceName)
    {
        using var stream = typeof(PreloadList).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"preload list {resourceName} is not embedded");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static IReadOnlyList<string> Parse(string text) =>
        text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith('#') && !line.StartsWith('['))
            .ToArray();
}
