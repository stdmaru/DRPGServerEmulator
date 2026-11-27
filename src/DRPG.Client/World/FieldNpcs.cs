using DRPG.Client.Data;
using DRPG.Client.Startup;
using DRPG.Client.Ui;
using DRPG.Engine.Rendering;
using DRPG.Shared.Formats.Map;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.World;

/// <summary>
/// One NPC entity on the field map (✅ v322 <c>0x00abb750</c> / <c>0x00abb8b0</c>, ctor <c>0x0085ebb0</c>,
/// init <c>0x0085f090</c>, vtable <c>0x010073f4</c>).
/// </summary>
public sealed class NpcEntity
{
    private int _idleCounter;
    private int _avatarFrame;
    private int _avatarStep;

    public NpcEntity(
        Serial serial,
        int npcId,
        string name,
        int cellX,
        int cellY,
        int direction,
        SpriteAnimator? sprite,
        FieldAnimator? animator,
        TamerDoll? avatar)
    {
        Serial = serial;
        NpcId = npcId;
        Name = name;
        CellX = cellX;
        CellY = cellY;
        Direction = direction is >= 0 and < 8 ? direction : 0;
        WorldX = cellX * MapFile.CellWidth + MapFile.CellWidth / 2;
        WorldY = cellY * MapFile.CellHeight + MapFile.CellHeight / 2;
        Sprite = sprite;
        Animator = animator;
        Avatar = avatar;
    }

    public Serial Serial { get; }
    public int NpcId { get; }
    public string Name { get; }
    public int CellX { get; }
    public int CellY { get; }
    public int Direction { get; }
    public int WorldX { get; }
    public int WorldY { get; }
    public SpriteAnimator? Sprite { get; }
    public FieldAnimator? Animator { get; }
    public TamerDoll? Avatar { get; }

    /// <summary>
    /// ✅ <c>0x00863430</c> + <c>0x00863b80</c> (standard .ani NPC) and <c>0x00862650</c> (Custom avatar NPC):
    /// advances the NPC animation by one logic step.
    /// </summary>
    public void Step(Random random)
    {
        if (Avatar is not null)
        {
            var range = Avatar.Body.Ani.Range(FieldAnimator.Standing, Direction);
            if (range.Count == 0)
                range = Avatar.Body.Ani.Range(FieldAnimator.Standing, 0);
            if (range.Count == 0)
                return;
            // ✅ 0x00862650 line 88: advances every 3 steps (counter == 2)
            if (_avatarStep >= 2)
            {
                _avatarStep = 0;
                _avatarFrame = (_avatarFrame + 1) % range.Count;
            }
            else
            {
                _avatarStep++;
            }
            Avatar.Body.ShowFrame(FieldAnimator.Standing, range.Start + _avatarFrame);
            return;
        }

        if (Animator is null)
            return;

        int previousOffset = Animator.Offset;
        Animator.Step();
        // ✅ 0x00863b80: when an idle variation (2 or 3) wraps around, return to Standing (1)
        if (Animator.Animation is FieldAnimator.IdleA or FieldAnimator.IdleB && Animator.Offset < previousOffset)
            Animator.Switch(FieldAnimator.Standing);

        // ✅ 0x00863430 line 30: every 200 steps, 1-in-4 chance of animation 3, else animation 2 (if available)
        _idleCounter++;
        if (_idleCounter >= FieldAnimator.ChangeStep)
        {
            _idleCounter = 0;
            int target = random.Next(4) == 0 ? FieldAnimator.IdleB : FieldAnimator.IdleA;
            Animator.Switch(target);
        }
    }
}

/// <summary>
/// Manages client-side NPCs loaded from <c>npc_info_us</c> on map entry and dynamic quest NPCs added/removed via S2C <c>0x09</c>
/// (✅ v322 <c>0x00ad3730</c>, <c>0x00abb750</c>, <c>0x00abb8b0</c>, <c>0x00abb9e0</c>, <c>0x00acfed0</c>, <c>0x00ad4a50</c>,
/// docs/cliente/cena-jogo.md#npcs-do-mapa).
/// </summary>
public sealed class FieldNpcs
{
    /// <summary>✅ <c>0x00ad3730</c> line 128: map <c>0x66</c> (102) skips the standard <c>npc_info_us</c> auto-spawn loop.</summary>
    public const int ExcludedAutoSpawnMapId = 0x66;

    private readonly record struct DynamicNpcRecord(int MapId, int NpcId, int X, int Y);

    private readonly List<NpcEntity> _list = [];
    private readonly List<DynamicNpcRecord> _dynamic = [];

    public IReadOnlyList<NpcEntity> All => _list;

    /// <summary>
    /// ✅ <c>0x00ad3730</c>: clears current map NPCs, spawns every <c>npc_info_us</c> row for <paramref name="mapId"/> with
    /// <c>col 9 == 1</c> (except map <c>0x66</c>), and re-spawns any dynamic <c>0x09</c> NPCs recorded for this map.
    /// </summary>
    public void LoadForMap(
        int mapId,
        GameData? data,
        FieldCollision? collision,
        AnimatedSprites? sprites,
        Random random)
    {
        ClearCurrent(collision);
        if (data is null)
            return;

        if (mapId != ExcludedAutoSpawnMapId)
        {
            foreach (var def in data.NpcsForMap(mapId))
                if (def.AutoSpawn)
                    Spawn(def, def.X, def.Y, def.Direction, data, collision, sprites, random);
        }

        // ✅ 0x00ad3730 lines 166-211: re-create dynamic 0x09 NPCs saved in +0x760 for this map
        foreach (var dyn in _dynamic)
        {
            if (dyn.MapId != mapId)
                continue;
            if (data.FindNpc(dyn.NpcId) is { } def)
                Spawn(def, dyn.X, dyn.Y, direction: 0, data, collision, sprites, random);
        }
    }

    /// <summary>
    /// ✅ <c>0x00ac7331</c> → <c>0x00abb9e0</c> (add) / <c>0x00acfed0</c> (remove): applies a S2C <c>0x09</c> dynamic NPC update.
    /// </summary>
    public void Apply(
        FieldNpcPacket packet,
        int currentMapId,
        GameData? data,
        FieldCollision? collision,
        AnimatedSprites? sprites,
        Random random)
    {
        if (packet.Action == FieldNpcPacket.ActionAdd)
        {
            if (packet.MapId != currentMapId || data is null)
                return;
            if (data.FindNpc(packet.NpcId) is not { } def)
                return;
            // ✅ 0x00abb9e0 lines 67-68: skip if an entry already exists at the same (mapId, x, y)
            foreach (var existing in _dynamic)
                if (existing.MapId == packet.MapId && existing.X == packet.X && existing.Y == packet.Y)
                    return;
            _dynamic.Add(new DynamicNpcRecord(packet.MapId, packet.NpcId, packet.X, packet.Y));
            Spawn(def, packet.X, packet.Y, direction: 0, data, collision, sprites, random);
            return;
        }

        if (packet.Action == FieldNpcPacket.ActionRemove)
        {
            // ✅ 0x00acfed0: mapId == 0 removes by npcId across all maps; otherwise only for that map
            bool removeFromCurrent = packet.MapId == 0 || packet.MapId == currentMapId;
            if (removeFromCurrent)
            {
                for (int i = _list.Count - 1; i >= 0; i--)
                {
                    if (_list[i].NpcId == packet.NpcId)
                    {
                        collision?.Vacate(_list[i].CellX, _list[i].CellY);
                        _list.RemoveAt(i);
                    }
                }
            }
            for (int i = _dynamic.Count - 1; i >= 0; i--)
            {
                if (_dynamic[i].NpcId == packet.NpcId && (packet.MapId == 0 || _dynamic[i].MapId == packet.MapId))
                    _dynamic.RemoveAt(i);
            }
        }
    }

    /// <summary>✅ <c>0x00afdba0</c> + <c>0x00afe1c0</c>: steps every active NPC on the map.</summary>
    public void Step(Random random)
    {
        foreach (var npc in _list)
            npc.Step(random);
    }

    /// <summary>✅ <c>0x00ad4a50</c> + <c>0x00860e50</c> / <c>0x00862650</c>: adds active NPCs to the field's Y-sorted draw queue.</summary>
    public void QueueDraw(
        List<DrawItem> queue,
        int cameraX,
        int cameraY,
        int mapId,
        SpriteOptions options,
        Action<IRenderer, SpriteAnimator, int, int> drawSprite)
    {
        bool tamerShadow = SpriteEffects.TamerHasShadow(mapId);
        foreach (var npc in _list)
        {
            int x = npc.WorldX - cameraX, y = npc.WorldY - cameraY;
            if (npc.Avatar is { } avatar)
            {
                // ✅ 0x00862650 line 174: avatar NPCs use the same paper-doll corner (x - 120, y - 162)
                queue.Add(new DrawItem(y, null, x - 120, y - 162, (r, drawX, drawY) => avatar.Draw(r, drawX, drawY, options, tamerShadow)));
                continue;
            }
            if (npc.Sprite?.Texture is not { } frame)
                continue;
            var (anchorX, anchorY) = npc.Sprite.Anchor;
            var sprite = npc.Sprite;
            queue.Add(new DrawItem(y, frame, x - anchorX, y - anchorY, (r, drawX, drawY) => drawSprite(r, sprite, drawX, drawY)));
        }
    }

    /// <summary>Clears active NPCs on the current map while keeping dynamic <c>0x09</c> records for map reloads.</summary>
    public void ClearCurrent(FieldCollision? collision)
    {
        if (collision is not null)
            foreach (var npc in _list)
                collision.Vacate(npc.CellX, npc.CellY);
        _list.Clear();
    }

    private void Spawn(
        NpcDefinition def,
        int x,
        int y,
        int direction,
        GameData data,
        FieldCollision? collision,
        AnimatedSprites? sprites,
        Random random)
    {
        int clampedDir = direction is >= 0 and < 8 ? direction : 0;
        TamerDoll? avatar = null;
        SpriteAnimator? sprite = null;
        FieldAnimator? animator = null;

        if (sprites is not null)
        {
            if (def.CustomAvatarIndex > 0)
            {
                int tamerId = data.CustomAvatarTamerId(def.CustomAvatarIndex);
                avatar = tamerId > 0 ? sprites.OpenTamer(tamerId) : null;
            }
            else
            {
                sprite = sprites.OpenNpc(def.Sprite);
                if (sprite is not null)
                {
                    animator = new FieldAnimator(sprite, digimon: false, species: 0, random.Next(8));
                    animator.SetDirection(clampedDir);
                }
            }
        }

        var serial = NewSerial();
        var npc = new NpcEntity(serial, def.NpcId, def.Name, x, y, clampedDir, sprite, animator, avatar);
        _list.Add(npc);
        // ✅ 0x0085e940: NPC occupies its cell (and cellY - 1 if empty) in the collision grid
        collision?.Occupy(npc.CellX, npc.CellY, npc.Serial);
    }

    private static Serial NewSerial()
    {
        Span<byte> bytes = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(bytes);
        return new Serial(BitConverter.ToUInt64(bytes[..8]), BitConverter.ToUInt64(bytes[8..]));
    }
}
