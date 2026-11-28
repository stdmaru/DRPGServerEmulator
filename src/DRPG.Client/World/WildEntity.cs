using DRPG.Client.Ui;
using DRPG.Shared.Protocol;

namespace DRPG.Client.World;

/// <summary>What the server tells about a wild digimon group when it first appears (✅ <c>0x11</c>, <c>0x00abb5b0</c>).</summary>
public sealed record WildSpawn(Serial Serial, string Name, int SpeciesId, int Stage, int Level, byte Grade, Cell Cell, Direction Direction);

/// <summary>One wild digimon group on the field (✅ v322 <c>0x00abb5b0</c>, vtable <c>0x010071c8</c>).</summary>
public sealed class WildEntity(WildSpawn spawn, SpriteAnimator? sprite, FieldAnimator? animator)
{
    public Serial Serial { get; } = spawn.Serial;
    public string Name { get; } = spawn.Name;
    public int SpeciesId { get; } = spawn.SpeciesId;
    public int Stage { get; } = spawn.Stage;
    public int Level { get; } = spawn.Level;
    public byte Grade { get; } = spawn.Grade;
    public FieldEntity Entity { get; } = new(spawn.Cell, spawn.Direction);
    public SpriteAnimator? Sprite { get; } = sprite;
    public FieldAnimator? Animator { get; } = animator;
}
