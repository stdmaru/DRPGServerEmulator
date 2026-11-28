using DRPG.Client.Data;
using DRPG.Client.Ui;
using DRPG.Engine.Rendering;
using DRPG.Shared.Protocol;

namespace DRPG.Client.World;

/// <summary>
/// The server's wild digimon groups on the current map (✅ v322 <c>0x00abb5b0</c>, <c>0x00afdba0</c>, <c>0x007f3df0</c>,
/// <c>0x007f4b10</c>, <c>0x00859340</c>, <c>0x007e83d0</c>, docs/cliente/campo.md). The <c>0x11</c> handler adds, removes and
/// queues commands; each logic step the groups walk on the map's grid.
/// </summary>
public sealed class FieldWilds(FieldCollision collision, IStepResolver steps, IFieldSprites sprites, Random random)
{
    /// <summary>✅ <c>0x00afdba0</c> → <c>0x0085e760(tamer, 10, 12)</c>: entities beyond 10 × 12 cells of the tamer are dropped.</summary>
    public const int KeepRangeX = 10, KeepRangeY = 12;

    private readonly List<WildEntity> _list = [];
    private readonly Dictionary<Serial, WildEntity> _bySerial = [];

    public IReadOnlyList<WildEntity> All => _list;

    public WildEntity? Find(Serial serial) => _bySerial.GetValueOrDefault(serial);

    /// <summary>✅ <c>0x00abb5b0</c>: creates the group on its cell (<c>rand() % 8</c> for the phase of its animation) and occupies it.</summary>
    public WildEntity Add(WildSpawn spawn)
    {
        var sprite = sprites.OpenDigimon(spawn.SpeciesId);
        var animator = sprite is not null ? new FieldAnimator(sprite, digimon: true, spawn.SpeciesId, random.Next(8)) : null;
        var wild = new WildEntity(spawn, sprite, animator);
        _list.Add(wild);
        _bySerial[wild.Serial] = wild;
        collision.Occupy(wild.Entity.Cell.X, wild.Entity.Cell.Y, wild.Serial);
        return wild;
    }

    /// <summary>✅ <c>0x00af25c0</c> state 12: removes the group at once.</summary>
    public void Remove(Serial serial)
    {
        if (!_bySerial.Remove(serial, out var wild))
            return;
        collision.Vacate(wild.Entity.Cell.X, wild.Entity.Cell.Y);
        _list.Remove(wild);
    }

    /// <summary>
    /// ✅ <c>0x00afdba0</c> + <c>0x007f3df0</c>: drops the groups beyond 10 × 12 cells of <paramref name="tamer"/>, pops queued
    /// commands at cell centres, walks and advances the animations.
    /// </summary>
    public void Step(FieldEntity tamer)
    {
        for (int i = _list.Count - 1; i >= 0; i--)
        {
            var wild = _list[i];
            var entity = wild.Entity;
            if (Math.Abs(entity.Cell.X - tamer.Cell.X) > KeepRangeX || Math.Abs(entity.Cell.Y - tamer.Cell.Y) > KeepRangeY)
            {
                collision.Vacate(entity.Cell.X, entity.Cell.Y);
                _bySerial.Remove(wild.Serial);
                _list.RemoveAt(i);
                continue;
            }

            var oldCell = entity.Cell;
            if (entity.IsAtCellCenter)
                entity.ProcessNextCommand(CommandRule.Wild);
            entity.StepPhysics(steps);
            collision.Move(oldCell, entity.Cell, wild.Serial);

            if (wild.Animator is not { } animator)
                continue;
            animator.Follow(entity);
            animator.Step(random.Next(4));                                  // ✅ 1 in 4 for the animation 3
            if (!entity.IsMoving && animator.Animation is FieldAnimator.Standing or FieldAnimator.IdleA or FieldAnimator.IdleB)
                entity.SetIdleState((MoveState)animator.Animation);
        }
    }

    /// <summary>✅ <c>0x007e83d0</c>: adds all visible wild digimons to the field's Y-sorted draw queue.</summary>
    public void QueueDraw(List<DrawItem> queue, int cameraX, int cameraY, EntityDrawer drawer)
    {
        foreach (var wild in _list)
            if (wild.Sprite is { } sprite)
                drawer.QueueSprite(queue, sprite, wild.SpeciesId, wild.Entity.WorldX - cameraX, wild.Entity.WorldY - cameraY);
    }

    /// <summary>Frees every group's cell (the map is left).</summary>
    public void Clear()
    {
        foreach (var wild in _list)
            collision.Vacate(wild.Entity.Cell.X, wild.Entity.Cell.Y);
        _list.Clear();
        _bySerial.Clear();
    }
}
