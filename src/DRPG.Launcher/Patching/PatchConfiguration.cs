using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DRPG.Launcher.Patching;

/// <summary>
/// The launcher entry point <c>DR_Configuration.json</c>, in the official format (including the
/// <c>LanuncherInfo</c> typo) plus the <c>LoginServer</c> section added for the new launcher
/// (docs/protocolo/launcher.md).
/// </summary>
public sealed record PatchConfiguration
{
    public ServerInfoSection ServerInfo { get; init; } = new();

    [JsonPropertyName("LanuncherInfo")]
    public LauncherInfoSection LauncherInfo { get; init; } = new();

    public PatchInfoSection PatchInfo { get; init; } = new();
    public MainUrlSection MainUrl { get; init; } = new();
    public LoginServerSection? LoginServer { get; init; }

    public sealed record ServerInfoSection
    {
        public bool Maintenance { get; init; }
        public MessageSection Message { get; init; } = new();
    }

    public sealed record MessageSection
    {
        public string Title { get; init; } = "";
        public string Msg { get; init; } = "";
    }

    public sealed record LauncherInfoSection
    {
        public string DownloadPath { get; init; } = "";
        public int CurrentVersion { get; init; }
        public string FileName { get; init; } = "";
        public string LauncherFileName { get; init; } = "";
        public string RunExeFileName { get; init; } = "";
    }

    public sealed record PatchInfoSection
    {
        public string DownloadUrl { get; init; } = "";
        public int MinPatchVersion { get; init; }
        public int PatchVersion { get; init; }
        public string DownloadFileHeader { get; init; } = "Patch_%d.zip";
        public string PatchFileHeader { get; init; } = "Patch_%d";
    }

    public sealed record MainUrlSection
    {
        public string HomePageUrl { get; init; } = "";
        public string MainPageUrl { get; init; } = "";
    }

    public sealed record LoginServerSection
    {
        public string Host { get; init; } = "";
        public int Port { get; init; }
        public string KeyFingerprint { get; init; } = "";
    }

    public static PatchConfiguration Parse(string json)
    {
        PatchConfiguration? config;
        try
        {
            config = JsonSerializer.Deserialize(json, PatchJsonContext.Default.PatchConfiguration);
        }
        catch (JsonException e)
        {
            throw new PatchConfigurationException($"invalid JSON: {e.Message}");
        }
        if (config is null)
            throw new PatchConfigurationException("empty configuration");
        config = config.Normalized();
        config.Validate();
        return config;
    }

    /// <summary>
    /// Missing sections and fields, or an explicit <c>null</c> from the server, become the defaults, so nothing
    /// after parsing has to check for null.
    /// </summary>
    private PatchConfiguration Normalized()
    {
        var server = ServerInfo ?? new();
        var launcher = LauncherInfo ?? new();
        var patch = PatchInfo ?? new();
        var main = MainUrl ?? new();
        var defaults = new PatchInfoSection();
        return this with
        {
            ServerInfo = server with
            {
                Message = new MessageSection { Title = server.Message?.Title ?? "", Msg = server.Message?.Msg ?? "" },
            },
            LauncherInfo = launcher with
            {
                DownloadPath = launcher.DownloadPath ?? "",
                FileName = launcher.FileName ?? "",
                LauncherFileName = launcher.LauncherFileName ?? "",
                RunExeFileName = launcher.RunExeFileName ?? "",
            },
            PatchInfo = patch with
            {
                DownloadUrl = patch.DownloadUrl ?? "",
                DownloadFileHeader = patch.DownloadFileHeader ?? defaults.DownloadFileHeader,
                PatchFileHeader = patch.PatchFileHeader ?? defaults.PatchFileHeader,
            },
            MainUrl = new MainUrlSection { HomePageUrl = main.HomePageUrl ?? "", MainPageUrl = main.MainPageUrl ?? "" },
            LoginServer = LoginServer is null ? null : LoginServer with
            {
                Host = LoginServer.Host ?? "",
                KeyFingerprint = LoginServer.KeyFingerprint ?? "",
            },
        };
    }

    private void Validate()
    {
        var p = PatchInfo;
        if (p.PatchVersion < 0 || p.MinPatchVersion < 0 || p.MinPatchVersion > p.PatchVersion)
            throw new PatchConfigurationException($"bad patch versions {p.MinPatchVersion}..{p.PatchVersion}");
        if (CountPlaceholders(p.DownloadFileHeader) != 1 || CountPlaceholders(p.PatchFileHeader) != 1)
            throw new PatchConfigurationException("file headers need exactly one %d");
        if (LoginServer is { } login)
        {
            if (string.IsNullOrWhiteSpace(login.Host) || login.Port is < 1 or > 65535)
                throw new PatchConfigurationException("LoginServer needs Host and Port");
            if (login.KeyFingerprint.Length != 0 && !IsLowerHex(login.KeyFingerprint, 64))
                throw new PatchConfigurationException("KeyFingerprint must be 64 lowercase hex characters");
        }
    }

    /// <summary>
    /// Patches the install still needs, in order. Null when the install is older than
    /// <c>MinPatchVersion</c> and cannot be patched (a full reinstall is required).
    /// </summary>
    public IReadOnlyList<PatchStep>? PendingPatches(int installedVersion)
    {
        var p = PatchInfo;
        if (installedVersion < p.MinPatchVersion)
            return null;
        var steps = new List<PatchStep>();
        for (int v = installedVersion + 1; v <= p.PatchVersion; v++)
            steps.Add(new PatchStep(v, CombineUrl(p.DownloadUrl, Format(p.DownloadFileHeader, v)), Format(p.PatchFileHeader, v)));
        return steps;
    }

    private static string Format(string header, int version) =>
        header.Replace("%d", version.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static string CombineUrl(string baseUrl, string file) => baseUrl.TrimEnd('/') + "/" + file;

    private static int CountPlaceholders(string s)
    {
        int n = 0;
        for (int i = s.IndexOf("%d", StringComparison.Ordinal); i >= 0; i = s.IndexOf("%d", i + 2, StringComparison.Ordinal))
            n++;
        return n;
    }

    private static bool IsLowerHex(string s, int length) =>
        s.Length == length && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>One patch to apply: <c>Patch_241.zip</c> holds <c>patch_241.hf/.pf</c>.</summary>
public sealed record PatchStep(int Version, string Url, string PackName);

public sealed class PatchConfigurationException(string message) : Exception(message);

/// <summary>Reads and writes the install's <c>VersionInfo.ini</c> (UTF-16 LE with BOM, <c>[VERSION] version=N</c>).</summary>
public static class VersionInfo
{
    public static int Read(string path)
    {
        foreach (var raw in File.ReadAllLines(path, Encoding.Unicode))
        {
            var line = raw.Trim();
            if (line.StartsWith("version", StringComparison.OrdinalIgnoreCase) && line.IndexOf('=') is > 0 and var eq
                && int.TryParse(line[(eq + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int v))
                return v;
        }
        throw new PatchConfigurationException($"{path} has no version");
    }

    /// <summary>Writes atomically, so a crash never leaves a half-written file.</summary>
    public static void Write(string path, int version)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, $"[VERSION]\r\nversion={version.ToString(CultureInfo.InvariantCulture)}\r\n", new UnicodeEncoding(false, true));
        File.Move(tmp, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(PatchConfiguration))]
internal sealed partial class PatchJsonContext : JsonSerializerContext;
