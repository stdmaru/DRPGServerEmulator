using DRPG.Engine.Audio;
using static SDL.SDL3;

namespace DRPG.Engine.Tests;

/// <summary>The real SDL backend on SDL's "dummy" driver, which opens a device without sound hardware (CI, the devcontainer).</summary>
public sealed class SdlAudioTests
{
    [Fact]
    public void Music_and_overlapping_effects_play_on_one_device()
    {
        // a hint and not an environment variable: SDL reads the native environment, which .NET does not change
        SDL_SetHint(SDL_HINT_AUDIO_DRIVER, "dummy");
        using var audio = new SdlAudio();
        var music = audio.Play(new PcmSound(44100, 2, new float[44100 * 2]), 0.5f, loop: true);
        var effects = Enumerable.Range(0, 4).Select(_ => audio.Play(new PcmSound(22050, 1, new float[2205]), 1f, loop: false)).ToList();

        audio.Update();

        Assert.True(music.IsPlaying);
        Assert.All(effects, e => Assert.True(e.IsPlaying));
        music.Stop();
        Assert.False(music.IsPlaying);
    }
}
