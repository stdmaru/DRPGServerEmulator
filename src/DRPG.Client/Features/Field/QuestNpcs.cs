using DRPG.Client.World;
using DRPG.Shared.Packets.Map;

namespace DRPG.Client.Features.Field;

/// <summary>A quest NPC the server placed with <c>0x09</c>.</summary>
public readonly record struct QuestNpc(int MapId, int NpcId, Cell Cell);

/// <summary>
/// ✅ The quest NPCs <c>0x09</c> added, kept for the whole game run (<c>+0x760</c>, <c>0x00abb9e0</c> / <c>0x00acfed0</c>) so a map
/// spawns them again whenever it is loaded (<c>0x00ad3730</c> lines 166-211).
/// </summary>
public sealed class QuestNpcs
{
    private readonly List<QuestNpc> _list = [];

    public IReadOnlyList<QuestNpc> All => _list;

    /// <summary>✅ <c>0x00abb9e0</c> lines 67-68: false (nothing added) when one is already recorded on the same map and cell.</summary>
    public bool Add(QuestNpc npc)
    {
        foreach (var existing in _list)
            if (existing.MapId == npc.MapId && existing.Cell == npc.Cell)
                return false;
        _list.Add(npc);
        return true;
    }

    /// <summary>✅ <c>0x00acfed0</c>: forgets <paramref name="npcId"/> on <paramref name="mapId"/>, or on every map when <paramref name="mapId"/> is 0.</summary>
    public void Remove(int mapId, int npcId) =>
        _list.RemoveAll(npc => npc.NpcId == npcId && (mapId == FieldNpcPacket.AllMaps || npc.MapId == mapId));
}

