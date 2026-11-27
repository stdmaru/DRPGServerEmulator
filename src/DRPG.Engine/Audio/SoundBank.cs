
namespace DRPG.Engine.Audio;

/// <summary>
/// The sounds of a scene, the way the original keeps them: effects registered by number with a file name
/// (✅ v322 <c>FUN_00a187b0(id, file)</c> registers, <c>FUN_00a18890(id, 0)</c> plays) and one background track at a
/// time that loops (<c>0x00b42500(file, 0)</c>). Files are read from the install's <c>Sound\</c> folder, found
/// ignoring case (the original runs on a case-insensitive file system), decoded the first time they are used and kept.
/// A file that is missing or cannot be decoded is logged once and then silent, never an error: the original skips it too.
/// </summary>
public sealed class SoundBank(IAudio audio, string soundDirectory, float effectVolume, float musicVolume, Action<string> log) : IDisposable
{
    /// <summary>✅ <c>FUN_00a18890</c>: the original has 790 positions.</summary>
    public const int EffectSlots = 790;

    private readonly Dictionary<int, string> _effects = [];
    private readonly Dictionary<string, PcmSound?> _decoded = new(StringComparer.OrdinalIgnoreCase);
    private IVoice? _music;
    private string? _musicFile;

    /// <summary>
    /// ✅ <c>0x00a18910</c>: the option is 0 for mute, otherwise <c>(v − 4) × 400</c> hundredths of a decibel
    /// (DirectSound, never above 0 dB). ❓ Which <c>option.ini</c> key feeds <c>[+8]</c> and <c>[+9]</c> is taken from the
    /// names (<c>[BGM] bgm</c> and <c>sound</c>).
    /// </summary>
    public static float GainFromOption(int option)
    {
        if (option <= 0)
            return 0f;
        double hundredthsOfDecibel = Math.Min(0, (option - 4) * 400);
        return (float)Math.Pow(10, hundredthsOfDecibel / 2000);
    }

    /// <summary>Registers (or replaces) effect <paramref name="id"/>. Ids outside 0..789 are refused.</summary>
    public void Register(int id, string relativePath)
    {
        if (id is < 0 or >= EffectSlots)
            throw new ArgumentOutOfRangeException(nameof(id), id, "effect id outside the 790 slots of the original");
        _effects[id] = relativePath;
    }

    /// <summary>Plays effect <paramref name="id"/> once; an id nobody registered is silent (✅ the original ignores it).</summary>
    public void PlayEffect(int id)
    {
        if (effectVolume <= 0 || !_effects.TryGetValue(id, out var path))
            return;
        if (Load(path) is { } sound)
            audio.Play(sound, effectVolume, loop: false);
    }

    /// <summary>
    /// Stops the current track and plays <paramref name="relativePath"/> in a loop (✅ <c>0x00b424a0</c> then
    /// <c>0x00b42500</c>). Asking for the track that already plays changes nothing.
    /// </summary>
    public void PlayMusic(string relativePath)
    {
        if (_music is { IsPlaying: true } && string.Equals(_musicFile, relativePath, StringComparison.OrdinalIgnoreCase))
            return;
        StopMusic();
        _musicFile = relativePath;
        if (musicVolume <= 0 || Load(relativePath) is not { } sound)
            return;
        _music = audio.Play(sound, musicVolume, loop: true);
    }

    public void StopMusic()
    {
        _music?.Stop();
        _music = null;
        _musicFile = null;
    }

    /// <summary>The file inside <see cref="soundDirectory"/> for a path written with backslashes and any case, or null.</summary>
    internal string? Locate(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        // the original paths start with the folder name itself ("Sound\title.ogg", "sound\menu-bt.wav")
        if (parts.Length > 1 && parts[0].Equals(Path.GetFileName(soundDirectory.TrimEnd('/', '\\')), StringComparison.OrdinalIgnoreCase))
            parts = parts[1..];
        if (parts.Length != 1 || parts[0] is "." or "..")
            return null;
        if (!Directory.Exists(soundDirectory))
            return null;
        return Directory.EnumerateFiles(soundDirectory)
            .FirstOrDefault(file => string.Equals(Path.GetFileName(file), parts[0], StringComparison.OrdinalIgnoreCase));
    }

    private PcmSound? Load(string relativePath)
    {
        if (_decoded.TryGetValue(relativePath, out var known))
            return known;
        PcmSound? sound = null;
        try
        {
            if (Locate(relativePath) is { } file)
                sound = SoundDecoder.Decode(file, File.ReadAllBytes(file));
            else
                log($"sound: {relativePath} not found");
        }
        catch (Exception e) when (e is SoundFormatException or IOException)
        {
            log($"sound: {relativePath}: {e.Message}");
        }
        _decoded[relativePath] = sound;
        return sound;
    }

    public void Dispose() => StopMusic();
}
