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
/// ✅ 0xB0 C2S (14 B zero, v322): sent by the client right after a cross-map transition loading screen finishes
/// (captures.db packet 1658: 28 B = 14 header + 14 zeros; <c>0x00aba410</c> @ <c>0x00aba757</c>).
/// </summary>
public sealed record TeleportAckRequest : ITwoWayPacket<TeleportAckRequest>
{
    public static ushort Opcode => (ushort)MapOpcode.TeleportCharacter;
    public static PacketLength Length => PacketLength.Exact(14);

    public static TeleportAckRequest Read(PacketReader r)
    {
        r.Skip(14);
        return new();
    }

    public void Write(PacketWriter w) => w.WriteZeros(14);
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

/// <summary>
/// ✅ 0x09 S2C (10 B, v322): dynamic/quest NPC add or remove on the field (captures.db: <c>8D00 5500 C800 FF00 0100</c>;
/// <c>0x00ac7331</c> → <c>0x00abb9e0</c> / <c>0x00acfed0</c>). Action: 1 = add, 0 = remove.
/// </summary>
public sealed record FieldNpcPacket(short MapId, short NpcId, short X, short Y, byte Action) : ITwoWayPacket<FieldNpcPacket>
{
    public const byte ActionRemove = 0;
    public const byte ActionAdd = 1;

    /// <summary>✅ <c>0x00acfed0</c>: a removal with map id 0 removes the NPC from every map.</summary>
    public const short AllMaps = 0;

    public static ushort Opcode => (ushort)MapOpcode.FieldNpcUpdate;
    public static PacketLength Length => PacketLength.Exact(10);

    public static FieldNpcPacket Read(PacketReader r)
    {
        var mapId = r.ReadI16();
        var npcId = r.ReadI16();
        var x = r.ReadI16();
        var y = r.ReadI16();
        var action = r.ReadU8();
        r.ReadU8();
        return new(mapId, npcId, x, y, action);
    }

    public void Write(PacketWriter w)
    {
        w.WriteI16(MapId);
        w.WriteI16(NpcId);
        w.WriteI16(X);
        w.WriteI16(Y);
        w.WriteU8(Action);
        w.WriteU8(0);
    }
}

/// <summary>
/// ✅ 0x2B C2S (14 B zero, v322): the player confirmed the exit window's "Close" (<c>0x00b80e80</c> → <c>0x00ae4bf0</c>,
/// <c>0x009b4ab0(0x2b, 0, 0)</c>); captures.db: 28 B (14 header + 14 zeros), answered by an empty S2C 0x2B.
/// </summary>
public sealed record ExitGameRequest : ITwoWayPacket<ExitGameRequest>
{
    public static ushort Opcode => (ushort)MapOpcode.ExitGame;
    public static PacketLength Length => PacketLength.Exact(14);

    public static ExitGameRequest Read(PacketReader r)
    {
        r.Skip(14);
        return new();
    }

    public void Write(PacketWriter w) => w.WriteZeros(14);
}

/// <summary>
/// ✅ 0x2B S2C (empty, captures.db: the answer to the C2S 0x2B): the server lets the player leave at once. The map dispatcher
/// (<c>0xabe26d</c>) calls <c>0x00b816e0(1)</c> on window 6: the count shows an "Exit" button that leaves without waiting.
/// </summary>
public sealed record ExitAllowedPacket : ITwoWayPacket<ExitAllowedPacket>
{
    public static ushort Opcode => (ushort)MapOpcode.ExitGame;
    public static PacketLength Length => PacketLength.Exact(0);

    public static ExitAllowedPacket Read(PacketReader r) => new();

    public void Write(PacketWriter w)
    {
    }
}
