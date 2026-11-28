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

    /// <summary>The NPCs of <c>npc_info_us</c>, checked when the tables load.</summary>
    public NpcTable Npcs { get; } = NpcTable.From(tables);

    /// <summary>✅ The wild digimon's full names of <c>monsterregen_us</c>, checked when the tables load.</summary>
    public MonsterRegenTable MonsterRegen { get; } = MonsterRegenTable.From(tables);

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

    /// <summary>
    /// ✅ The four portals of the map's <c>Map</c> row (<c>+0x84</c>, step 0x24; columns 16 + 4·n: destination name, on, x, y), which
    /// the minimap marks (<c>0x00b9b930</c>). A row without those columns has none.
    /// </summary>
    public IReadOnlyList<MapPortalInfo> MapPortals(int mapId)
    {
        if (MapRow(mapId) is not { Length: >= FirstPortalColumn + PortalCount * PortalColumns } row)
            return [];
        var portals = new List<MapPortalInfo>(PortalCount);
        for (int i = 0; i < PortalCount; i++)
        {
            int column = FirstPortalColumn + i * PortalColumns;
            portals.Add(new MapPortalInfo(row[column], Int(row[column + 1]) != 0, Int(row[column + 2]), Int(row[column + 3])));
        }
        return portals;

        // ✅ atoi, as the row parser (0x0078e480) reads the numbers: anything else is 0
        static int Int(string text) => int.TryParse(text, out int value) ? value : 0;
    }

    /// <summary>✅ The portal columns of <c>map_us</c>: 4 portals of 4 columns from column 16.</summary>
    public const int FirstPortalColumn = 16, PortalCount = 4, PortalColumns = 4;

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

/// <summary>✅ A portal of a <c>Map</c> row: the destination's name (the minimap's tooltip), on, and its cell.</summary>
public sealed record MapPortalInfo(string Name, bool Enabled, int X, int Y);
