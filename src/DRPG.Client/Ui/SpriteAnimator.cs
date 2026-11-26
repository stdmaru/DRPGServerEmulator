using DRPG.Engine.Rendering;
using DRPG.Shared.Formats.Sprite;

namespace DRPG.Client.Ui;

/// <summary>
/// The animation loop of the character selection (✅ <c>0x00b46120</c> / <c>0x00b46d50</c>, docs/cliente/formato-ani.md):
/// the sprite frame advances every <see cref="TicksPerFrame"/> game frames and, when it reaches
/// <c>start + count − 1</c>, goes back to <c>start</c> (so the last frame of the range is never shown). Direction 0 always.
/// Changing to another animation keeps the current frame until the next loop, like the original; <see cref="Restart"/> does not.
/// </summary>
public sealed class SpriteAnimator(AniFile ani, ITexture[] frames)
{
    public const int TicksPerFrame = 3;

    /// <summary>✅ Animation 1 is "standing", 5 is "selected".</summary>
    public const int Standing = 1, Selected = 5;

    private int _ticks;

    public AniFile Ani { get; } = ani;

    public int Animation { get; private set; } = Standing;

    public int Frame { get; private set; } = ani.Range(Standing, 0).Start;

    /// <summary>The texture of the current frame; null when the animation points outside the sprite.</summary>
    public ITexture? Texture => Frame >= 0 && Frame < frames.Length ? frames[Frame] : null;

    /// <summary>The anchor of the current frame; (0, 0) when there is none.</summary>
    public (int X, int Y) Anchor => Frame >= 0 && Frame < Ani.Frames.Count ? (Ani.Frames[Frame].AnchorX, Ani.Frames[Frame].AnchorY) : (0, 0);

    /// <summary>Plays <paramref name="animation"/> from its first frame.</summary>
    public void Restart(int animation)
    {
        Animation = animation;
        Frame = Ani.Range(animation, 0).Start;
        _ticks = 0;
    }

    /// <summary>Plays <paramref name="animation"/> from the frame it is at.</summary>
    public void Switch(int animation) => Animation = animation;

    /// <summary>One game frame.</summary>
    public void Tick()
    {
        if (++_ticks % TicksPerFrame != 0)
            return;
        var range = Ani.Range(Animation, 0);
        Frame++;
        if (Frame >= range.Start + range.Count - 1)
            Frame = range.Start;
    }
}
