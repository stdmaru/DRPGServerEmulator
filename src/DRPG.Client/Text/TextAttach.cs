using System.Text;

namespace DRPG.Client.Text;

/// <summary>
/// The interface texts by id (✅ v322 <c>FUN_0091b950(id)</c>, the <c>TextAttach</c> table, docs/cliente/cenas.md#texto-som-e-fontes-).
/// The rows come from <c>table\textattach_us.dat</c> (<c>id|text</c> once decoded) and <c>Language\TextAttach_US.bin</c>
/// (UTF-8 with BOM, lines separated by CR alone, <c>id|text</c>) replaces them. An id nobody defined is
/// <see cref="MissingText"/>, as in the original. ❓ <c>†</c> inside a text is a line break in the original; it is kept as it is here.
/// </summary>
public sealed class TextAttach
{
    public const string MissingText = "Null Text";

    private readonly Dictionary<int, string> _texts = [];

    public int Count => _texts.Count;

    public string this[int id] => _texts.GetValueOrDefault(id, MissingText);

    public bool Contains(int id) => _texts.ContainsKey(id);

    /// <summary>Adds the rows of the table (<c>id|text</c>, the text may hold more <c>|</c>). Rows that are not a number and text are skipped.</summary>
    public void AddTableRows(IEnumerable<string[]> rows)
    {
        foreach (var row in rows)
            if (row.Length >= 2 && int.TryParse(row[0], out int id) && id >= 0)
                _texts[id] = string.Join('|', row.Skip(1));
    }

    /// <summary>Applies a language file on top of what is there. Bad lines are skipped, never an error.</summary>
    public void AddLanguageFile(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(Encoding.UTF8.Preamble))
            data = data[Encoding.UTF8.Preamble.Length..];
        foreach (var line in Encoding.UTF8.GetString(data).Split('\r'))
        {
            int bar = line.IndexOf('|');
            if (bar > 0 && int.TryParse(line.AsSpan(0, bar), out int id) && id >= 0)
                _texts[id] = line[(bar + 1)..].TrimEnd('\n');
        }
    }
}
