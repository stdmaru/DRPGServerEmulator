namespace DRPG.Engine.Audio;

/// <summary>A sound that is playing; stop it to cut it.</summary>
public interface IVoice
{
    bool IsPlaying { get; }

    void Stop();

    /// <summary>Changes the volume of the sound that plays (clamped to 0..1).</summary>
    void SetGain(float gain);
}

/// <summary>Plays <see cref="PcmSound"/>s. Game thread only; <see cref="Update"/> once per frame (refills the loops).</summary>
public interface IAudio : IDisposable
{
    string BackendName { get; }

    /// <summary><paramref name="volume"/> is clamped to 0..1.</summary>
    IVoice Play(PcmSound sound, float volume, bool loop);

    void Update();
}

/// <summary>
/// The audio of a machine without an output device (CI, a container): every sound "plays" and ends at once.
/// Chosen by <see cref="AudioFactory"/> when SDL cannot open a device, never as a missing dependency.
/// </summary>
public sealed class SilentAudio : IAudio
{
    private sealed class SilentVoice : IVoice
    {
        public bool IsPlaying => false;

        public void Stop()
        {
        }

        public void SetGain(float gain)
        {
        }
    }

    public string BackendName => "silent";

    public IVoice Play(PcmSound sound, float volume, bool loop) => new SilentVoice();

    public void Update()
    {
    }

    public void Dispose()
    {
    }
}

public static class AudioFactory
{
    /// <summary>SDL audio on the default output device; <see cref="SilentAudio"/> (and a log line) when there is none.</summary>
    public static IAudio Create(Action<string> log)
    {
        try
        {
            return new SdlAudio();
        }
        catch (InvalidOperationException e)
        {
            log($"audio: {e.Message}; running without sound");
            return new SilentAudio();
        }
    }
}
