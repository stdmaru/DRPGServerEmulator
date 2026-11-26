namespace DRPG.Engine.Assets;

/// <summary>The official install a game executable sits in: found next to the executable or given with <c>--game-dir</c>.</summary>
public sealed class GameInstall
{
    private GameInstall(string directory) => Directory = directory;

    public string Directory { get; }
    public string ResourcePath => Path.Combine(Directory, "resource.hf");
    public string VersionInfoPath => Path.Combine(Directory, "VersionInfo.ini");

    public static GameInstall? Find(string? explicitDirectory)
    {
        foreach (var dir in new[] { explicitDirectory, AppContext.BaseDirectory, Environment.CurrentDirectory })
            if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "resource.hf")) && File.Exists(Path.Combine(dir, "resource.pf")))
                return new GameInstall(Path.GetFullPath(dir));
        return null;
    }
}
