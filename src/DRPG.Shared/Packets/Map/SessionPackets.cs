using DRPG.Shared.Protocol;

namespace DRPG.Shared.Packets.Map;

/// <summary>
/// ✅ 0x01 C2S (57 B, v322): enter the map. Character[21], user[21], u8 and 14 trailing bytes.
/// ❓ The u8 is never used by the emulator and no sample documents it: it is skipped on read; the client writes 1, like the capture.
/// </summary>
public sealed record JoinCharacterRequest(string CharacterName, string Username) : ITwoWayPacket<JoinCharacterRequest>
{
    public const int NameBytes = 21;

    public static ushort Opcode => (ushort)MapOpcode.JoinCharacter;
    public static PacketLength Length => PacketLength.Exact(2 * NameBytes + 1 + 14);

    public static JoinCharacterRequest Read(PacketReader r)
    {
        var character = r.ReadFixedString(NameBytes);
        var username = r.ReadFixedString(NameBytes);
        r.ReadU8();                             // ❓
        r.Skip(14);
        return new(character, username);
    }

    public void Write(PacketWriter w)
    {
        w.WriteFixedString(CharacterName, NameBytes);
        w.WriteFixedString(Username, NameBytes);
        w.WriteU8(1);                           // ❓ 1 in the capture
        w.WriteZeros(14);
    }
}

/// <summary>✅ 0x20 C2S (20 B, v322): walk. i16 (always 5), i16 x, i16 y and 14 zero bytes.</summary>
public sealed record MoveCharacterRequest(short X, short Y) : ITwoWayPacket<MoveCharacterRequest>
{
    /// <summary>✅ The first i16 is 5 in every capture.</summary>
    private const short Marker = 5;

    public static ushort Opcode => (ushort)MapOpcode.MoveCharacter;
    public static PacketLength Length => PacketLength.Exact(20);

    public static MoveCharacterRequest Read(PacketReader r)
    {
        r.ReadI16();                            // ✅ always 5
        var x = r.ReadI16();
        var y = r.ReadI16();
        r.Skip(14);
        return new(x, y);
    }

    public void Write(PacketWriter w)
    {
        w.WriteI16(Marker);
        w.WriteI16(X);
        w.WriteI16(Y);
        w.WriteZeros(14);
    }
}
