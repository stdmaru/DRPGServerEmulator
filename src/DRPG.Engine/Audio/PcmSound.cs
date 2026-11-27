namespace DRPG.Engine.Audio;

/// <summary>A decoded sound: interleaved 32-bit float samples, ready for the mixer.</summary>
public sealed record PcmSound(int SampleRate, int Channels, float[] Samples)
{
    /// <summary>Largest sound accepted (about 20 minutes of 44.1 kHz stereo); a bigger file is refused instead of eating memory.</summary>
    public const int MaxSamples = 100_000_000;

    public int FrameCount => Samples.Length / Channels;

    public TimeSpan Duration => TimeSpan.FromSeconds((double)FrameCount / SampleRate);
}

/// <summary>Why a sound file could not be decoded.</summary>
public sealed class SoundFormatException(string message, Exception? inner = null) : Exception(message, inner);
