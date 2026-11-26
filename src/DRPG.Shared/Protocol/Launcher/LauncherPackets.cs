namespace DRPG.Shared.Protocol.Launcher;

/// <summary>
/// Launcher login opcodes on the LoginServer (docs/protocolo/launcher.md). Chosen far above every opcode the original
/// client uses (≤ 0x179, docs/protocolo/opcodes.md), so <c>DigimonRPG.bin</c> never sends them.
/// </summary>
public enum LauncherOpcode : ushort
{
    /// <summary>C2S: protocol and launcher versions.</summary>
    Hello = 0x0F00,

    /// <summary>S2C: server public key and a one-time nonce.</summary>
    HelloReply = 0x0F01,

    /// <summary>C2S: credentials in a sealed box for the server key.</summary>
    Login = 0x0F02,

    /// <summary>S2C: result and game key, encrypted with the response key from the request.</summary>
    LoginReply = 0x0F03,
}

/// <summary>Limits shared by both sides; anything outside them closes the connection.</summary>
public static class LauncherLimits
{
    public const ushort ProtocolVersion = 1;

    /// <summary>Login (username or e-mail), UTF-8 bytes.</summary>
    public const int MaxLoginBytes = 255;

    /// <summary>Password, UTF-8 bytes.</summary>
    public const int MaxPasswordBytes = 255;

    /// <summary>Game username: the <c>username[21]</c> field of the channel 0x04 packet.</summary>
    public const int MaxGameUsernameBytes = 20;

    /// <summary>Game key: the <c>auth key[40]</c> field of the channel 0x04 packet, as 40 hex characters.</summary>
    public const int GameKeyChars = 40;

    public const int NonceBytes = 32;
    public const int MaxPublicKeyBytes = 256;
    public const int MaxSealedBytes = 1024;
}

/// <summary>C2S 0x0F00: <c>u16 protocol, u32 launcher version</c>.</summary>
public sealed record LauncherHello(ushort Protocol, uint LauncherVersion) : ITwoWayPacket<LauncherHello>
{
    public static ushort Opcode => (ushort)LauncherOpcode.Hello;
    public static PacketLength Length => PacketLength.Exact(6);

    public static LauncherHello Read(PacketReader r) => new(r.ReadU16(), r.ReadU32());

    public void Write(PacketWriter w)
    {
        w.WriteU16(Protocol);
        w.WriteU32(LauncherVersion);
    }
}

public enum LauncherHelloStatus : byte
{
    Ok = 0,

    /// <summary>Protocol version not supported: the launcher must update.</summary>
    UnsupportedProtocol = 1,

    /// <summary>Launcher login is turned off on this server.</summary>
    Disabled = 2,
}

/// <summary>S2C 0x0F01: <c>u16 protocol, u8 status, u8[32] nonce, u16 key length, key (SubjectPublicKeyInfo DER)</c>.</summary>
public sealed record LauncherHelloReply(ushort Protocol, LauncherHelloStatus Status, byte[] Nonce, byte[] PublicKey)
    : ITwoWayPacket<LauncherHelloReply>
{
    public static ushort Opcode => (ushort)LauncherOpcode.HelloReply;
    public static PacketLength Length => new(2 + 1 + LauncherLimits.NonceBytes + 2, 2 + 1 + LauncherLimits.NonceBytes + 2 + LauncherLimits.MaxPublicKeyBytes);

    public static LauncherHelloReply Read(PacketReader r)
    {
        var protocol = r.ReadU16();
        var status = (LauncherHelloStatus)r.ReadU8();
        var nonce = r.ReadBytes(LauncherLimits.NonceBytes);
        var key = r.ReadBytes(BoundedLength(r.ReadU16(), LauncherLimits.MaxPublicKeyBytes, "public key"));
        return new(protocol, status, nonce, key);
    }

    public void Write(PacketWriter w)
    {
        w.WriteU16(Protocol);
        w.WriteU8((byte)Status);
        w.WriteBytes(Nonce);
        w.WriteU16((ushort)PublicKey.Length);
        w.WriteBytes(PublicKey);
    }

    internal static int BoundedLength(ushort length, int max, string field) =>
        length <= max ? length : throw new PacketFormatException($"{field}: {length} B, at most {max}");
}

/// <summary>
/// C2S 0x0F02: <c>u16 ephemeral key length, ephemeral key (SPKI DER), u8[12] IV, u16 sealed length, sealed (ciphertext + 16-byte tag)</c>.
/// The plaintext is a <see cref="LauncherCredentials"/>.
/// </summary>
public sealed record LauncherLogin(byte[] EphemeralKey, byte[] Iv, byte[] Sealed) : ITwoWayPacket<LauncherLogin>
{
    public static ushort Opcode => (ushort)LauncherOpcode.Login;
    public static PacketLength Length => PacketLength.AtLeast(2 + LauncherCrypto.IvBytes + 2);

    public static LauncherLogin Read(PacketReader r)
    {
        var key = r.ReadBytes(LauncherHelloReply.BoundedLength(r.ReadU16(), LauncherLimits.MaxPublicKeyBytes, "ephemeral key"));
        var iv = r.ReadBytes(LauncherCrypto.IvBytes);
        var sealedBytes = r.ReadBytes(LauncherHelloReply.BoundedLength(r.ReadU16(), LauncherLimits.MaxSealedBytes, "sealed body"));
        return new(key, iv, sealedBytes);
    }

    public void Write(PacketWriter w)
    {
        w.WriteU16((ushort)EphemeralKey.Length);
        w.WriteBytes(EphemeralKey);
        w.WriteBytes(Iv);
        w.WriteU16((ushort)Sealed.Length);
        w.WriteBytes(Sealed);
    }
}

/// <summary>S2C 0x0F03: <c>u8[12] IV, u16 sealed length, sealed</c>. The plaintext is a <see cref="LauncherLoginResult"/>.</summary>
public sealed record LauncherLoginReply(byte[] Iv, byte[] Sealed) : ITwoWayPacket<LauncherLoginReply>
{
    public static ushort Opcode => (ushort)LauncherOpcode.LoginReply;
    public static PacketLength Length => PacketLength.AtLeast(LauncherCrypto.IvBytes + 2);

    public static LauncherLoginReply Read(PacketReader r)
    {
        var iv = r.ReadBytes(LauncherCrypto.IvBytes);
        var sealedBytes = r.ReadBytes(LauncherHelloReply.BoundedLength(r.ReadU16(), LauncherLimits.MaxSealedBytes, "sealed body"));
        return new(iv, sealedBytes);
    }

    public void Write(PacketWriter w)
    {
        w.WriteBytes(Iv);
        w.WriteU16((ushort)Sealed.Length);
        w.WriteBytes(Sealed);
    }
}
