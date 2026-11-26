namespace DRPG.Engine.Audio;

/// <summary>Picks the decoder by the extension of the file name in the install (<c>Sound\</c>).</summary>
public static class SoundDecoder
{
    public static PcmSound Decode(string fileName, byte[] data) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".wav" => WavDecoder.Decode(data),
        ".ogg" => OggDecoder.Decode(data),
        var extension => throw new SoundFormatException($"sound: unsupported extension '{extension}'"),
    };
}
