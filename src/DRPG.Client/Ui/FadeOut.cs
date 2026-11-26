using DRPG.Engine.Rendering;

namespace DRPG.Client.Ui;

/// <summary>
/// The fade to black that leaves a scene (✅ v322 <c>0x00b546c0</c> and <c>FUN_00b55f10</c>): a counter grows by
/// <c>dt × 10</c> each frame, the frame loses <c>4 × counter − 1</c> from every 5-bit colour channel (the 6-bit green
/// twice that), and the scene changes when the counter passes 15. At 30 FPS that is 45 frames, about 1.5 s.
/// </summary>
public sealed class FadeOut
{
    /// <summary>✅ <c>0x0102ee70</c>.</summary>
    public const float Speed = 10f;

    /// <summary>✅ <c>0x0102ee74</c>.</summary>
    public const float Limit = 15f;

    private float _counter;

    public bool Done => _counter > Limit;

    public float Counter => _counter;

    /// <summary>The 8-bit amount that corresponds to <c>4 × counter − 1</c> steps of a 5-bit channel (×255/31).</summary>
    public int Amount => Math.Clamp((int)MathF.Round((4 * _counter - 1) * 255f / 31f), 0, 255);

    /// <summary>One frame of <paramref name="seconds"/>: advances the counter and darkens what the scene drew.</summary>
    public void Frame(IRenderer renderer, float seconds)
    {
        _counter += Math.Max(0, seconds) * Speed;
        renderer.Darken(Amount);
    }
}
