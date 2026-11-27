using DRPG.Client.Data;
using DRPG.Client.Ui;
using DRPG.Engine.Rendering;

namespace DRPG.Client.World;

/// <summary>
/// ✅ The <c>0xa8</c> warp-out effect of a map change (<c>Effect\effect_map.spr</c>, <c>0x00afe310</c> → <c>0x00a1b670(0xa8)</c> →
/// <c>0x00a4a9d7</c> / <c>0x00a46e0a</c>): it plays once over the tamer, and the map's loading screen starts when it ends
/// (<c>0x00a4ab91</c> → <c>0x00aba410</c>).
/// </summary>
public sealed class WarpEffect(IFieldSprites sprites)
{
    /// <summary>✅ <c>0x00a4aa92</c>: vertical offset (+100 px) of the effect from the tamer's feet.</summary>
    public const int OffsetY = 0x64;

    /// <summary>✅ <c>0x00a1be69</c> + <c>0x00a4ab1c</c>: delay = 1 (each frame stays for 2 logic steps).</summary>
    public const int FrameDelay = 1;

    private readonly EffectFrames _frames = new(sprites, "Effect\\effect_map.spr");
    private int _delay;

    public bool IsPlaying { get; private set; }

    public int Frame { get; private set; }

    /// <summary>✅ <c>0x00afe310</c> line 51 → <c>0x00a1b670(0xa8)</c>: starts the effect from its first frame.</summary>
    public void Start()
    {
        IsPlaying = true;
        Frame = 0;
        _delay = 0;
    }

    /// <summary>
    /// ✅ <c>0x00a4a9d7</c> → <c>0x00a4ab1c</c> → <c>0x00a4ab91</c>: one logic step of the effect. True on the step it ends (also at
    /// once when the sprite is missing), when the map's loading screen starts.
    /// </summary>
    public bool Step()
    {
        if (!IsPlaying)
            return false;
        int frameCount = _frames.Frames?.Count ?? 0;
        if (_delay >= FrameDelay)
        {
            Frame++;
            _delay = 0;
        }
        else
        {
            _delay++;
        }
        if (Frame < frameCount)
            return false;
        IsPlaying = false;
        return true;
    }

    /// <summary>
    /// ✅ <c>0x00a46e0a</c> + <c>0x00a4a9d7</c>: draws the effect centred on the tamer at
    /// <c>(x − w/2, y − h + 100)</c> of its screen point, in mode 5 (additive).
    /// </summary>
    public void Draw(IRenderer renderer, int tamerScreenX, int tamerScreenY, SpriteOptions options)
    {
        if (!IsPlaying || _frames.Frames is not { Count: > 0 } frames)
            return;
        var texture = frames[Math.Clamp(Frame, 0, frames.Count - 1)];
        FieldPortals.DrawMode5(renderer, texture, tamerScreenX - texture.Width / 2, tamerScreenY - texture.Height + OffsetY, options);
    }
}
