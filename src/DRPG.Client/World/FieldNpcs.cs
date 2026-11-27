using DRPG.Client.Data;
using DRPG.Client.Startup;
using DRPG.Client.Ui;
using DRPG.Shared.Protocol;

namespace DRPG.Client.World;

/// <summary>
/// The NPCs standing on the current map (✅ v322 <c>0x00ad3730</c>, <c>0x00abb750</c>, <c>0x00abb8b0</c>, <c>0x00ad4a50</c>,
/// docs/cliente/campo.md#npcs-client-side-no-mapa-): the <c>npc_info_us</c> rows spawned when the map loads, plus the quest NPCs
/// that <c>0x09</c> adds. Each occupies its cell on the grid.
/// </summary>
public sealed class FieldNpcs(int mapId, NpcTable table, FieldCollision collision, IFieldSprites sprites, Random random)
{
    /// <summary>✅ <c>0x00ad3730</c> line 128: map <c>0x66</c> (102) skips the standard <c>npc_info_us</c> auto-spawn loop.</summary>
    public const int ExcludedAutoSpawnMapId = 0x66;

    /// <summary>
    /// ❓ The client makes the NPC serials itself (the server sends none); the high half marks them so they never equal a
    /// server serial on the grid.
    /// </summary>
    private const ulong ClientSerialMark = 0x4E50_4300_0000_0000;      // "NPC"

    private readonly List<NpcEntity> _list = [];
    private ulong _nextSerial;

    public IReadOnlyList<NpcEntity> All => _list;

    /// <summary>✅ <c>0x00ad3730</c>: spawns every <c>npc_info_us</c> row of the map with column 9 == 1 (none on map <c>0x66</c>).</summary>
    public void SpawnFromTable()
    {
        if (mapId == ExcludedAutoSpawnMapId)
            return;
        foreach (var npc in table.ForMap(mapId))
            if (npc.AutoSpawn)
                Spawn(npc, new Cell(npc.X, npc.Y), (Direction)npc.Direction);
    }

    /// <summary>✅ <c>0x00abb9e0</c>: spawns a quest NPC of the table at <paramref name="cell"/>, facing south; false when the id is not in the table.</summary>
    public bool SpawnQuestNpc(int npcId, Cell cell)
    {
        if (table.Find(npcId) is not { } npc)
            return false;
        Spawn(npc, cell, Direction.South);
        return true;
    }

    /// <summary>✅ <c>0x00acfed0</c>: removes every NPC of <paramref name="npcId"/> from the map.</summary>
    public void RemoveAll(int npcId)
    {
        for (int i = _list.Count - 1; i >= 0; i--)
        {
            if (_list[i].NpcId != npcId)
                continue;
            collision.Vacate(_list[i].Cell.X, _list[i].Cell.Y);
            _list.RemoveAt(i);
        }
    }

    /// <summary>✅ <c>0x00afdba0</c> + <c>0x00afe1c0</c>: steps every NPC on the map.</summary>
    public void Step()
    {
        foreach (var npc in _list)
            npc.Step(random);
    }

    /// <summary>✅ <c>0x00ad4a50</c> + <c>0x00860e50</c> / <c>0x00862650</c>: adds the NPCs to the field's y-sorted draw queue.</summary>
    public void QueueDraw(List<DrawItem> queue, int cameraX, int cameraY, EntityDrawer drawer)
    {
        foreach (var npc in _list)
        {
            int x = npc.WorldX - cameraX, y = npc.WorldY - cameraY;
            if (npc.Avatar is { } avatar)
                drawer.QueueTamer(queue, avatar, x, y);
            else if (npc.Sprite is { } sprite)
                drawer.QueueSprite(queue, sprite, species: 0, x, y);
        }
    }

    private void Spawn(NpcDefinition npc, Cell cell, Direction direction)
    {
        TamerDoll? avatar = null;
        SpriteAnimator? sprite = null;
        FieldAnimator? animator = null;
        if (npc.CustomAvatarIndex > 0)
        {
            if (table.CustomAvatarTamerId(npc.CustomAvatarIndex) is { } tamerId and > 0)
                avatar = sprites.OpenTamer(tamerId);
        }
        else
        {
            sprite = sprites.OpenNpc(npc.Sprite);
            if (sprite is not null)
            {
                animator = new FieldAnimator(sprite, digimon: false, species: 0, random.Next(8));
                animator.SetDirection(direction);
            }
        }

        var entity = new NpcEntity(new Serial(++_nextSerial, ClientSerialMark), npc.NpcId, npc.Name, cell, direction, sprite, animator, avatar);
        _list.Add(entity);
        // ✅ 0x0085e940: the NPC occupies its cell (and cellY − 1 when empty) on the grid
        collision.Occupy(cell.X, cell.Y, entity.Serial);
    }
}
