using System.Text;

namespace DRPG.Launcher;

/// <summary>
/// Minimal Windows-style INI reading and in-place editing (<c>drpg-launcher.ini</c>, the game's <c>option.ini</c>).
/// Editing keeps every other line as it was, the way <c>WritePrivateProfileString</c> does, so the game's own
/// sections in <c>option.ini</c> survive.
/// </summary>
public static class IniFile
{
    /// <summary>Values keyed by <c>section.key</c>, both lower case. <c>;</c> starts a comment.</summary>
    public static Dictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string section = "";
        foreach (var raw in lines)
        {
            var line = StripComment(raw);
            if (line.Length == 0)
                continue;
            if (IsSection(line, out var name))
            {
                section = name.ToLowerInvariant();
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq > 0)
                values[$"{section}.{line[..eq].Trim().ToLowerInvariant()}"] = line[(eq + 1)..].Trim();
        }
        return values;
    }

    public static Dictionary<string, string> Read(string path) =>
        File.Exists(path) ? Parse(File.ReadAllLines(path, Encoding.Latin1)) : new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sets <paramref name="key"/> in <paramref name="section"/>, adding either when missing. Atomic write.</summary>
    public static void Set(string path, string section, string key, string value)
    {
        if (value.AsSpan().IndexOfAny('\r', '\n') >= 0)
            throw new ArgumentException("INI values cannot span lines", nameof(value));
        var lines = File.Exists(path) ? File.ReadAllLines(path, Encoding.Latin1).ToList() : [];
        int sectionStart = -1, sectionEnd = lines.Count;
        for (int i = 0; i < lines.Count; i++)
        {
            if (!IsSection(StripComment(lines[i]), out var name))
                continue;
            if (sectionStart >= 0)
            {
                sectionEnd = i;
                break;
            }
            if (name.Equals(section, StringComparison.OrdinalIgnoreCase))
                sectionStart = i;
        }

        var entry = $"{key}={value}";
        if (sectionStart < 0)
        {
            lines.Add($"[{section}]");
            lines.Add(entry);
        }
        else
        {
            int existing = -1;
            for (int i = sectionStart + 1; i < sectionEnd && existing < 0; i++)
            {
                var line = StripComment(lines[i]);
                int eq = line.IndexOf('=');
                if (eq > 0 && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    existing = i;
            }
            if (existing >= 0)
                lines[existing] = entry;
            else
            {
                // after the section's last non-blank line, so a blank separator stays between sections
                int insert = sectionEnd;
                while (insert > sectionStart + 1 && string.IsNullOrWhiteSpace(lines[insert - 1]))
                    insert--;
                lines.Insert(insert, entry);
            }
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, string.Join("\r\n", lines) + "\r\n", Encoding.Latin1);
        File.Move(temp, path, overwrite: true);
    }

    private static string StripComment(string raw)
    {
        int comment = raw.IndexOf(';');
        return (comment >= 0 ? raw[..comment] : raw).Trim();
    }

    private static bool IsSection(string line, out string name)
    {
        bool section = line.Length >= 2 && line[0] == '[' && line[^1] == ']';
        name = section ? line[1..^1].Trim() : "";
        return section;
    }
}
