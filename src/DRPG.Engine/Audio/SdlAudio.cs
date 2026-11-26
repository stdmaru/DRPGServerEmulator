using SDL;
using static SDL.SDL3;

namespace DRPG.Engine.Audio;

/// <summary>
/// <see cref="IAudio"/> on SDL3: the default output device is opened once, and each playing sound is an
/// <c>SDL_AudioStream</c> bound to it, so SDL converts the rate and mixes the voices. (Opening a device stream per
/// sound made every effect open a new logical device, and the reconfiguration crackled the music under it.) A loop is refilled in <see cref="Update"/> whenever less than one
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
    private readonly SDL_AudioDeviceID _device;

    public SdlAudio()
    {
        if (!SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO))
            throw new InvalidOperationException($"SDL audio: {SDL_GetError()}");
        _device = SDL_OpenAudioDevice(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, null);
        if (_device == 0)
        {
            var error = SDL_GetError();
            SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
            throw new InvalidOperationException($"SDL audio: {error}");
        }
    }

    public string BackendName => "sdl3";

    public IVoice Play(PcmSound sound, float volume, bool loop)
    {
        var spec = new SDL_AudioSpec { format = SDL_AUDIO_F32, channels = sound.Channels, freq = sound.SampleRate };
        var stream = SDL_CreateAudioStream(&spec, null);
        if (stream == null)
            throw new InvalidOperationException($"SDL audio: {SDL_GetError()}");
        SDL_SetAudioStreamGain(stream, Math.Clamp(volume, 0f, 1f));
        var voice = new SdlVoice(this, stream, sound, loop);
        Queue(voice);
        if (!SDL_BindAudioStream(_device, stream))
        {
            var error = SDL_GetError();
            SDL_DestroyAudioStream(stream);
            throw new InvalidOperationException($"SDL audio: {error}");
        }
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
        SDL_CloseAudioDevice(_device);
        SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
    }
}
