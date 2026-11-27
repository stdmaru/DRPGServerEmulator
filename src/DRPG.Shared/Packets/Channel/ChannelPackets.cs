using DRPG.Shared.Packets.Login;
using DRPG.Shared.Protocol;

namespace DRPG.Shared.Packets.Channel;

/// <summary>
/// ✅ 0x03 C2S (66 B, v322, session 56): <c>i32 1, i32 0, i32 0, name[40]</c> and 14 B zero. The name is
/// <c>MoveInteractive_Digimon_111</c> in every capture ❓ (a model name; the server ignores it).
/// </summary>
public sealed record ChannelSelectRequest(string Name = ChannelSelectRequest.CapturedName) : ITwoWayPacket<ChannelSelectRequest>
{
    public const string CapturedName = "MoveInteractive_Digimon_111";
    public const int NameBytes = 40;

    public static ushort Opcode => (ushort)ChannelOpcode.ChannelSelect;
    public static PacketLength Length => PacketLength.Exact(66);

    public static ChannelSelectRequest Read(PacketReader r)
    {
        r.ReadI32();                            // 1
        r.ReadI32();                            // 0
        r.ReadI32();                            // 0
        var name = r.ReadFixedString(NameBytes);
        r.Skip(14);
        return new(name);
    }

    public void Write(PacketWriter w)
    {
        w.WriteI32(1);
        w.WriteI32(0);
        w.WriteI32(0);
        w.WriteFixedString(Name, NameBytes);
        w.WriteZeros(14);
    }
}

/// <summary>
/// ✅ 0x03 S2C (52 B, v322): result 2/0, <c>i32 fourth starter</c> (which of the four extra starters the server
/// offers) and the 40 B name echoed from the request.
/// </summary>
public sealed record ChannelSelectReply(int FourthStarter, string Name = ChannelSelectRequest.CapturedName) : ITwoWayPacket<ChannelSelectReply>
{
    public static ushort Opcode => (ushort)ChannelOpcode.ChannelSelect;
    public static PacketLength Length => PacketLength.Exact(52);

    public static ChannelSelectReply Read(PacketReader r)
    {
        ChannelResult.Read(r);
        var fourthStarter = r.ReadI32();
        return new(fourthStarter, r.ReadFixedString(ChannelSelectRequest.NameBytes));
    }

    public void Write(PacketWriter w)
    {
        ChannelResult.Write(w, ChannelError.None);
        w.WriteI32(FourthStarter);
        w.WriteFixedString(Name, ChannelSelectRequest.NameBytes);
    }
}

/// <summary>
/// ✅ 0x04 C2S (462 B, v322): <c>i32 1, i32 0, username[21], auth key[40]</c>, 60 B zero, <c>MAC[21] × 3</c> (as
/// <c>18-C0-4D-3B-04-C6</c> text) and 270 B zero. The auth key is the 40 hex characters the launcher passes on the
/// command line.
/// </summary>
public sealed record AuthenticateRequest(string Username, string AuthKey, string Mac1 = "", string Mac2 = "", string Mac3 = "")
    : ITwoWayPacket<AuthenticateRequest>
{
    public const int MacBytes = 21;

    public static ushort Opcode => (ushort)ChannelOpcode.Authenticate;
    public static PacketLength Length => PacketLength.Exact(462);

    public static AuthenticateRequest Read(PacketReader r)
    {
        r.ReadI32();
        r.ReadI32();
        var username = r.ReadFixedString(AccountLimits.ChannelUsernameBytes);
        var authKey = r.ReadFixedString(AccountLimits.AuthKeyBytes);
        r.Skip(60);
        var mac1 = r.ReadFixedString(MacBytes);
        var mac2 = r.ReadFixedString(MacBytes);
        var mac3 = r.ReadFixedString(MacBytes);
        r.Skip(270);
        return new(username, authKey, mac1, mac2, mac3);
    }

    public void Write(PacketWriter w)
    {
        w.WriteI32(1);
        w.WriteI32(0);
        w.WriteFixedString(Username, AccountLimits.ChannelUsernameBytes);
        w.WriteFullFixedString(AuthKey, AccountLimits.AuthKeyBytes);
        w.WriteZeros(60);
        w.WriteFixedString(Mac1, MacBytes);
        w.WriteFixedString(Mac2, MacBytes);
        w.WriteFixedString(Mac3, MacBytes);
        w.WriteZeros(270);
    }

    /// <summary>Never prints the auth key.</summary>
    public override string ToString() => $"{nameof(AuthenticateRequest)}({Username})";
}

/// <summary>
/// ✅ 0x04 S2C (448 B, v322): result, <c>username[21], auth key[40]</c>, 60 B zero, <c>MAC[21] × 3</c> and 256 B zero.
/// On an error the original echoes the request (user, key and MACs); on success only the user.
/// </summary>
public sealed record AuthenticateReply(int ErrorCode, string Username, string AuthKey = "", string Mac1 = "", string Mac2 = "", string Mac3 = "")
    : ITwoWayPacket<AuthenticateReply>
{
    public static ushort Opcode => (ushort)ChannelOpcode.Authenticate;
    public static PacketLength Length => PacketLength.Exact(448);

    public bool Succeeded => ErrorCode == ChannelError.None;

    public static AuthenticateReply Read(PacketReader r)
    {
        var errorCode = ChannelResult.Read(r);
        var username = r.ReadFixedString(AccountLimits.ChannelUsernameBytes);
        var authKey = r.ReadFixedString(AccountLimits.AuthKeyBytes);
        r.Skip(60);
        var mac1 = r.ReadFixedString(AuthenticateRequest.MacBytes);
        var mac2 = r.ReadFixedString(AuthenticateRequest.MacBytes);
        var mac3 = r.ReadFixedString(AuthenticateRequest.MacBytes);
        r.Skip(256);
        return new(errorCode, username, authKey, mac1, mac2, mac3);
    }

    public void Write(PacketWriter w)
    {
        ChannelResult.Write(w, ErrorCode);
        w.WriteFixedString(Username, AccountLimits.ChannelUsernameBytes);
        w.WriteFullFixedString(AuthKey, AccountLimits.AuthKeyBytes);
        w.WriteZeros(60);
        w.WriteFixedString(Mac1, AuthenticateRequest.MacBytes);
        w.WriteFixedString(Mac2, AuthenticateRequest.MacBytes);
        w.WriteFixedString(Mac3, AuthenticateRequest.MacBytes);
        w.WriteZeros(256);
    }

    /// <summary>Never prints the auth key.</summary>
    public override string ToString() => $"{nameof(AuthenticateReply)}({ErrorCode}, {Username})";
}

/// <summary>✅ 0x14 C2S (36 B, v322): <c>username[22]</c> and 14 B zero. The server ignores the contents.</summary>
public sealed record UserCheckRequest(string Username = "") : ITwoWayPacket<UserCheckRequest>
{
    public const int UsernameBytes = 22;

    public static ushort Opcode => (ushort)ChannelOpcode.UserCheck;
    public static PacketLength Length => PacketLength.Exact(36);

    public static UserCheckRequest Read(PacketReader r)
    {
        var username = r.ReadFixedString(UsernameBytes);
        r.Skip(14);
        return new(username);
    }

    public void Write(PacketWriter w)
    {
        w.WriteFixedString(Username, UsernameBytes);
        w.WriteZeros(14);
    }
}

/// <summary>
/// Byte 21 of <see cref="UserCheckReply"/> (<c>0x14</c> S2C, ✅ v322 <c>0x00b45c30</c> → <c>0x00c96960</c>):
/// tells the client whether the secondary password system is disabled, mandatory to register, already set, or optional.
/// </summary>
public enum SecondaryPasswordStatus : byte
{
    /// <summary>Disabled on the server (v322 default: byte 21 = 0, Exchange Password hidden, delete skips password).</summary>
    Disabled = 0,
    /// <summary>Required on the server and not yet set on this account (byte 21 = 1: opens registration state 3).</summary>
    RequiredUnset = 1,
    /// <summary>Enabled on the server and already set on this account (Exchange Password opens change; delete prompts state 0).</summary>
    Set = 2,
    /// <summary>Optional on the server and not yet set on this account (no auto-prompt; Exchange Password lets player opt in).</summary>
    OptionalUnset = 3,
}

/// <summary>Result byte of <see cref="SecondaryPasswordReply"/> (<c>0x15</c> S2C, ✅ v322 <c>0x00c950d0</c> state 5).</summary>
public enum SecondaryPasswordResult : byte
{
    /// <summary>✅ <c>0x55d</c> "Password has been changed."</summary>
    Success = 1,
    /// <summary>✅ <c>0x55e</c> "New Password you entered is incorrect."</summary>
    InvalidNewPassword = 2,
    /// <summary>✅ <c>0x55f</c> "Old Password you entered is incorrect."</summary>
    InvalidOldPassword = 3,
}

/// <summary>✅ 0x14 S2C (22 B, v322): <c>username[21]</c> and <c>u8 secondaryPasswordStatus</c> (byte 21, <c>0x00b45c30</c>).</summary>
public sealed record UserCheckReply(string Username, SecondaryPasswordStatus SecondaryPassword = SecondaryPasswordStatus.Disabled)
    : ITwoWayPacket<UserCheckReply>
{
    public static ushort Opcode => (ushort)ChannelOpcode.UserCheck;
    public static PacketLength Length => PacketLength.Exact(UserCheckRequest.UsernameBytes);

    public static UserCheckReply Read(PacketReader r)
    {
        var username = r.ReadFixedString(AccountLimits.ChannelUsernameBytes);
        var status = (SecondaryPasswordStatus)r.ReadU8();
        return new(username, status);
    }

    public void Write(PacketWriter w)
    {
        w.WriteFixedString(Username, AccountLimits.ChannelUsernameBytes);
        w.WriteU8((byte)SecondaryPassword);
    }
}

/// <summary>
/// ✅ 0x15 C2S (142 B, v322 <c>0x00ae6070</c>): <c>u8 0, username[21], oldPassword[40], newPassword[40], confirmPassword[40]</c>.
/// Each password field holds a lowercase hex MD5 (or <c>TextAttach[0x55b]</c> in <c>OldPassword</c> on initial registration).
/// </summary>
public sealed record SecondaryPasswordRequest(string Username, string OldPassword, string NewPassword, string ConfirmPassword)
    : ITwoWayPacket<SecondaryPasswordRequest>
{
    public const int PasswordBytes = 40;

    public static ushort Opcode => (ushort)ChannelOpcode.SecondaryPassword;
    public static PacketLength Length => PacketLength.Exact(1 + AccountLimits.ChannelUsernameBytes + PasswordBytes * 3);

    public static SecondaryPasswordRequest Read(PacketReader r)
    {
        r.ReadU8();
        var username = r.ReadFixedString(AccountLimits.ChannelUsernameBytes);
        var oldPassword = r.ReadFixedString(PasswordBytes);
        var newPassword = r.ReadFixedString(PasswordBytes);
        var confirmPassword = r.ReadFixedString(PasswordBytes);
        return new(username, oldPassword, newPassword, confirmPassword);
    }

    public void Write(PacketWriter w)
    {
        w.WriteU8(0);
        w.WriteFixedString(Username, AccountLimits.ChannelUsernameBytes);
        w.WriteFixedString(OldPassword, PasswordBytes);
        w.WriteFixedString(NewPassword, PasswordBytes);
        w.WriteFixedString(ConfirmPassword, PasswordBytes);
    }

    /// <summary>Never prints any password or MD5.</summary>
    public override string ToString() => $"{nameof(SecondaryPasswordRequest)}({Username})";
}

/// <summary>✅ 0x15 S2C (1 B, v322 <c>0x00b45c30</c> case <c>0x15</c>): result byte (1 = success, 2 = bad new, 3 = bad old).</summary>
public sealed record SecondaryPasswordReply(SecondaryPasswordResult Result) : ITwoWayPacket<SecondaryPasswordReply>
{
    public static ushort Opcode => (ushort)ChannelOpcode.SecondaryPassword;
    public static PacketLength Length => PacketLength.Exact(1);

    public static SecondaryPasswordReply Read(PacketReader r) => new((SecondaryPasswordResult)r.ReadU8());

    public void Write(PacketWriter w) => w.WriteU8((byte)Result);
}

/// <summary>
/// ✅ 0x16 C2S (55 B, v322): <c>username[21], character name[21]</c> and 13 B ❓ (zero in the captures). Sent before
/// the client leaves for the map server; the channel server does not answer it.
/// </summary>
public sealed record MapConnectionConfirmRequest(string Username, string CharacterName) : ITwoWayPacket<MapConnectionConfirmRequest>
{
    public static ushort Opcode => (ushort)ChannelOpcode.MapConnectionConfirm;
    public static PacketLength Length => PacketLength.Exact(55);

    public static MapConnectionConfirmRequest Read(PacketReader r)
    {
        var username = r.ReadFixedString(AccountLimits.ChannelUsernameBytes);
        var character = r.ReadFixedString(CharacterListSlot.NameBytes);
        r.Skip(13);
        return new(username, character);
    }

    public void Write(PacketWriter w)
    {
        w.WriteFixedString(Username, AccountLimits.ChannelUsernameBytes);
        w.WriteFixedString(CharacterName, CharacterListSlot.NameBytes);
        w.WriteZeros(13);
    }
}

/// <summary>
/// ✅ 0x18 C2S (74 B, v322): the MAC addresses of the machine as text <c>"AA-BB-CC-DD-EE-FF"</c>, three fields of 20 bytes,
/// and 14 zero bytes (❓ the code that builds it is not read; the capture has three adapters). Owner decision: the new client sends
/// the real addresses, like the original. They never go to a log: <see cref="ToString"/> shows only the count.
/// </summary>
public sealed record ClientHardwareInfoRequest(IReadOnlyList<string> MacAddresses) : ITwoWayPacket<ClientHardwareInfoRequest>
{
    public const int FieldCount = 3, FieldBytes = 20;

    public static ushort Opcode => (ushort)ChannelOpcode.ClientHardwareInfo;
    public static PacketLength Length => PacketLength.Exact(FieldCount * FieldBytes + 14);

    public static ClientHardwareInfoRequest Read(PacketReader r)
    {
        var macs = new List<string>(FieldCount);
        for (int i = 0; i < FieldCount; i++)
            macs.Add(r.ReadFixedString(FieldBytes));
        r.Skip(14);
        return new(macs);
    }

    public void Write(PacketWriter w)
    {
        for (int i = 0; i < FieldCount; i++)
            w.WriteFixedString(i < MacAddresses.Count ? MacAddresses[i] : "", FieldBytes);
        w.WriteZeros(14);
    }

    public override string ToString() => $"{nameof(ClientHardwareInfoRequest)}({MacAddresses.Count} addresses)";
}

/// <summary>✅ 0xC4 C2S on channel, map and global (14 B zero, v322): keep-alive ping of the client.</summary>
public sealed record KeepAliveRequest : ITwoWayPacket<KeepAliveRequest>
{
    public static ushort Opcode => (ushort)ChannelOpcode.KeepAlive;
    public static PacketLength Length => PacketLength.Exact(14);

    public static KeepAliveRequest Read(PacketReader r)
    {
        r.Skip(14);
        return new();
    }

    public void Write(PacketWriter w) => w.WriteZeros(14);
}

/// <summary>✅ 0xC4 S2C on channel and global (empty payload, v322): answer to <see cref="KeepAliveRequest"/>.</summary>
public sealed record KeepAliveReply : ITwoWayPacket<KeepAliveReply>
{
    public static ushort Opcode => (ushort)ChannelOpcode.KeepAlive;
    public static PacketLength Length => PacketLength.Exact(0);

    public static KeepAliveReply Read(PacketReader r) => new();

    public void Write(PacketWriter w)
    {
    }
}
