using System.Collections.Frozen;

namespace DRPG.Client.Startup;

/// <summary>One row of <c>npc_info_us</c> (✅ <c>0x007231e0</c>, docs/cliente/campo.md#npcs-client-side-no-mapa-).</summary>
/// <param name="Direction">Column 3, 0..7 (checked on load).</param>
/// <param name="AutoSpawn">✅ Column 9 == 1: spawned when the map loads (<c>0x00ad3730</c>).</param>
/// <param name="CustomAvatarIndex">✅ 1-based row of <c>Custom_AvatarNPC</c> for a <c>Custom_N</c> sprite, 0 otherwise.</param>
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

/// <summary>
/// The NPCs of <c>npc_info_us</c> and the tamer bodies of <c>Custom_AvatarNPC</c>, read once when the tables load (✅ <c>0x007231e0</c>,
/// <c>0x00722ce0</c>, <c>0x007230e0</c>). A malformed row stops the load with its line: the table is data, not something to skip.
/// </summary>
public sealed class NpcTable
{
    /// <summary>✅ <c>0x007231e0</c>: the columns a row needs (map, id, name, direction, x, y, sprite, icon, …, auto-spawn at 9).</summary>
    public const int MinColumns = 10;

    /// <summary>
    /// ✅ <c>0x007231e0</c>: a sprite name containing this is an avatar NPC, and <c>atoi</c> of the name after its 7th byte
    /// (<c>Custom_1</c> → 1) is the <c>Custom_AvatarNPC</c> row (0, no avatar, when it is not a number).
    /// </summary>
    private const string CustomMarker = "Custom";
    private const int CustomNumberStart = 7;

    private readonly FrozenDictionary<int, NpcDefinition> _byId;
    private readonly FrozenDictionary<int, NpcDefinition[]> _byMap;
    private readonly int[] _customTamers;

    private NpcTable(IReadOnlyList<NpcDefinition> npcs, int[] customTamers)
    {
        var byId = new Dictionary<int, NpcDefinition>();
        foreach (var npc in npcs)
            byId.TryAdd(npc.NpcId, npc);                                    // ✅ 0x00722ce0 returns the first row of an id
        _byId = byId.ToFrozenDictionary();
        _byMap = npcs.GroupBy(npc => npc.MapId).ToFrozenDictionary(group => group.Key, group => group.ToArray());
        _customTamers = customTamers;
    }

    public static NpcTable Empty { get; } = new([], []);

    /// <summary>Every row of <paramref name="mapId"/>, in table order.</summary>
    public IReadOnlyList<NpcDefinition> ForMap(int mapId) => _byMap.TryGetValue(mapId, out var npcs) ? npcs : [];

    /// <summary>✅ <c>0x00722ce0</c>: the first row of <paramref name="npcId"/>.</summary>
    public NpcDefinition? Find(int npcId) => _byId.GetValueOrDefault(npcId);

    /// <summary>✅ <c>0x007230e0</c>: the tamer body of the 1-based <c>Custom_AvatarNPC</c> row, null when there is no such row.</summary>
    public int? CustomAvatarTamerId(int customIndex) =>
        customIndex >= 1 && customIndex <= _customTamers.Length ? _customTamers[customIndex - 1] : null;

    /// <summary>Reads the rows of <c>NPC_INFO</c> and <c>Custom_AvatarNPC</c>; a table the pack lacks is empty.</summary>
    /// <exception cref="InvalidDataException">A row is too short or a number column is not a number.</exception>
    public static NpcTable From(IReadOnlyDictionary<string, IReadOnlyList<string[]>> tables)
    {
        var npcs = new List<NpcDefinition>();
        if (tables.TryGetValue("NPC_INFO", out var rows))
            for (int i = 0; i < rows.Count; i++)
                npcs.Add(ParseNpc(rows[i], i + 1));

        var customTamers = new List<int>();
        if (tables.TryGetValue("Custom_AvatarNPC", out var avatarRows))
            for (int i = 0; i < avatarRows.Count; i++)
                customTamers.Add(Number(avatarRows[i], 0, "Custom_AvatarNPC", i + 1));
        return new NpcTable(npcs, customTamers.ToArray());
    }

    private static NpcDefinition ParseNpc(string[] row, int line)
    {
        if (row.Length < MinColumns)
            throw new InvalidDataException($"NPC_INFO line {line}: {row.Length} columns, {MinColumns} needed");
        int direction = Number(row, 3, "NPC_INFO", line);
        if (direction is < 0 or > 7)
            throw new InvalidDataException($"NPC_INFO line {line}: direction {direction} is not 0..7");
        var sprite = row[6];
        int customIndex = 0;
        if (sprite.Contains(CustomMarker, StringComparison.OrdinalIgnoreCase) && sprite.Length > CustomNumberStart
            && int.TryParse(sprite.AsSpan(CustomNumberStart), out int parsed))
            customIndex = parsed;
        return new NpcDefinition(
            Number(row, 0, "NPC_INFO", line),
            Number(row, 1, "NPC_INFO", line),
            row[2],
            direction,
            Number(row, 4, "NPC_INFO", line),
            Number(row, 5, "NPC_INFO", line),
            sprite,
            Number(row, 9, "NPC_INFO", line) == 1,
            customIndex);
    }

    private static int Number(string[] row, int column, string table, int line)
    {
        if (column >= row.Length || !int.TryParse(row[column], out int value))
            throw new InvalidDataException($"{table} line {line}: column {column} is not a number");
        return value;
    }
}
