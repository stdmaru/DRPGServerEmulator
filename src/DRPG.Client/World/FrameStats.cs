using System.Diagnostics;

namespace DRPG.Client.World;

/// <summary>Frame times of a scene (the first measurement of milestone 3: target 60 FPS, p99 under 16.6 ms).</summary>
public sealed class FrameStats
{
    private readonly List<double> _frames = [];
    private long _last;

    /// <summary>Call once per frame: records the time since the last call, in milliseconds.</summary>
    public void Tick()
    {
        long now = Stopwatch.GetTimestamp();
        if (_last != 0)
            _frames.Add(Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds);
        _last = now;
    }

    public int Count => _frames.Count;

    public double Average => _frames.Count == 0 ? 0 : _frames.Average();

    public double Max => _frames.Count == 0 ? 0 : _frames.Max();

    /// <summary>The 99th percentile, nearest rank.</summary>
    public double P99
    {
        get
        {
            if (_frames.Count == 0)
                return 0;
            var sorted = _frames.Order().ToArray();
            return sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.99) - 1)];
        }
    }

    public override string ToString() => $"{Count} frames: average {Average:F2} ms, p99 {P99:F2} ms, max {Max:F2} ms";
}
