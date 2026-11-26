namespace DRPG.Launcher;

/// <summary>
/// <c>drpg-launcher.ini</c> next to the launcher (docs/protocolo/launcher.md):
/// <code>
/// [Patch]
/// ConfigUrl=https://patch.example/DR_Configuration.json
/// [Development]
/// AllowInsecure=true      ; HTTP and no key fingerprint check, with a visible warning
/// [Game]
/// Runner=wine             ; optional program that starts RunExeFileName (Linux)
/// </code>
/// </summary>
public sealed record LauncherSettings(string ConfigUrl, bool AllowInsecure, string? Runner)
{
    public const string FileName = "drpg-launcher.ini";

    public static LauncherSettings Load(string path)
    {
        if (!File.Exists(path))
            throw new LauncherSettingsException($"{FileName} not found next to the launcher");
        var values = IniFile.Parse(File.ReadAllLines(path));
        values.TryGetValue("patch.configurl", out var url);
        values.TryGetValue("development.allowinsecure", out var insecure);
        values.TryGetValue("game.runner", out var runner);
        var settings = new LauncherSettings(url ?? "", insecure is "1" or "true" or "yes", string.IsNullOrWhiteSpace(runner) ? null : runner);
        settings.Validate();
        return settings;
    }

    private void Validate()
    {
        if (!Uri.TryCreate(ConfigUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new LauncherSettingsException("Patch.ConfigUrl must be an http(s) URL");
        if (uri.Scheme == "http" && !AllowInsecure)
            throw new LauncherSettingsException("Patch.ConfigUrl must use HTTPS (or set Development.AllowInsecure)");
    }
}

public sealed class LauncherSettingsException(string message) : Exception(message);
