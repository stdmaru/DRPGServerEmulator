using DRPG.Engine.Audio;

namespace DRPG.Engine.Tests;

/// <summary>The effect registry, the music slot and the option.ini volumes of the original's sound system.</summary>
public sealed class SoundBankTests : IDisposable
{
    private sealed class CountingAudio : IAudio
    {
        private sealed class Voice(CountingAudio owner) : IVoice
        {
            public bool IsPlaying { get; private set; } = true;

            public void Stop()
            {
                IsPlaying = false;
                owner.Stops++;
            }

            public void SetGain(float gain) => owner.Gains.Add(gain);
        }

        public List<float> Gains { get; } = [];

        public List<(float Volume, bool Loop)> Plays { get; } = [];
        public int Stops { get; private set; }
        public string BackendName => "counting";

        public IVoice Play(PcmSound sound, float volume, bool loop)
        {
            Plays.Add((volume, loop));
            return new Voice(this);
        }

        public void Update() { }
        public void Dispose() { }
    }

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "drpg-soundbank-" + Guid.NewGuid().ToString("N"), "Sound");
    private readonly CountingAudio _audio = new();
    private readonly List<string> _log = [];

    public SoundBankTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(Path.Combine(_directory, "Menu-Bt.wav"), SilentWav());
        File.WriteAllBytes(Path.Combine(_directory, "title.ogg"), [1, 2, 3]); // not a real Vorbis stream
    }

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_directory)!, recursive: true);

    private SoundBank Bank(float effects = 1, float music = 1) => new(_audio, _directory, effects, music, _log.Add);

    private static byte[] SilentWav()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + 4); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(22050); w.Write(44100); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(4); w.Write((short)0); w.Write((short)0);
        return ms.ToArray();
    }

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(-5, 0f)]
    [InlineData(4, 1f)]
    [InlineData(10, 1f)]
    public void The_option_maps_to_a_gain_without_boosting(int option, float expected) =>
        Assert.Equal(expected, SoundBank.GainFromOption(option), 3);

    [Fact]
    public void A_quieter_option_is_ten_to_the_power_of_its_hundredths_of_a_decibel()
    {
        Assert.Equal(MathF.Pow(10, -0.6f), SoundBank.GainFromOption(1), 3);
        Assert.True(SoundBank.GainFromOption(2) < SoundBank.GainFromOption(3));
    }

    [Fact]
    public void A_registered_effect_plays_once_whatever_the_case_of_the_file()
    {
        using var bank = Bank(0.5f);
        bank.Register(5, @"sound\MENU-BT.WAV");
        bank.PlayEffect(5);
        Assert.Equal([(0.5f, false)], _audio.Plays);
    }

    [Fact]
    public void An_id_nobody_registered_is_silent_and_an_id_out_of_range_is_refused()
    {
        using var bank = Bank();
        bank.PlayEffect(7);
        Assert.Empty(_audio.Plays);
        Assert.Throws<ArgumentOutOfRangeException>(() => bank.Register(SoundBank.EffectSlots, "x.wav"));
        Assert.Throws<ArgumentOutOfRangeException>(() => bank.Register(-1, "x.wav"));
    }

    [Fact]
    public void A_missing_file_is_logged_once_and_never_throws()
    {
        using var bank = Bank();
        bank.Register(1, @"sound\nothing.wav");
        bank.PlayEffect(1);
        bank.PlayEffect(1);
        Assert.Empty(_audio.Plays);
        Assert.Single(_log);
    }

    [Theory]
    [InlineData(@"..\Menu-Bt.wav")]
    [InlineData(@"sound\..\..\etc\passwd")]
    [InlineData(@"C:\Windows\x.wav")]
    [InlineData("")]
    public void Paths_that_leave_the_sound_folder_are_not_played(string path)
    {
        using var bank = Bank();
        bank.Register(2, path);
        bank.PlayEffect(2);
        Assert.Empty(_audio.Plays);
    }

    [Fact]
    public void A_broken_file_is_logged_and_skipped()
    {
        using var bank = Bank();
        bank.PlayMusic(@"Sound\title.ogg");
        Assert.Empty(_audio.Plays);
        Assert.NotEmpty(_log);
    }

    [Fact]
    public void Volume_zero_plays_nothing()
    {
        using var bank = Bank(effects: 0, music: 0);
        bank.Register(5, @"sound\menu-bt.wav");
        bank.PlayEffect(5);
        bank.PlayMusic(@"sound\menu-bt.wav");
        Assert.Empty(_audio.Plays);
    }

    [Fact]
    public void Music_loops_and_the_same_track_is_not_restarted()
    {
        using var bank = Bank();
        bank.PlayMusic(@"sound\menu-bt.wav");
        bank.PlayMusic(@"SOUND\MENU-BT.WAV");
        Assert.Equal([(1f, true)], _audio.Plays);
        bank.StopMusic();
        Assert.Equal(1, _audio.Stops);
    }
}
