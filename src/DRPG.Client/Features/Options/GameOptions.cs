namespace DRPG.Client.Features.Options;

/// <summary>
/// ✅ The options object <c>DAT_01158530</c> past the four sprite flags (those are <see cref="SpriteOptions"/>), read from
/// <c>option.ini</c> by <c>0x00b42be0</c> and changed by the options window (<see cref="OptionsWindow"/>). The original writes them back
/// to <c>option.ini</c> when the game closes (<c>0x00b429f0</c>); the client never writes the install, so a change lasts until the
/// client closes (docs/arquitetura/cliente.md#instalação-original-drop-in).
/// </summary>
public sealed class GameOptions
{
    /// <summary>✅ The five levels of the music and effect volumes (0 = mute, 4 = full).</summary>
    public const int VolumeLevels = 5;

    /// <summary>✅ The three choices of "Simplified" (Not Used, All Users, All Users Except Party Members).</summary>
    public const int SimplifiedChoices = 3;

    /// <summary>✅ The two languages (English, Portuguese).</summary>
    public const int Languages = 2;

    /// <summary>✅ <c>+4</c> <c>[GAME] whisper</c>: refuse whispers.</summary>
    public bool RefuseWhisper { get; set; }

    /// <summary>✅ <c>+5</c> <c>[GAME] party</c>.</summary>
    public bool RefuseParty { get; set; }

    /// <summary>✅ <c>+6</c> <c>[GAME] pvp</c>: refuse fights.</summary>
    public bool RefuseFight { get; set; }

    /// <summary>✅ <c>+7</c> <c>[GAME] trade</c>.</summary>
    public bool RefuseTrade { get; set; }

    /// <summary>✅ <c>+8</c> <c>[BGM] bgm</c>, 0..4.</summary>
    public int MusicLevel { get; set; }

    /// <summary>✅ <c>+9</c> <c>[BGM] sound</c>, 0..4.</summary>
    public int EffectLevel { get; set; }

    /// <summary>✅ <c>+0xb</c>: "View My Item", always off at start.</summary>
    public bool ViewMyItem { get; set; }

    /// <summary>✅ <c>+0xc</c> <c>[LANGUAGE] language</c>.</summary>
    public int Language { get; set; }

    /// <summary>✅ <c>+0x10</c> <c>[SPRITE] Optimization</c>: the "Simplified" choice, 0..2.</summary>
    public int Simplified { get; set; }

    /// <summary>✅ <c>+0x14</c>: "Window Mode", always on at start.</summary>
    public bool WindowMode { get; set; } = true;

    /// <summary>✅ <c>0x00b42be0</c>: the keys and their defaults (<c>GetPrivateProfileInt</c>). Values outside a radio's range keep no mark.</summary>
    public static GameOptions From(IReadOnlyDictionary<string, string> ini) => new()
    {
        Simplified = IniProfile.ReadInt(ini, "sprite.optimization", 0),
        MusicLevel = IniProfile.ReadByte(ini, "bgm.bgm", 1),
        EffectLevel = IniProfile.ReadByte(ini, "bgm.sound", 1),
        RefuseWhisper = IniProfile.ReadByte(ini, "game.whisper", 0) != 0,
        RefuseParty = IniProfile.ReadByte(ini, "game.party", 0) != 0,
        RefuseFight = IniProfile.ReadByte(ini, "game.pvp", 0) != 0,
        RefuseTrade = IniProfile.ReadByte(ini, "game.trade", 0) != 0,
        Language = IniProfile.ReadByte(ini, "language.language", 0),
    };
}
