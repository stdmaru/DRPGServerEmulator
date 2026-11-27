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

    /// <summary>
    /// All NPC rows from <c>npc_info_us</c> belonging to <paramref name="mapId"/> (✅ <c>0x007231e0</c>, <c>0x00ad3730</c>).
    /// </summary>
    public IReadOnlyList<NpcDefinition> NpcsForMap(int mapId)
    {
        if (!Tables.TryGetValue("NPC_INFO", out var rows))
            return [];
        var list = new List<NpcDefinition>();
        foreach (var row in rows)
            if (TryParseNpc(row, out var npc) && npc.MapId == mapId)
                list.Add(npc);
        return list;
    }

    /// <summary>Finds one NPC definition in <c>npc_info_us</c> by its id (✅ <c>0x00722ce0</c>).</summary>
    public NpcDefinition? FindNpc(int npcId)
    {
        if (!Tables.TryGetValue("NPC_INFO", out var rows))
            return null;
        foreach (var row in rows)
            if (TryParseNpc(row, out var npc) && npc.NpcId == npcId)
                return npc;
        return null;
    }

    /// <summary>
    /// Tamer body id for a <c>Custom_N</c> avatar NPC from <c>Custom_AvatarNPC</c> (✅ <c>0x007230e0</c>, 1-based index).
    /// Returns 0 if the table or row is absent.
    /// </summary>
    public int CustomAvatarTamerId(int customIndex)
    {
        if (customIndex < 1 || !Tables.TryGetValue("Custom_AvatarNPC", out var rows) || customIndex > rows.Count)
            return 0;
        var row = rows[customIndex - 1];
        return row.Length >= 1 && int.TryParse(row[0], out int tamerId) ? tamerId : 0;
    }

    private static bool TryParseNpc(string[] row, out NpcDefinition npc)
    {
        npc = default!;
        if (row.Length < 10
            || !int.TryParse(row[0], out int mapId)
            || !int.TryParse(row[1], out int npcId)
            || !int.TryParse(row[3], out int dir)
            || !int.TryParse(row[4], out int x)
            || !int.TryParse(row[5], out int y)
            || !int.TryParse(row[9], out int autoSpawn))
            return false;
        var sprite = row[6];
        int customIndex = 0;
        // ✅ 0x007231e0: sprite containing "Custom" (e.g. "Custom_1") parses the 1-based avatar index after byte 7
        if (sprite.Contains("Custom", StringComparison.OrdinalIgnoreCase) && sprite.Length > 7
            && int.TryParse(sprite.AsSpan(7), out int parsed))
            customIndex = parsed;
        npc = new NpcDefinition(mapId, npcId, row[2], dir, x, y, sprite, autoSpawn == 1, customIndex);
        return true;
    }

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

/// <summary>One row of <c>npc_info_us</c> (✅ <c>0x007231e0</c>, docs/cliente/cena-jogo.md#npcs-do-mapa).</summary>
public sealed record NpcDefinition(
    int MapId,
    int NpcId,
    string Name,
    int Direction,
    int X,
    int Y,
    string Sprite,
    bool AutoSpawn,
    int CustomAvatarIndex);
