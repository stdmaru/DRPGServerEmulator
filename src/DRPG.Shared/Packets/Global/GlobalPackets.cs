using DRPG.Shared.Protocol;

namespace DRPG.Shared.Packets.Global;

// The global connection packets of the world entry (✅ captures.db session 52, connection 89). The opcodes are the
// server's GlobalOpcode values; the layouts come from the captures (❓ the client code that builds them is not read).

/// <summary>
/// ✅ 0x64 C2S (114 B): username[21], character[21], 22 zero bytes, then u32 3, u32 1, u32 1 (❓ constants of the
/// capture), zeros and, at 96, the character uid (❓ <c>0xABF5</c> = the uid of the <c>0x02</c> packet in the capture).
/// </summary>
public sealed record GlobalUserAuthRequest(string Username, string CharacterName, uint CharacterUid) : ITwoWayPacket<GlobalUserAuthRequest>
{
    public const int NameBytes = 21;

    public static ushort Opcode => 0x64;
    public static PacketLength Length => PacketLength.Exact(114);

    public static GlobalUserAuthRequest Read(PacketReader r)
    {
        var username = r.ReadFixedString(NameBytes);
        var character = r.ReadFixedString(NameBytes);
        r.Skip(54);                             // 42..95: the constants and zeros
        var uid = r.ReadU32();
        r.Skip(14);
        return new(username, character, uid);
    }

    public void Write(PacketWriter w)
    {
        w.WriteFixedString(Username, NameBytes);
        w.WriteFixedString(CharacterName, NameBytes);
        w.WriteZeros(22);                       // 42
        w.WriteU32(3);                          // 64 ❓
        w.WriteU32(1);                          // 68 ❓
        w.WriteU32(1);                          // 72 ❓
        w.WriteZeros(20);                       // 76
        w.WriteU32(CharacterUid);               // 96
        w.WriteZeros(14);
    }

    public override string ToString() => $"{nameof(GlobalUserAuthRequest)}({Username}, {CharacterName})";
}

/// <summary>✅ 0xDE C2S (50 B): u32 3, character[20], u32 x, u32 y (the cell of the tamer in the capture: 26, 37) and zeros; sent twice.</summary>
public sealed record GlobalJoinRequest(string CharacterName, int X, int Y) : ITwoWayPacket<GlobalJoinRequest>
{
    public const int NameBytes = 24;

    public static ushort Opcode => 0xDE;
    public static PacketLength Length => PacketLength.Exact(50);

    public static GlobalJoinRequest Read(PacketReader r)
    {
        r.ReadU32();                            // ❓ 3
        var name = r.ReadFixedString(NameBytes);
        var x = r.ReadI32();
        var y = r.ReadI32();
        r.Skip(14);
        return new(name, x, y);
    }

    public void Write(PacketWriter w)
    {
        w.WriteU32(3);                          // ❓
        w.WriteFixedString(CharacterName, NameBytes);
        w.WriteI32(X);
        w.WriteI32(Y);
        w.WriteZeros(14);
    }
}
