using SDL;
using static SDL.SDL3;

namespace DRPG.Engine.Audio;

/// <summary>
/// <see cref="IAudio"/> on SDL3: each playing sound is an <c>SDL_AudioStream</c> bound to the default output device,
/// so SDL converts the rate and mixes the voices. A loop is refilled in <see cref="Update"/> whenever less than one
/// full pass is queued. Volume is the stream gain.
/// </summary>
public sealed unsafe class SdlAudio : IAudio
{
    private sealed class SdlVoice(SdlAudio owner, SDL_AudioStream* stream, PcmSound sound, bool loop) : IVoice
    {
        public SDL_AudioStream* Stream { get; private set; } = stream;

        public bool Loop { get; } = loop;

        public PcmSound Sound { get; } = sound;

        public bool IsPlaying => Stream != null && SDL_GetAudioStreamQueued(Stream) > 0;

        public void Stop()
        {
            if (Stream == null)
                return;
            SDL_DestroyAudioStream(Stream);
            Stream = null;
            owner._voices.Remove(this);
        }
    }

    private readonly List<SdlVoice> _voices = [];

    public SdlAudio()
    {
        if (!SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO))
            throw new InvalidOperationException($"SDL audio: {SDL_GetError()}");
    }

    public string BackendName => "sdl3";

    public IVoice Play(PcmSound sound, float volume, bool loop)
    {
        var spec = new SDL_AudioSpec { format = SDL_AUDIO_F32, channels = sound.Channels, freq = sound.SampleRate };
        var stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, null, 0);
        if (stream == null)
            throw new InvalidOperationException($"SDL audio: {SDL_GetError()}");
        SDL_SetAudioStreamGain(stream, Math.Clamp(volume, 0f, 1f));
        var voice = new SdlVoice(this, stream, sound, loop);
        Queue(voice);
        SDL_ResumeAudioStreamDevice(stream);
        _voices.Add(voice);
        return voice;
    }

    public void Update()
    {
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            var voice = _voices[i];
            if (voice.Loop)
            {
                if (SDL_GetAudioStreamQueued(voice.Stream) < voice.Sound.Samples.Length * sizeof(float))
                    Queue(voice);
            }
            else if (!voice.IsPlaying)
            {
                voice.Stop();       // removes itself from the list
            }
        }
    }

    private static void Queue(SdlVoice voice)
    {
        fixed (float* samples = voice.Sound.Samples)
        {
            if (!SDL_PutAudioStreamData(voice.Stream, (nint)samples, voice.Sound.Samples.Length * sizeof(float)))
                throw new InvalidOperationException($"SDL audio: {SDL_GetError()}");
        }
    }

    public void Dispose()
    {
        foreach (var voice in _voices.ToArray())
            voice.Stop();
        SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
    }
}
