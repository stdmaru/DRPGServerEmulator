using DRPG.Shared.Protocol;

namespace DRPG.Shared.Packets.Login;

/// <summary>Limits of the login/channel packets: the fixed string sizes of the layouts below.</summary>
public static class AccountLimits
{
    /// <summary>The <c>username[20]</c> field of the login packets (19 characters and the terminator).</summary>
    public const int LoginUsernameBytes = 20;

    /// <summary>The <c>username[21]</c> field of the channel packets.</summary>
    public const int ChannelUsernameBytes = 21;

    /// <summary>The <c>auth key[40]</c> field of the channel 0x04 packet.</summary>
    public const int AuthKeyBytes = 40;
}

/// <summary>
/// ✅ 0x05 C2S (42 B, v322, captures.db session 56): <c>u32 1, u32 0, username[20]</c> and 14 B ❓ (the original client
/// sends zeros there).
/// </summary>
public sealed record LoginHandshakeRequest(string Username) : ITwoWayPacket<LoginHandshakeRequest>
{
    public static ushort Opcode => (ushort)LoginOpcode.Handshake;
    public static PacketLength Length => PacketLength.Exact(42);

    public static LoginHandshakeRequest Read(PacketReader r)
    {
        r.ReadU32();                            // 1
        r.ReadU32();                            // 0
        var username = r.ReadFixedString(AccountLimits.LoginUsernameBytes);
        r.Skip(14);
        return new(username);
    }

    public void Write(PacketWriter w)
    {
        w.WriteU32(1);
        w.WriteU32(0);
        w.WriteFixedString(Username, AccountLimits.LoginUsernameBytes);
        w.WriteZeros(14);
    }
}

/// <summary>
/// ✅ 0x05 S2C (28 B, v322): <c>i32 3, i32 0x2710 (captures; the emulator sends 0x68), username[20]</c>.
/// ❓ The meaning of the two numbers is unknown: the client does not need them to go on to the channel.
/// </summary>
public sealed record LoginHandshakeReply(string Username, int Unknown1 = 3, int Unknown2 = 0x68) : ITwoWayPacket<LoginHandshakeReply>
{
    public static ushort Opcode => (ushort)LoginOpcode.Handshake;
    public static PacketLength Length => PacketLength.Exact(28);

    public static LoginHandshakeReply Read(PacketReader r)
    {
        var unknown1 = r.ReadI32();
        var unknown2 = r.ReadI32();
        return new(r.ReadFixedString(AccountLimits.LoginUsernameBytes), unknown1, unknown2);
    }

    public void Write(PacketWriter w)
    {
        w.WriteI32(Unknown1);
        w.WriteI32(Unknown2);
        w.WriteFixedString(Username, AccountLimits.LoginUsernameBytes);
    }
}
