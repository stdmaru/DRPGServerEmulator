using System.Text;

namespace DRPG.Client.Text;

/// <summary>
/// The prohibited word list for character and digimon names (✅ v322 <c>0x00b3db80</c>,
/// docs/cliente/cenas.md#criação-de-personagem-modo-1--re). Loaded like <see cref="TextAttach"/>:
/// <c>table\ProhibitedWords_US.dat</c> and <c>Language\ProhibitedWords_US.bin</c> (UTF-8 with BOM, lines separated by
/// CR alone, <c>id|WORD</c>). A name is rejected when any non-empty substring of its uppercase form is in the set.
/// </summary>
public sealed class ProhibitedWords
{
    private readonly Dictionary<int, string> _byId = [];
    private HashSet<string>? _words;

    public int Count => _byId.Count;

    /// <summary>Adds the rows of <c>table\prohibitedwords_us.dat</c> (<c>id|word</c>). Malformed rows are skipped.</summary>
    public void AddTableRows(IEnumerable<string[]> rows)
    {
        foreach (var row in rows)
        {
            if (row.Length >= 2 && int.TryParse(row[0], out int id) && id >= 0)
            {
                _byId[id] = string.Join('|', row.Skip(1));
                _words = null;
            }
        }
    }

    /// <summary>Applies <c>Language\ProhibitedWords_US.bin</c> on top of the table rows. Bad lines are skipped.</summary>
    public void AddLanguageFile(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(Encoding.UTF8.Preamble))
            data = data[Encoding.UTF8.Preamble.Length..];
        foreach (var line in Encoding.UTF8.GetString(data).Split('\r'))
        {
            int bar = line.IndexOf('|');
            if (bar > 0 && int.TryParse(line.AsSpan(0, bar), out int id) && id >= 0)
            {
                _byId[id] = line[(bar + 1)..].TrimEnd('\n');
                _words = null;
            }
        }
    }

    /// <summary>✅ <c>0x00b3db80</c>: true if any non-empty substring of <paramref name="name"/> (uppercased) is listed.</summary>
    public bool ContainsProhibited(string name)
    {
        if (string.IsNullOrEmpty(name) || _byId.Count == 0)
            return false;
        var words = Words();
        if (words.Count == 0)
            return false;
        string upper = name.ToUpperInvariant();
        for (int start = 0; start < upper.Length; start++)
        {
            for (int length = 1; start + length <= upper.Length; length++)
            {
                if (words.Contains(upper.Substring(start, length)))
                    return true;
            }
        }
        return false;
    }

    private HashSet<string> Words()
    {
        if (_words is not null)
            return _words;
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in _byId.Values)
        {
            if (value.Length > 0)
                set.Add(value.ToUpperInvariant());
        }
        _words = set;
        return set;
    }
}
