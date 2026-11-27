using DRPG.Client.Ui;
using DRPG.Engine.Rendering;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.World;

/// <summary>One wild digimon group on the field (✅ v322 <c>0x00abb5b0</c>, vtable <c>0x010071c8</c>).</summary>
public sealed class WildEntity
{
    public WildEntity(FieldDigimonPacket packet, SpriteAnimator? sprite, FieldAnimator? animator)
    {
        Serial = packet.Serial;
        Name = packet.Name;
        SpeciesId = packet.SpeciesId;
        Stage = packet.Stage;
        Level = packet.Level;
        Grade = packet.Grade;
        Entity = new FieldEntity(packet.X, packet.Y, packet.Direction);
        Sprite = sprite;
        Animator = animator;
    }

    public Serial Serial { get; }
    public string Name { get; }
    public int SpeciesId { get; }
    public int Stage { get; }
    public int Level { get; }
    public byte Grade { get; }
    public FieldEntity Entity { get; }
    public SpriteAnimator? Sprite { get; }
    public FieldAnimator? Animator { get; }
}

/// <summary>
/// Manages the server's wild digimon groups on the field map (✅ v322 <c>0x00af25c0</c>, <c>0x00abb5b0</c>,
/// <c>0x00afdba0</c>, <c>0x007f3df0</c>, <c>0x007f4b10</c>, <c>0x00859340</c>, <c>0x007e83d0</c>, docs/cliente/campo.md).
/// </summary>
public sealed class FieldWilds
{
    /// <summary>✅ <c>0x00af25c0</c>: <c>0x11</c> is ignored on map <c>0xFA</c> (250).</summary>
    public const int IgnoredMapId = 0xFA;

    /// <summary>✅ <c>0x00af25c0</c>: state 12 removes the group immediately.</summary>
    public const byte LeavingState = 12;

    /// <summary>✅ <c>0x00afdba0</c> → <c>0x0085e760(tamer, 10, 12)</c>: entities beyond 10 × 12 cells of the tamer are dropped.</summary>
    public const int KeepRangeX = 10, KeepRangeY = 12;

    private readonly List<WildEntity> _list = [];
    private readonly Dictionary<Serial, WildEntity> _bySerial = [];

    public IReadOnlyList<WildEntity> All => _list;

    public bool TryGet(Serial serial, out WildEntity wild) => _bySerial.TryGetValue(serial, out wild!);

    /// <summary>✅ <c>0x00af25c0</c>: applies a <c>0x11</c> packet (removes on state 12; finds/creates and enqueues command otherwise).</summary>
    public void Apply(
        FieldDigimonPacket packet,
        int mapId,
        FieldCollision? collision,
        Func<int, SpriteAnimator?> openSprite,
        Random random)
    {
        if (mapId == IgnoredMapId)
            return;

        if (packet.State == LeavingState)
        {
            Remove(packet.Serial, collision);
            return;
        }

        if (!_bySerial.TryGetValue(packet.Serial, out var wild))
        {
            var sprite = openSprite(packet.SpeciesId);
            var animator = sprite is not null
                ? new FieldAnimator(sprite, digimon: true, packet.SpeciesId, random.Next(8))
                : null;
            wild = new WildEntity(packet, sprite, animator);
            _list.Add(wild);
            _bySerial[packet.Serial] = wild;
            collision?.Occupy(wild.Entity.CellX, wild.Entity.CellY, wild.Serial);
        }

        // ✅ 0x007f3b30: enqueue the command; read at cell centres in Step
        wild.Entity.EnqueueCommand(new MoveCommand(
            (MoveState)packet.State,
            packet.X,
            packet.Y,
            packet.DestX,
            packet.DestY,
            packet.Direction));
    }

    /// <summary>
    /// ✅ <c>0x00afdba0</c> + <c>0x007f3df0</c>: removes wilds beyond 10 × 12 cells of <paramref name="tamer"/>,
    /// pops queued commands at cell centres, steps movement via <paramref name="pathfinder"/> and advances animations.
    /// </summary>
    public void Step(FieldEntity tamer, FieldCollision? collision, FieldPathfinder? pathfinder, Random random)
    {
        for (int i = _list.Count - 1; i >= 0; i--)
        {
            var wild = _list[i];
            if (Math.Abs(wild.Entity.CellX - tamer.CellX) > KeepRangeX
                || Math.Abs(wild.Entity.CellY - tamer.CellY) > KeepRangeY)
            {
                collision?.Vacate(wild.Entity.CellX, wild.Entity.CellY);
                _bySerial.Remove(wild.Serial);
                _list.RemoveAt(i);
                continue;
            }

            int oldX = wild.Entity.CellX, oldY = wild.Entity.CellY;
            if (wild.Entity.IsAtCellCenter)
                wild.Entity.ProcessNextCommand(isWild: true);
            wild.Entity.StepPhysics(pathfinder is not null ? pathfinder.NextStep : null);

            if (collision is not null)
            {
                if (wild.Entity.CellX != oldX || wild.Entity.CellY != oldY)
                    collision.Vacate(oldX, oldY);
                collision.Occupy(wild.Entity.CellX, wild.Entity.CellY, wild.Serial);
            }

            if (wild.Animator is { } animator)
            {
                animator.SetDirection(wild.Entity.Direction);
                if (wild.Entity.IsMoving)
                {
                    int target = (int)wild.Entity.State;
                    if (animator.Animation != target)
                        animator.Switch(target);
                }
                else if (animator.Animation >= FieldAnimator.Walk)
                {
                    animator.Switch(FieldAnimator.Standing);
                }
                animator.Step(random.Next(4));
            }
        }
    }

    /// <summary>✅ <c>0x007e83d0</c>: adds all visible wild digimons to the field's Y-sorted draw queue.</summary>
    public void QueueDraw(
        List<DrawItem> queue,
        int cameraX,
        int cameraY,
        Func<int, bool> isLowerSpecies,
        Action<IRenderer, SpriteAnimator, int, int> drawDigimon)
    {
        foreach (var wild in _list)
        {
            if (wild.Sprite?.Texture is not { } frame)
                continue;
            var (anchorX, anchorY) = wild.Sprite.Anchor;
            int x = wild.Entity.WorldX - cameraX, y = wild.Entity.WorldY - cameraY;
            int lower = isLowerSpecies(wild.SpeciesId) ? 32 : 0;
            var sprite = wild.Sprite;
            queue.Add(new DrawItem(y, frame, x - anchorX, y - anchorY + lower, (r, drawX, drawY) => drawDigimon(r, sprite, drawX, drawY)));
        }
    }

    public void Clear(FieldCollision? collision)
    {
        if (collision is not null)
            foreach (var wild in _list)
                collision.Vacate(wild.Entity.CellX, wild.Entity.CellY);
        _list.Clear();
        _bySerial.Clear();
    }

    private void Remove(Serial serial, FieldCollision? collision)
    {
        if (!_bySerial.Remove(serial, out var wild))
            return;
        collision?.Vacate(wild.Entity.CellX, wild.Entity.CellY);
        _list.Remove(wild);
    }
}
