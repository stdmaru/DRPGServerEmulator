namespace DRPG.Client.Startup;

/// <summary>
/// The progress of the loading bar (✅ v322 <c>CSceneTitle</c>, "PreLoadingBar Thread" <c>0x00b35560</c>). The loader raises a
/// <em>target</em> from its own thread (<c>0x00b34d40</c>); the screen's <em>displayed</em> value chases it from the game
/// thread. Percent, 0..100.
/// </summary>
public sealed class LoadingProgress
{
    /// <summary>✅ <c>0x00b34d40</c> stores 39.0 (<c>0x421c0000</c>) beside the target every time it rises.</summary>
    public const float BoostOnRaise = 39f;

    /// <summary>✅ Minimum time between two steps of the displayed value (10 ms).</summary>
    public const ulong StepIntervalMs = 10;

    private const float StepScale = 0.01f;       // ✅ _DAT_0102e35c
    private const float StepFactor = 0.18f;      // ✅ _DAT_0102e360
    private const float FastMultiplier = 7f;     // ✅ _DAT_0102e370
    private const float FastMargin = 1f;         // ✅ DAT_01006ce8
    private const float Full = 100f;             // ✅ DAT_01006cec

    private sealed record Target(float Percent, float Boost);

    private Target _target = new(0, 0);
    private float _displayed;
    private ulong? _lastStepMs;

    /// <summary>The loader's last report.</summary>
    public float TargetPercent => Volatile.Read(ref _target).Percent;

    public float Displayed => _displayed;

    /// <summary>Called by the loader thread. Only raises: a lower value is ignored, like the original.</summary>
    public void Raise(float percent)
    {
        var current = Volatile.Read(ref _target);
        if (current.Percent < percent)
            Volatile.Write(ref _target, new Target(Math.Min(percent, Full), BoostOnRaise));
    }

    /// <summary>
    /// One step of the chase, called once per frame by the game thread. The displayed value grows by
    /// <c>boost × 0.01 × 0.18</c>, seven times that while the boost is still below <c>target − 1</c>, and only while it
    /// has not passed the target.
    /// </summary>
    public void Tick(ulong nowMs)
    {
        if (_lastStepMs is not { } last)
        {
            _lastStepMs = nowMs;
            return;
        }
        var target = Volatile.Read(ref _target);
        if (nowMs - last < StepIntervalMs || _displayed > target.Percent)
            return;
        float step = target.Boost * StepScale;
        if (target.Boost < target.Percent - FastMargin)
            step *= FastMultiplier;
        _displayed += step * StepFactor;
        _lastStepMs = nowMs;
    }

    /// <summary>
    /// ✅ Frame of the bar sprite: <c>1 + (frames − 1) × displayed / 100</c>, truncated (<c>0x00b35560</c>).
    /// ❓ Whether the original counts frames from 1 or 0 is not confirmed; this is the index into the sprite's frame
    /// list, clamped to it.
    /// </summary>
    public int BarFrame(int frameCount)
    {
        if (frameCount <= 0)
            return 0;
        var frame = (int)(1 + (frameCount - 1) * _displayed / Full);
        return Math.Clamp(frame, 0, frameCount - 1);
    }
}
