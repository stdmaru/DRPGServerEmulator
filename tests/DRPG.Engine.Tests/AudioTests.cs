using DRPG.Engine.Audio;

namespace DRPG.Engine.Tests;

/// <summary>The decoders of the game's two sound formats and the silent fallback.</summary>
public sealed class AudioTests
{
    /// <summary>0.1 s of a 440 Hz sine, mono, 22050 Hz, Vorbis (made with ffmpeg/libvorbis; synthetic, not a game asset).</summary>
    private const string TinyOggBase64 = "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";

    private static byte[] TinyOgg => Convert.FromBase64String(TinyOggBase64);

    /// <summary>A canonical 16-bit PCM RIFF/WAVE file.</summary>
    private static byte[] Wav(int sampleRate, int channels, params short[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = samples.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * 2);
        w.Write((short)(channels * 2));
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in samples)
            w.Write(s);
        return ms.ToArray();
    }

    [Fact]
    public void Wav_16_bit_becomes_float_with_the_same_rate_and_channels()
    {
        var sound = WavDecoder.Decode(Wav(11025, 2, 0, 0, 16384, -16384, 32767, -32768));

        Assert.Equal(11025, sound.SampleRate);
        Assert.Equal(2, sound.Channels);
        Assert.Equal(3, sound.FrameCount);
        Assert.Equal(0f, sound.Samples[0]);
        Assert.Equal(0.5f, sound.Samples[2], 4);
        Assert.Equal(-0.5f, sound.Samples[3], 4);
        Assert.Equal(1f, sound.Samples[4], 3);
        Assert.Equal(-1f, sound.Samples[5], 4);
    }

    [Fact]
    public void Ogg_decodes_to_the_length_and_rate_it_declares()
    {
        var sound = OggDecoder.Decode(TinyOgg);

        Assert.Equal(22050, sound.SampleRate);
        Assert.Equal(1, sound.Channels);
        Assert.InRange(sound.Duration.TotalSeconds, 0.09, 0.12);
        Assert.Contains(sound.Samples, s => Math.Abs(s) > 0.1f);        // it is a tone, not silence
        Assert.All(sound.Samples, s => Assert.InRange(s, -1.01f, 1.01f));
    }

    [Theory]
    [InlineData("a.WAV")]
    [InlineData("music/b.Ogg")]
    public void The_extension_picks_the_decoder(string name)
    {
        var data = name.EndsWith("ogg", StringComparison.OrdinalIgnoreCase) ? TinyOgg : Wav(8000, 1, 1, 2, 3);

        Assert.NotEmpty(SoundDecoder.Decode(name, data).Samples);
    }

    [Theory]
    [InlineData("a.mp3")]
    [InlineData("noextension")]
    [InlineData("")]
    public void An_unknown_extension_is_refused(string name) =>
        Assert.Throws<SoundFormatException>(() => SoundDecoder.Decode(name, Wav(8000, 1, 1)));

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(64)]
    public void Garbage_is_not_a_wav(int length) =>
        Assert.Throws<SoundFormatException>(() => WavDecoder.Decode(new byte[length]));

    [Fact]
    public void A_truncated_wav_is_refused()
    {
        var wav = Wav(8000, 1, 1, 2, 3, 4, 5, 6);

        Assert.Throws<SoundFormatException>(() => WavDecoder.Decode(wav[..30]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(64)]
    public void Garbage_is_not_an_ogg(int length) =>
        Assert.Throws<SoundFormatException>(() => OggDecoder.Decode(new byte[length]));

    [Fact]
    public void A_truncated_ogg_is_refused_or_cut_short_but_never_crashes()
    {
        var ogg = TinyOgg;
        for (int cut = 1; cut < ogg.Length; cut += 97)
        {
            try
            {
                var sound = OggDecoder.Decode(ogg[..cut]);
                Assert.True(sound.Samples.Length <= OggDecoder.Decode(ogg).Samples.Length);
            }
            catch (SoundFormatException)
            {
            }
        }
    }

    [Fact]
    public void Silent_audio_plays_nothing()
    {
        using var audio = new SilentAudio();

        var voice = audio.Play(new PcmSound(8000, 1, [0f]), 1f, loop: true);
        audio.Update();

        Assert.False(voice.IsPlaying);
        voice.Stop();
    }
}
