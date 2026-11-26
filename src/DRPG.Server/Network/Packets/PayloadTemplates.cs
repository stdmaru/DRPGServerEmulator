using System.Collections.Concurrent;
using System.Reflection;

namespace DRPG.Server.Network.Packets;

/// <summary>
/// Payloads capturados do servidor oficial que ainda não foram decodificados.
/// Ficam em Network/Packets/Templates/*.hex (texto hexadecimal, embutido no assembly).
/// Cada pacote que usa um template deve sobrescrever os campos conhecidos por offset.
/// </summary>
public static class PayloadTemplates
{
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();

    public static ReadOnlySpan<byte> Get(string name) => Cache.GetOrAdd(name, Load);

    private static byte[] Load(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream($"Templates.{name}")
            ?? throw new InvalidOperationException($"template de payload '{name}' não encontrado");
        using var reader = new StreamReader(stream);
        var hex = new System.Text.StringBuilder();
        while (reader.ReadLine() is { } line)
        {
            int comment = line.IndexOf('#');
            var data = comment >= 0 ? line[..comment] : line;
            foreach (char c in data)
                if (Uri.IsHexDigit(c)) hex.Append(c);
        }
        return Convert.FromHexString(hex.ToString());
    }
}
