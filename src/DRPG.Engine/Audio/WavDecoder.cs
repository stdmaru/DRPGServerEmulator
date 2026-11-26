using SDL;
using static SDL.SDL3;

namespace DRPG.Engine.Audio;

/// <summary>
/// <c>.wav</c> (the sound effects) with SDL3 (<c>SDL_LoadWAV_IO</c>), converted to 32-bit float. Needs no audio device,
/// only the SDL library. Only this file and <see cref="SdlAudio"/> touch SDL audio.
/// </summary>
public static unsafe class WavDecoder
{
    private const int MaxChannels = 8;

    public static PcmSound Decode(byte[] data)
    {
        if (data.Length < 12)
            throw new SoundFormatException("wav: too short");
        SDL_AudioSpec spec;
        byte* buffer;
        uint length;
        fixed (byte* source = data)
        {
            var stream = SDL_IOFromConstMem((nint)source, (nuint)data.Length);
            if (stream == null)
                throw new SoundFormatException($"wav: {SDL_GetError()}");
            // closeio: SDL closes the stream, also when it fails
            if (!SDL_LoadWAV_IO(stream, true, &spec, &buffer, &length))
                throw new SoundFormatException($"wav: {SDL_GetError()}");
        }

        try
        {
            if (spec.channels is < 1 or > MaxChannels || spec.freq < 1)
                throw new SoundFormatException($"wav: {spec.channels} channels at {spec.freq} Hz");
            var target = new SDL_AudioSpec { format = SDL_AUDIO_F32, channels = spec.channels, freq = spec.freq };
            byte* converted;
            int convertedLength;
            if (!SDL_ConvertAudioSamples(&spec, buffer, (int)length, &target, &converted, &convertedLength))
                throw new SoundFormatException($"wav: {SDL_GetError()}");
            try
            {
                int count = convertedLength / sizeof(float);
                if (count > PcmSound.MaxSamples)
                    throw new SoundFormatException("wav: too long");
                var samples = new float[count - count % target.channels];
                new ReadOnlySpan<float>(converted, samples.Length).CopyTo(samples);
                return new PcmSound(target.freq, target.channels, samples);
            }
            finally
            {
                SDL_free(converted);
            }
        }
        finally
        {
            SDL_free(buffer);
        }
    }
}
