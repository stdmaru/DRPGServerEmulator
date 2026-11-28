using DRPG.Client.Ui;
using DRPG.Shared.Protocol;

namespace DRPG.Client.World;

/// <summary>
/// The idle animation of a tamer or a digimon on the map, one logic step at a time (✅ v322 tamer <c>0x00867a60</c> /
/// <c>0x00867790</c>, digimon <c>0x007f4cf0</c> / <c>0x007f4080</c>, docs/cliente/cena-jogo.md#tamer-e-digimon-no-campo). The
/// character selection's <see cref="SpriteAnimator"/> draws it: here only its frame is set. The frame is the start of the
/// animation in direction 0 plus the offset; it stays for the duration the <c>.ani</c> gives that frame plus one step, and the
/// offset wraps to 0 after the last frame (every frame appears, unlike the character selection).
/// </summary>
public sealed class FieldAnimator
{
    /// <summary>
    /// ✅ The animation of a movement state has the state's number (<c>+0x149</c> is passed to <c>0x0085e4c0</c> as it is):
    /// 1 = standing, 2 and 3 = the digimon's idle variations, 4 = walk, 5 = run, 0x0f = fast.
    /// </summary>
    public const int Standing = (int)MoveState.Standing, IdleA = (int)MoveState.IdleA, IdleB = (int)MoveState.IdleB;

    /// <inheritdoc cref="Standing"/>
    public const int Walk = (int)MoveState.Walk, Run = (int)MoveState.Run, Fast = (int)MoveState.Fast;

    /// <summary>✅ The digimon changes animation at this step of its idle counter and comes back at <see cref="BackStep"/>.</summary>
    public const int ChangeStep = 200, BackStep = 320;

    /// <summary>✅ The sprite species whose frames last 2 steps whatever the file says (<c>0x11b</c>, <c>0x11c</c>, <c>0x15d</c>, <c>0x343</c>, <c>0x360</c>).</summary>
    private static readonly HashSet<int> TwoStepSpecies = [0x11b, 0x11c, 0x15d, 0x343, 0x360];

    /// <summary>✅ The species that never change to the idle animations.</summary>
    private const int NoIdleSpecies = 0xfb;

    private readonly SpriteAnimator _view;
    private readonly bool _digimon;
    private readonly int _species;
    private int _offset, _counter, _idle;

    /// <param name="view">What draws the frames.</param>
    /// <param name="digimon">True for a digimon (idle variations), false for a tamer.</param>
    /// <param name="species">The species id, for the special cases (0 for a tamer).</param>
    /// <param name="initialOffset">✅ <c>rand() % 8</c>: the phase of the loop (<c>0x0085e7b0</c>).</param>
    public FieldAnimator(SpriteAnimator view, bool digimon, int species, int initialOffset)
    {
        _view = view;
        _digimon = digimon;
        _species = species;
        _offset = initialOffset;
        Animation = Standing;
        _view.ShowFrame(Standing, _view.Ani.Range(Standing, (int)Direction).Start + _offset);
    }

    public int Animation { get; private set; }

    public Direction Direction { get; private set; }

    public int Offset => _offset;

    public int Counter => _counter;

    public int IdleCounter => _idle;

    public SpriteAnimator View => _view;

    /// <summary>Updates the facing direction and refreshes the shown frame if it changed.</summary>
    public void SetDirection(Direction direction)
    {
        if (direction == Direction)
            return;
        Direction = direction;
        var range = _view.Ani.Range(Animation, (int)Direction);
        if (range.Count == 0)
            return;
        if (_offset > range.Count - 1)
            _offset = 0;
        _view.ShowFrame(Animation, range.Start + _offset);
    }

    /// <summary>One logic step. <paramref name="idleRoll"/> is the 1-in-4 draw of the change at step 200 (0 = the animation 3).</summary>
    public void Step(int idleRoll = 1)
    {
        var range = _view.Ani.Range(Animation, (int)Direction);
        if (range.Count == 0)
            range = _view.Ani.Range(Animation, 0);
        if (_offset > range.Count - 1)
            _offset = 0;
        int frame = range.Start + _offset;
        _view.ShowFrame(Animation, frame);
        int duration = TwoStepSpecies.Contains(_species) ? 2 : DurationOf(frame);
        if (_counter >= duration)
        {
            _offset++;
            _counter = 0;
        }
        else
        {
            _counter++;
        }
        if (_offset > range.Count - 1)
            _offset = 0;

        // ✅ 0x007f3df0: idle counter runs only while standing/idle (state < 4)
        if (!_digimon || _species == NoIdleSpecies || Animation >= Walk)
            return;
        _idle++;
        if (_idle == ChangeStep)
            Switch(idleRoll == 0 ? IdleB : IdleA);
        else if (_idle == BackStep)
        {
            Switch(Standing);
            _idle = 0;
        }
    }

    /// <summary>✅ <c>0x0085e4c0</c>: zeroes the offset and the counter and shows the first frame of the animation (an animation the sprite lacks is not played).</summary>
    public void Switch(int animation)
    {
        var range = _view.Ani.Range(animation, (int)Direction);
        if (range.Count == 0)
            range = _view.Ani.Range(animation, 0);
        if (range.Count == 0)
            return;
        Animation = animation;
        _offset = 0;
        _counter = 0;
        if (animation >= Walk)
            _idle = 0;
        _view.ShowFrame(animation, range.Start);
    }

    /// <summary>
    /// ✅ <c>0x007f3df0</c> / <c>0x00867a60</c>: shows what <paramref name="entity"/> does: its direction, the animation of its
    /// movement state while it moves, and the standing animation when it stops (the idle variations stay).
    /// </summary>
    public void Follow(FieldEntity entity)
    {
        SetDirection(entity.Direction);
        if (entity.IsMoving)
        {
            if (Animation != (int)entity.State)
                Switch((int)entity.State);
        }
        else if (Animation >= Walk)
        {
            Switch(Standing);
        }
    }

    private int DurationOf(int frame) =>
        frame >= 0 && frame < _view.Ani.Frames.Count ? (short)_view.Ani.Frames[frame].Unknown : 0;
}
