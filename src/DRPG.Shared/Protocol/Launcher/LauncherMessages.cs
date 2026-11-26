using System.Text;

namespace DRPG.Shared.Protocol.Launcher;

/// <summary>
/// Plaintext inside <see cref="LauncherLogin"/>:
/// <c>u8[32] server nonce, u8[32] response key, u32 launcher version, u16 n, login[n], u16 m, password[m]</c> (UTF-8).
/// Never logged: <see cref="ToString"/> hides the password and the response key.
/// </summary>
public sealed record LauncherCredentials(byte[] ServerNonce, byte[] ResponseKey, uint LauncherVersion, string Login, string Password)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public byte[] Encode()
    {
        var w = new PacketWriter();
        w.WriteBytes(ServerNonce);
        w.WriteBytes(ResponseKey);
        w.WriteU32(LauncherVersion);
        WriteText(w, Login, LauncherLimits.MaxLoginBytes, "login");
        WriteText(w, Password, LauncherLimits.MaxPasswordBytes, "password");
        return w.Payload.ToArray();
    }

    public static LauncherCredentials Decode(ReadOnlyMemory<byte> plaintext)
    {
        var r = new PacketReader(plaintext);
        var nonce = r.ReadBytes(LauncherLimits.NonceBytes);
        var responseKey = r.ReadBytes(LauncherCrypto.KeyBytes);
        var version = r.ReadU32();
        var login = ReadText(r, LauncherLimits.MaxLoginBytes, "login");
        var password = ReadText(r, LauncherLimits.MaxPasswordBytes, "password");
        if (r.Remaining != 0) throw new PacketFormatException($"credentials: {r.Remaining} B left");
        return new(nonce, responseKey, version, login, password);
    }

    public override string ToString() => $"{nameof(LauncherCredentials)}({Login}, v{LauncherVersion})";

    internal static void WriteText(PacketWriter w, string text, int maxBytes, string field)
    {
        var bytes = Utf8.GetBytes(text);
        if (bytes.Length > maxBytes) throw new ArgumentException($"{field}: {bytes.Length} B, at most {maxBytes}");
        w.WriteU16((ushort)bytes.Length);
        w.WriteBytes(bytes);
    }

    internal static string ReadText(PacketReader r, int maxBytes, string field)
    {
        int n = r.ReadU16();
        if (n > maxBytes) throw new PacketFormatException($"{field}: {n} B, at most {maxBytes}");
        try
        {
            return Utf8.GetString(r.ReadBytes(n));
        }
        catch (DecoderFallbackException)
        {
            throw new PacketFormatException($"{field}: invalid UTF-8");
        }
    }
}

public enum LauncherLoginStatus : byte
{
    Ok = 0,

    /// <summary>Unknown login or wrong password (one answer for both, on purpose).</summary>
    InvalidCredentials = 1,

    /// <summary>Too many attempts from this address or for this account; try later.</summary>
    TooManyAttempts = 2,

    /// <summary>Launcher version below the server minimum.</summary>
    OutdatedLauncher = 3,

    /// <summary>The account cannot play (banned, no game username).</summary>
    AccountUnavailable = 4,

    ServerError = 5,
}

/// <summary>
/// Plaintext inside <see cref="LauncherLoginReply"/>:
/// <c>u8 status, u16 n, game username[n], u16 m, game key[m], u32 seconds valid</c>.
/// On success the launcher starts the game with <c>&lt;game username&gt; &lt;game key&gt;</c>.
/// </summary>
public sealed record LauncherLoginResult(LauncherLoginStatus Status, string GameUsername, string GameKey, uint ValidSeconds)
{
    public static LauncherLoginResult Failure(LauncherLoginStatus status) => new(status, "", "", 0);

    public byte[] Encode()
    {
        var w = new PacketWriter();
        w.WriteU8((byte)Status);
        LauncherCredentials.WriteText(w, GameUsername, LauncherLimits.MaxGameUsernameBytes, "game username");
        LauncherCredentials.WriteText(w, GameKey, LauncherLimits.GameKeyChars, "game key");
        w.WriteU32(ValidSeconds);
        return w.Payload.ToArray();
    }

    public static LauncherLoginResult Decode(ReadOnlyMemory<byte> plaintext)
    {
        var r = new PacketReader(plaintext);
        var status = (LauncherLoginStatus)r.ReadU8();
        var username = LauncherCredentials.ReadText(r, LauncherLimits.MaxGameUsernameBytes, "game username");
        var key = LauncherCredentials.ReadText(r, LauncherLimits.GameKeyChars, "game key");
        var seconds = r.ReadU32();
        if (r.Remaining != 0) throw new PacketFormatException($"login result: {r.Remaining} B left");
        return new(status, username, key, seconds);
    }

    public override string ToString() => $"{nameof(LauncherLoginResult)}({Status}, {GameUsername})";
}
