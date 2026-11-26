using NVorbis;

namespace DRPG.Engine.Audio;

/// <summary>
/// <c>.ogg</c> (Vorbis, the BGM of the game) with NVorbis: managed, so it needs no native library and is AOT-safe
/// (docs/projeto/decisoes.md#audio). Decodes the whole file; a BGM is a few MB of PCM.
/// </summary>
public static class OggDecoder
{
    /// <summary>Largest channel count accepted: Vorbis allows 255, the game uses mono and stereo.</summary>
    private const int MaxChannels = 8;

    public static PcmSound Decode(byte[] data)
    {
        try
        {
            using var reader = new VorbisReader(new MemoryStream(data), closeOnDispose: true);
            if (reader.Channels is < 1 or > MaxChannels || reader.SampleRate < 1)
                throw new SoundFormatException($"ogg: {reader.Channels} channels at {reader.SampleRate} Hz");
            long expected = reader.TotalSamples * reader.Channels;
            if (expected > PcmSound.MaxSamples)
                throw new SoundFormatException("ogg: too long");

            var samples = new List<float[]>();
            var chunk = new float[4096 * reader.Channels];
            long total = 0;
            int read;
            while ((read = reader.ReadSamples(chunk, 0, chunk.Length)) > 0)
            {
                total += read;
                if (total > PcmSound.MaxSamples)
                    throw new SoundFormatException("ogg: too long");
                samples.Add(chunk[..read]);
            }

            var all = new float[total];
            int at = 0;
            foreach (var part in samples)
            {
                part.CopyTo(all, at);
                at += part.Length;
            }
            return new PcmSound(reader.SampleRate, reader.Channels, all);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or ArgumentException or IOException)
        {
            throw new SoundFormatException($"ogg: {e.Message}", e);
        }
    }
}
