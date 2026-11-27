using DRPG.Shared.Protocol;

namespace DRPG.Shared.Packets.Map;

// The packets of the map connection around the world entry (✅ captures.db session 52, connections 87–89; v322 server
// sequence in docs/cliente/cena-jogo.md#sequência-na-rede). The layouts the RE has not read in the client are taken from
// the captures: they are marked ❓.

/// <summary>
/// ✅ 0x0E S2C on the map and global connections (16 B, v322): the seed of the session's AES key (the map seed feeds the
/// <c>0xE6</c> experience event). The client answers with <see cref="SeedHelloEcho"/>.
/// </summary>
public sealed record SeedHelloPacket(byte[] Seed) : ITwoWayPacket<SeedHelloPacket>
{
    public const int SeedBytes = 16;

    public static ushort Opcode => 0x0E;
    public static PacketLength Length => PacketLength.Exact(SeedBytes);

    public static SeedHelloPacket Read(PacketReader r) => new(r.ReadBytes(SeedBytes));

    public void Write(PacketWriter w)
    {
        if (Seed.Length != SeedBytes)
            throw new InvalidOperationException($"the seed has {Seed.Length} bytes, expected {SeedBytes}");
        w.WriteBytes(Seed);
    }
}

/// <summary>✅ 0x0E C2S on the map and global connections (30 B, v322): the seed as received, then 14 zero bytes (❓ the header is 14 B more than the hello).</summary>
public sealed record SeedHelloEcho(byte[] Seed) : ITwoWayPacket<SeedHelloEcho>
{
    public static ushort Opcode => 0x0E;
    public static PacketLength Length => PacketLength.Exact(SeedHelloPacket.SeedBytes + 14);

    public static SeedHelloEcho Read(PacketReader r)
    {
        var seed = r.ReadBytes(SeedHelloPacket.SeedBytes);
        r.Skip(14);
        return new(seed);
    }

    public void Write(PacketWriter w)
    {
        if (Seed.Length != SeedHelloPacket.SeedBytes)
            throw new InvalidOperationException($"the seed has {Seed.Length} bytes, expected {SeedHelloPacket.SeedBytes}");
        w.WriteBytes(Seed);
        w.WriteZeros(14);
    }
}

/// <summary>
/// ✅ 0x49 C2S (18 B, v322): the first request on the map connection, sent three times before <c>0x01</c> in the
/// capture (✅ <c>0x009b5840</c>). ❓ The first byte is 1 in every capture, the rest zero; the server reads nothing.
/// </summary>
public sealed record JoinHandshakeRequest : ITwoWayPacket<JoinHandshakeRequest>
{
    public const int Times = 3;

    public static ushort Opcode => (ushort)MapOpcode.JoinHandshakeReq;
    public static PacketLength Length => PacketLength.Exact(18);

    public static JoinHandshakeRequest Read(PacketReader r)
    {
        r.Skip(18);
        return new();
    }

    public void Write(PacketWriter w)
    {
        w.WriteU8(1);                           // ❓
        w.WriteZeros(17);
    }
}

/// <summary>✅ 0x04 C2S (30 B, v322 <c>0x00abbb20</c>): the 16-byte id of the character (the serial of the <c>0x02</c> packet) and 14 B.</summary>
public sealed record RefreshCharacterInfoRequest(Serial Character) : ITwoWayPacket<RefreshCharacterInfoRequest>
{
    public static ushort Opcode => (ushort)MapOpcode.RefreshCharacterInfoReq;
    public static PacketLength Length => PacketLength.Exact(Serial.Length + 14);

    public static RefreshCharacterInfoRequest Read(PacketReader r)
    {
        var serial = Serial.Read(r);
        r.Skip(14);
        return new(serial);
    }

    public void Write(PacketWriter w)
    {
        Character.Write(w);
        w.WriteZeros(14);
    }
}

public enum RenameKind : byte { Tamer = 1, Digimon = 2 }

/// <summary>
/// ✅ 0xF0 C2S (42 B, v322): rename. u8 0, u8 kind (1 tamer, 2 digimon), u16 0, u32 uid, name[20] and 14 B. Without a name
/// it is a query. The client sends <c>(Tamer, 0, "")</c> when it enters the map (✅ capture: <c>00 01 00 00 …</c>).
/// </summary>
public sealed record RenameRequest(RenameKind Kind, uint Uid, string Name) : ITwoWayPacket<RenameRequest>
{
    public const int NameLength = 20;

    public static ushort Opcode => (ushort)MapOpcode.Rename;
    public static PacketLength Length => PacketLength.Exact(8 + NameLength + 14);

    public static RenameRequest Read(PacketReader r)
    {
        r.ReadU8();
        var kind = (RenameKind)r.ReadU8();
        r.ReadU16();
        var uid = r.ReadU32();
        var name = r.ReadFixedString(NameLength);
        r.Skip(14);
        return new(kind, uid, name);
    }

    public void Write(PacketWriter w)
    {
        w.WriteU8(0);
        w.WriteU8((byte)Kind);
        w.WriteU16(0);
        w.WriteU32(Uid);
        w.WriteFixedString(Name, NameLength);
        w.WriteZeros(14);
    }
}

/// <summary>✅ 0x121 C2S (14 B, v322 <c>0x00ae07b0(0x121)</c>): empty request sent once, on the first entry; the server answers with the captured block.</summary>
public sealed record Unknown121Request : ITwoWayPacket<Unknown121Request>
{
    public static ushort Opcode => (ushort)MapOpcode.Unknown121;
    public static PacketLength Length => PacketLength.Exact(14);

    public static Unknown121Request Read(PacketReader r)
    {
        r.Skip(14);
        return new();
    }

    public void Write(PacketWriter w) => w.WriteZeros(14);
}
