using DRPG.Client.Text;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Formats.Sprite;

namespace DRPG.Client.Startup;

/// <summary>
/// What the loading screen loads (✅ v322 <c>0x00719af0</c> tables and <c>0x00ad3d30</c> resources): the table pack, every
/// table's rows, the language files, the interface texts and the sprites that were decoded ahead. Read-only: nothing
/// here is ever written back.
/// </summary>
public sealed class GameData(
    PackArchive resource,
    PackArchive table,
    IReadOnlyDictionary<string, IReadOnlyList<string[]>> tables,
    IReadOnlyDictionary<string, byte[]> language,
    TextAttach texts,
    ProhibitedWords prohibitedWords,
    IReadOnlyDictionary<string, SpriteFile> sprites)
{
    public PackArchive Resource { get; } = resource;
    public PackArchive Table { get; } = table;

    /// <summary>Rows of each table by lower case name (<c>textattach</c>, <c>map</c>, …); a table the pack lacks is absent.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string[]>> Tables { get; } = tables;

    /// <summary>Raw <c>Language\*_US.bin</c> files by file name. ❓ Only <c>TextAttach</c> and <c>ProhibitedWords</c> are applied so far.</summary>
    public IReadOnlyDictionary<string, byte[]> Language { get; } = language;

    public TextAttach Texts { get; } = texts;

    public ProhibitedWords ProhibitedWords { get; } = prohibitedWords;

    /// <summary>The sprites decoded while loading, by pack path; <c>SpriteStore.Adopt</c> takes them.</summary>
    public IReadOnlyDictionary<string, SpriteFile> Sprites { get; } = sprites;

    /// <summary>
    /// Name of a map for the character slots (✅ <c>0x00b395a0</c>): the second column of the <c>Map</c> row whose first
    /// column is the id. ❓ The language file of <c>Map</c> is not applied: its columns carry no ids.
    /// </summary>
    public string MapName(int mapId) => MapRow(mapId) is { Length: >= 2 } row ? row[1] : "";

    /// <summary>
    /// The pack path of a map's file (✅ docs/cliente/mapas.md): column 4 of the <c>Map</c> row is the code (<c>R079</c>) and the
    /// file is <c>map\r079.map</c>. Null when the map has no row or no code.
    /// </summary>
    public string? MapFile(int mapId) => MapRow(mapId) is { Length: >= 5 } row && row[4].Length > 0 ? $"map\\{row[4]}.map" : null;

    private string[]? MapRow(int mapId)
    {
        if (!Tables.TryGetValue("map", out var rows))
            return null;
        foreach (var row in rows)
            if (row.Length >= 1 && int.TryParse(row[0], out int id) && id == mapId)
                return row;
        return null;
    }
}
