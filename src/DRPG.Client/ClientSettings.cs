using DRPG.Engine.Assets;

namespace DRPG.Client;

/// <summary>A server address from <c>drpg-client.ini</c>.</summary>
public readonly record struct ServerAddress(string Host, int Port)
{
    public static ServerAddress? Parse(string? text)
    {
        if (text is null)
            return null;
        int colon = text.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(text.AsSpan(colon + 1), out int port) || port is < 1 or > ushort.MaxValue)
            return null;
        var host = text[..colon].Trim();
        return host.Length == 0 || Uri.CheckHostName(host) == UriHostNameType.Unknown ? null : new ServerAddress(host, port);
    }

    public override string ToString() => $"{Host}:{Port}";
}

/// <summary>
/// <c>drpg-client.ini</c> next to the executable. ❓ The original keeps its server table inside the binary
/// (docs/protocolo/servidores.md); the new client takes the login and channel addresses from this file. Missing file or
/// key: the local emulator. A value that is not <c>host:port</c> is an error, not a silent default.
/// </summary>
public sealed record ClientSettings(ServerAddress Login, ServerAddress Channel)
{
    public const string FileName = "drpg-client.ini";

    public static readonly ClientSettings Local = new(new ServerAddress("127.0.0.1", 7001), new ServerAddress("127.0.0.1", 7000));

    public static ClientSettings Load(string path)
    {
        if (!File.Exists(path))
            return Local;
        var values = IniFile.Parse(File.ReadAllLines(path));
        return new ClientSettings(
            Address(values, "servers.login", Local.Login),
            Address(values, "servers.channel", Local.Channel));
    }

    private static ServerAddress Address(Dictionary<string, string> values, string key, ServerAddress fallback)
    {
        if (!values.TryGetValue(key, out var text))
            return fallback;
        return ServerAddress.Parse(text) ?? throw new ClientSettingsException($"{FileName}: {key} must be host:port");
    }
}

public sealed class ClientSettingsException(string message) : Exception(message);
