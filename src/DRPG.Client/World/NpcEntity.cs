using DRPG.Client.Data;
using DRPG.Client.Ui;
using DRPG.Shared.Protocol;

namespace DRPG.Client.World;

/// <summary>
/// One NPC on the field map (✅ v322 <c>0x00abb750</c> / <c>0x00abb8b0</c>, ctor <c>0x0085ebb0</c>, init <c>0x0085f090</c>,
/// vtable <c>0x010073f4</c>): a table <c>.ani</c> (<see cref="Sprite"/>) or a tamer avatar (<see cref="Avatar"/>), standing on its cell.
/// </summary>
public sealed class NpcEntity(Serial serial, int npcId, string name, Cell cell, Direction direction, SpriteAnimator? sprite,
    FieldAnimator? animator, TamerDoll? avatar)
{
    /// <summary>✅ <c>0x00862650</c> line 88: an avatar NPC's frame advances every 3 steps (counter == 2).</summary>
    private const int AvatarStepsPerFrame = 3;

    private int _idleCounter;
    private int _avatarFrame;
    private int _avatarStep;

    public Serial Serial { get; } = serial;
    public int NpcId { get; } = npcId;
    public string Name { get; } = name;
    public Cell Cell { get; } = cell;
    public Direction Direction { get; } = direction;
    public int WorldX => Cell.CenterX;
    public int WorldY => Cell.CenterY;
    public SpriteAnimator? Sprite { get; } = sprite;
    public FieldAnimator? Animator { get; } = animator;
    public TamerDoll? Avatar { get; } = avatar;

    /// <summary>
    /// ✅ <c>0x00863430</c> + <c>0x00863b80</c> (table .ani NPC) and <c>0x00862650</c> (avatar NPC): advances the NPC animation
    /// by one logic step.
    /// </summary>
    public void Step(Random random)
    {
        if (Avatar is not null)
            StepAvatar(Avatar);
        else if (Animator is not null)
            StepAnimation(Animator, random);
    }

    private void StepAvatar(TamerDoll avatar)
    {
        var range = avatar.Body.Ani.Range(FieldAnimator.Standing, (int)Direction);
        if (range.Count == 0)
            range = avatar.Body.Ani.Range(FieldAnimator.Standing, 0);
        if (range.Count == 0)
            return;
        if (_avatarStep >= AvatarStepsPerFrame - 1)
        {
            _avatarStep = 0;
            _avatarFrame = (_avatarFrame + 1) % range.Count;
        }
        else
        {
            _avatarStep++;
        }
        avatar.Body.ShowFrame(FieldAnimator.Standing, range.Start + _avatarFrame);
    }

    private void StepAnimation(FieldAnimator animator, Random random)
    {
        int previousOffset = animator.Offset;
        animator.Step();
        // ✅ 0x00863b80: when an idle variation (2 or 3) wraps around, return to Standing (1)
        if (animator.Animation is FieldAnimator.IdleA or FieldAnimator.IdleB && animator.Offset < previousOffset)
            animator.Switch(FieldAnimator.Standing);

        // ✅ 0x00863430 line 30: every 200 steps, 1-in-4 chance of animation 3, else animation 2 (if available)
        _idleCounter++;
        if (_idleCounter >= FieldAnimator.ChangeStep)
        {
            _idleCounter = 0;
            animator.Switch(random.Next(4) == 0 ? FieldAnimator.IdleB : FieldAnimator.IdleA);
        }
    }
}
