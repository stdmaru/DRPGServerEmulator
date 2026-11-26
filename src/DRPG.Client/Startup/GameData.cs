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
    IReadOnlyDictionary<string, SpriteFile> sprites)
{
    public PackArchive Resource { get; } = resource;
    public PackArchive Table { get; } = table;

    /// <summary>Rows of each table by lower case name (<c>textattach</c>, <c>map</c>, …); a table the pack lacks is absent.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string[]>> Tables { get; } = tables;

    /// <summary>Raw <c>Language\*_US.bin</c> files by file name. ❓ Only <c>TextAttach</c> is applied so far.</summary>
    public IReadOnlyDictionary<string, byte[]> Language { get; } = language;

    public TextAttach Texts { get; } = texts;

    /// <summary>The sprites decoded while loading, by pack path; <c>SpriteStore.Adopt</c> takes them.</summary>
    public IReadOnlyDictionary<string, SpriteFile> Sprites { get; } = sprites;

    /// <summary>
    /// Name of a map for the character slots (✅ <c>0x00b395a0</c>): the second column of the <c>Map</c> row whose first
    /// column is the id. ❓ The language file of <c>Map</c> is not applied: its columns carry no ids.
    /// </summary>
    public string MapName(int mapId)
    {
        if (!Tables.TryGetValue("map", out var rows))
            return "";
        foreach (var row in rows)
            if (row.Length >= 2 && int.TryParse(row[0], out int id) && id == mapId)
                return row[1];
        return "";
    }
}
