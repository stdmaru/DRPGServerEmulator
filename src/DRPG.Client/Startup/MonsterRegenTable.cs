namespace DRPG.Client.Startup;

/// <summary>One row of <c>monsterregen_us</c> (✅ <c>CTableClass_RegenInfo</c>, row parser <c>0x007b19b0</c>): id, species, name, …</summary>
public sealed record MonsterRegenRow(int Id, int SpeciesId, string Name);

/// <summary>
/// ✅ <c>MonsterRegen</c> (table group 19 = <c>0x13</c>, <c>CTableClass_RegenInfo</c>): the wild digimon's full names. The <c>0x11</c>
/// carries the name in 21 bytes, so the original's hover pass (<c>0x00a0d340</c>, <c>0x00a0f669</c>) looks the name up here by
/// species and first 20 characters and shows the table's whole name. A malformed row stops the load with its line.
/// </summary>
public sealed class MonsterRegenTable
{
    /// <summary>✅ <c>0x007b19b0</c>: six columns (id, species, name, then three numbers).</summary>
    public const int Columns = 6;

    /// <summary>✅ <c>0x00918860(…, 0, 0x14)</c>: the part of the table's name compared with the packet's.</summary>
    public const int ComparedLength = 20;

    private readonly MonsterRegenRow[] _rows;

    private MonsterRegenTable(MonsterRegenRow[] rows) => _rows = rows;

    public static MonsterRegenTable Empty { get; } = new([]);

    public IReadOnlyList<MonsterRegenRow> Rows => _rows;

    /// <summary>
    /// ✅ <c>0x00a0f6d2</c>…<c>0x00a0f7c6</c>: the name of the first row, in file order, with this species whose name begins with
    /// <paramref name="name"/> over its first 20 characters; <paramref name="name"/> itself when no row matches.
    /// </summary>
    public string FullName(int speciesId, string name)
    {
        foreach (var row in _rows)
        {
            if (row.SpeciesId != speciesId)
                continue;
            var compared = row.Name.Length > ComparedLength ? row.Name[..ComparedLength] : row.Name;
            if (string.Equals(compared, name, StringComparison.Ordinal))
                return row.Name;
        }
        return name;
    }

    /// <summary>Reads the rows of <c>MonsterRegen</c>; a table the pack lacks is empty.</summary>
    /// <exception cref="InvalidDataException">A row is too short or a number column is not a number.</exception>
    public static MonsterRegenTable From(IReadOnlyDictionary<string, IReadOnlyList<string[]>> tables)
    {
        if (!tables.TryGetValue("MonsterRegen", out var rows))
            return Empty;
        var parsed = new MonsterRegenRow[rows.Count];
        for (int i = 0; i < rows.Count; i++)
            parsed[i] = Parse(rows[i], i + 1);
        return new MonsterRegenTable(parsed);
    }

    private static MonsterRegenRow Parse(string[] row, int line)
    {
        if (row.Length < Columns)
            throw new InvalidDataException($"MonsterRegen line {line}: {row.Length} columns, {Columns} needed");
        return new MonsterRegenRow(Number(row, 0, line), Number(row, 1, line), row[2]);
    }

    private static int Number(string[] row, int column, int line)
    {
        if (!int.TryParse(row[column], out int value))
            throw new InvalidDataException($"MonsterRegen line {line}: column {column} is not a number");
        return value;
    }
}
