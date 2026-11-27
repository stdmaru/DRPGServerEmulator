namespace DRPG.Client.World;

/// <summary>Where the loading screen of the world entry is (✅ the two runs of the loading thread <c>0x00b34ea0</c>).</summary>
public enum LoadingPhase
{
    /// <summary>
    /// ✅ "PreMap Loading Thread" (<c>0x00b34c10</c>, started by the main loop right after the switch to scene 5, flag
    /// <c>[DAT_0135a1dc]+0x10</c> of <c>0x00b47790</c>; <c>+0x5e4 = 1</c>): tip piece 0. After 16 frames a 1 s timer starts; when it
    /// runs out the thread sends the <c>0x01</c> (<c>0x00adca00</c>, which also sends the channel's <c>0x16</c>) and ends.
    /// </summary>
    PreMap,

    /// <summary>
    /// ✅ The thread has ended but <c>+0x5e4</c> is still set: scene 5 draws nothing (<c>0x00b4e550</c>), so the last loading frame
    /// stays on the screen until the server's <c>0x03</c>.
    /// </summary>
    WaitingForData,

    /// <summary>
    /// ✅ "Map Loading Thread" (<c>0x00b34dc0</c>, from the <c>0x03</c> handler <c>0x00abbb20</c>, <c>+10 = 1</c>): a random tip while
    /// the <c>.map</c> loads; it ends once the map is loaded (<c>0x00b348e0</c> clears <c>+10</c>) and 16 frames have been shown. Only
    /// then does the handler send <c>0x04</c> and <c>0xF0</c> and set the entry marker (+0x34c = 0x5f, no brightening).
    /// </summary>
    Map,

    Done,
}

/// <summary>What <see cref="WorldLoading.Update"/> asks the scene to do.</summary>
public enum LoadingAction
{
    None,

    /// <summary>Send the <c>0x01</c> (and the channel's <c>0x16</c>/<c>0x18</c>).</summary>
    Join,

    /// <summary>The loading screen is over: send <c>0x04</c>/<c>0xF0</c>/<c>0x121</c> and show the field.</summary>
    EnterWorld,
}

/// <summary>
/// The timing of the loading screen of the world entry, without drawing (✅ <c>0x00b34ea0</c>; docs/cliente/cena-jogo.md#tela-de-carga).
/// One run of the thread's loop is one <see cref="Update"/> that is due: it checks whether to stop, then draws a frame and moves the gauge
/// one frame; a drawn frame sleeps 80 ms and the loop 10 ms more (<see cref="FrameMs"/>; ✅ the 30 FPS limiter of <c>0x00867ed0(0x1e)</c>
/// never holds it back). The captures (pcap sessions 4 and 5) show the <c>0x01</c> 2.75–2.9 s after the connection and the <c>0x04</c>
/// ~2 s after the <c>0x03</c>: the same 16 frames with Windows' 15.6 ms sleeps.
/// </summary>
public sealed class WorldLoading
{
    /// <summary>✅ <c>Sleep(0x50)</c> after the frame and <c>Sleep(10)</c> at the end of the loop.</summary>
    public const int DrawSleepMs = 80, LoopSleepMs = 10, FrameMs = DrawSleepMs + LoopSleepMs;

    /// <summary>✅ <c>+0x5c8 ≥ 0x10</c>: the screen stays up for at least 16 frames; the counter then goes back to 10.</summary>
    public const int MinimumFrames = 16, CounterAfterMinimum = 10;

    /// <summary>✅ <c>DAT_0100f030 = 1.0</c> s, measured with the performance counter from the end of the 16th frame.</summary>
    public const int JoinDelayMs = 1000;

    /// <summary>✅ <c>[DAT_011584f0]+0xf803</c>, set when the <c>0x01</c> is sent: the next frame shows tip 0 and sleeps 30 ms more.</summary>
    public const int JoinedFrameExtraMs = 30;

    /// <summary>✅ <c>0x00b34dc0</c>: the tip of the map loading is <c>rand() &amp; 15</c>.</summary>
    public const int MapTips = 16;

    private readonly Random _random;
    private ulong _nextMs;
    private int _counter;
    private bool _minimumShown;
    private ulong? _joinTimerMs;
    private bool _joinedFrame;
    private bool _mapLoaded;
    private int _mapTip;
    private bool _started;

    public WorldLoading(Random random, int backgroundCount)
    {
        _random = random;
        Background = backgroundCount > 0 ? random.Next(backgroundCount) : -1;   // ✅ 0x00b34c10: rand() % count, once per entry
    }

    public LoadingPhase Phase { get; private set; } = LoadingPhase.PreMap;

    /// <summary>The background image of the whole entry.</summary>
    public int Background { get; }

    /// <summary>The gauge frame on the screen (<c>+0x5cc</c>); it does not go back to 0 between the two runs.</summary>
    public int GaugeFrame { get; private set; }

    private int _nextGauge;

    /// <summary>The piece of the text <c>0xa6b</c> to draw.</summary>
    public int Tip { get; private set; }

    /// <summary>Frames drawn so far (for tests and the log).</summary>
    public int FramesDrawn { get; private set; }

    public bool Showing => Phase != LoadingPhase.Done;

    /// <summary>✅ The <c>0x03</c> came: the second run starts (it waits for the first, which has ended by then).</summary>
    public void DataArrived()
    {
        if (Phase != LoadingPhase.WaitingForData)
            throw new InvalidOperationException($"the world data came while {Phase}");
        Phase = LoadingPhase.Map;
        _mapTip = _random.Next(MapTips);
        _counter = 0;
        _minimumShown = false;
    }

    /// <summary>✅ The <c>.map</c> is loaded (<c>0x00ad3730</c> ends with <c>0x00b348e0</c>).</summary>
    public void MapLoaded()
    {
        if (Phase != LoadingPhase.Map)
            throw new InvalidOperationException($"the map loaded while {Phase}");
        _mapLoaded = true;
    }

    public LoadingAction Update(ulong nowMs)
    {
        if (!_started)
        {
            _started = true;
            _nextMs = nowMs;
        }
        if (Phase is LoadingPhase.WaitingForData or LoadingPhase.Done || nowMs < _nextMs)
            return LoadingAction.None;

        if (Phase == LoadingPhase.PreMap && _joinTimerMs is { } timer && nowMs - timer >= JoinDelayMs)
        {
            Phase = LoadingPhase.WaitingForData;
            _joinedFrame = true;
            return LoadingAction.Join;
        }
        if (Phase == LoadingPhase.Map && _mapLoaded && _minimumShown)
        {
            Phase = LoadingPhase.Done;
            return LoadingAction.EnterWorld;
        }

        // one frame of the loop
        Tip = Phase == LoadingPhase.PreMap || _joinedFrame ? 0 : _mapTip;
        int frameMs = _joinedFrame ? FrameMs + JoinedFrameExtraMs : FrameMs;
        _joinedFrame = false;
        FramesDrawn++;
        GaugeFrame = _nextGauge;
        _nextGauge = (_nextGauge + 1) % Ui.LoadingScreen.GaugeFrames;
        if (++_counter >= MinimumFrames)
        {
            _counter = CounterAfterMinimum;
            _minimumShown = true;
            if (Phase == LoadingPhase.PreMap)
                _joinTimerMs ??= nowMs + DrawSleepMs;
        }
        _nextMs = nowMs + (ulong)frameMs;
        return LoadingAction.None;
    }
}
