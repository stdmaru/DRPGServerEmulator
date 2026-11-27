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

/// <summary>✅ 0xB0 S2C (8 B, v322): teleport or position correction. i32 mapId, i16 x, i16 y (captures.db: 2E000000 0F00 0F00).</summary>
public sealed record TeleportCharacterPacket(int MapId, short X, short Y) : ITwoWayPacket<TeleportCharacterPacket>
{
    public static ushort Opcode => (ushort)MapOpcode.TeleportCharacter;
    public static PacketLength Length => PacketLength.Exact(8);

    public static TeleportCharacterPacket Read(PacketReader r) => new(r.ReadI32(), r.ReadI16(), r.ReadI16());

    public void Write(PacketWriter w)
    {
        w.WriteI32(MapId);
        w.WriteI16(X);
        w.WriteI16(Y);
    }
}

/// <summary>
/// ✅ 0x11 S2C (56 B, v322): wild digimon group update on the field (docs/mecanicas/selvagens.md, <c>0x00af25c0</c>).
/// State: 1 standing, 4 walking, 8 defeated, 11 in battle, 12 leaving.
/// </summary>
public sealed record FieldDigimonPacket(
    Serial Serial,
    string Name,
    ushort SpeciesId,
    ushort Stage,
    ushort Level,
    byte Grade,
    byte State,
    ushort Direction,
    short X,
    short Y,
    short DestX,
    short DestY) : ITwoWayPacket<FieldDigimonPacket>
{
    public const int NameBytes = 21;

    public static ushort Opcode => (ushort)MapOpcode.FieldDigimonUpdate;
    public static PacketLength Length => PacketLength.Exact(56);

    public static FieldDigimonPacket Read(PacketReader r)
    {
        var serial = Serial.Read(r);
        var name = r.ReadFixedString(NameBytes);
        r.ReadU8();
        var speciesId = r.ReadU16();
        var stage = r.ReadU16();
        var level = r.ReadU16();
        var grade = r.ReadU8();
        var state = r.ReadU8();
        var direction = r.ReadU16();
        var x = r.ReadI16();
        var y = r.ReadI16();
        var destX = r.ReadI16();
        var destY = r.ReadI16();
        return new(serial, name, speciesId, stage, level, grade, state, direction, x, y, destX, destY);
    }

    public void Write(PacketWriter w)
    {
        Serial.Write(w);
        w.WriteFixedString(Name, NameBytes);
        w.WriteU8(0);
        w.WriteU16(SpeciesId);
        w.WriteU16(Stage);
        w.WriteU16(Level);
        w.WriteU8(Grade);
        w.WriteU8(State);
        w.WriteU16(Direction);
        w.WriteI16(X);
        w.WriteI16(Y);
        w.WriteI16(DestX);
        w.WriteI16(DestY);
    }
}
