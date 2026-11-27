using DRPG.Shared.Protocol;

namespace DRPG.Shared.Packets.Channel;

/// <summary>✅ 0x12 C2S (14 B zero, v322): asks for the character list; the client sends it on every channel connection.</summary>
public sealed record CharacterListRequest : ITwoWayPacket<CharacterListRequest>
{
    public static ushort Opcode => (ushort)ChannelOpcode.CharacterList;
    public static PacketLength Length => PacketLength.Exact(14);

    public static CharacterListRequest Read(PacketReader r)
    {
        r.Skip(14);
        return new();
    }

    public void Write(PacketWriter w) => w.WriteZeros(14);
}

/// <summary>
/// ✅ One of the four 124 B slots of the 0x12 reply (docs/protocolo/structs/channel.md). An empty slot has uid 0 and
/// only the slot index (and the check) filled. <see cref="CheckOffset"/> is a random value that changes in every list
/// and the check is <c>offset + map + level</c> ✅ (18 lists, sessions 52–56).
/// </summary>
public sealed record CharacterListSlot(
    uint Uid, byte TamerId, string Name, ushort Level, short X, short Y,
    ushort LeaderSpeciesId, ushort LeaderLevel, string LeaderName, int TotalBattles, int TotalWins,
    byte Slot, byte MapId, int CheckOffset)
{
    public const int NameBytes = 21;
    public const int LeaderNameBytes = 24;
    public const int Length = 124;

    /// <summary>❓ 11 × u32 of equipment; always zero in the 18 v322 lists, even after equipping.</summary>
    private const int EquipmentBytes = 44;

    public bool IsEmpty => Uid == 0;

    /// <summary>An empty slot, as the original sends it.</summary>
    public static CharacterListSlot Empty(byte slot, int checkOffset) =>
        new(0, 0, "", 0, 0, 0, 0, 0, "", 0, 0, slot, 0, checkOffset);

    public static CharacterListSlot Read(PacketReader r)
    {
        var uid = r.ReadU32();
        var tamerId = r.ReadU8();
        var name = r.ReadFixedString(NameBytes);
        var level = r.ReadU16();
        var x = r.ReadI16();
        var y = r.ReadI16();
        r.Skip(EquipmentBytes);
        var leaderSpecies = r.ReadU16();
        var leaderLevel = r.ReadU16();
        var leaderName = r.ReadFixedString(LeaderNameBytes);
        var battles = r.ReadI32();
        var wins = r.ReadI32();
        var slot = r.ReadU8();
        var mapId = r.ReadU8();
        r.ReadI16();                            // 0
        var check = r.ReadI32();
        var offset = r.ReadI32();
        // ✅ the check is always offset + map + level; anything else is a broken or forged list
        if (check - offset != mapId + level)
            throw new PacketFormatException($"character slot {slot}: check {check - offset} for map {mapId} level {level}");
        return new(uid, tamerId, name, level, x, y, leaderSpecies, leaderLevel, leaderName, battles, wins, slot, mapId, offset);
    }

    public void Write(PacketWriter w)
    {
        w.WriteU32(Uid);
        w.WriteU8(TamerId);
        w.WriteFixedString(Name, NameBytes);
        w.WriteU16(Level);
        w.WriteI16(X);
        w.WriteI16(Y);
        w.WriteZeros(EquipmentBytes);
        w.WriteU16(LeaderSpeciesId);
        w.WriteU16(LeaderLevel);
        w.WriteFixedString(LeaderName, LeaderNameBytes);
        w.WriteI32(TotalBattles);
        w.WriteI32(TotalWins);
        w.WriteU8(Slot);
        w.WriteU8(MapId);
        w.WriteI16(0);
        w.WriteI32(CheckOffset + MapId + Level);
        w.WriteI32(CheckOffset);
    }
}

/// <summary>✅ 0x12 S2C (500 B, v322): <c>i32 1</c> and the four slots.</summary>
public sealed record CharacterListReply(IReadOnlyList<CharacterListSlot> Slots) : ITwoWayPacket<CharacterListReply>
{
    public const int SlotCount = 4;

    public static ushort Opcode => (ushort)ChannelOpcode.CharacterList;
    public static PacketLength Length => PacketLength.Exact(4 + SlotCount * CharacterListSlot.Length);

    public static CharacterListReply Read(PacketReader r)
    {
        r.ReadI32();                            // 1 ❓
        var slots = new CharacterListSlot[SlotCount];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = CharacterListSlot.Read(r);
        return new(slots);
    }

    public void Write(PacketWriter w)
    {
        if (Slots.Count != SlotCount)
            throw new InvalidOperationException($"character list with {Slots.Count} slots");
        w.WriteI32(1);
        foreach (var slot in Slots)
            slot.Write(w);
    }
}

/// <summary>
/// Header of the create request (0x10), echoed by the official server in the reply.
/// ✅ sessions 3, 4 and 53: slot chosen by the client, map 0x4F (tutorial) and two u16 that vary (3B..3E and 37..3A).
/// ❓ They look like coordinates, but the new character shows up elsewhere.
/// </summary>
public readonly record struct CharacterCreateHeader(byte Slot, byte MapId, ushort A, ushort B)
{
    public static CharacterCreateHeader Read(PacketReader r) => new(r.ReadU8(), r.ReadU8(), r.ReadU16(), r.ReadU16());

    public void Write(PacketWriter w)
    {
        w.WriteU8(Slot);
        w.WriteU8(MapId);
        w.WriteU16(A);
        w.WriteU16(B);
    }
}

/// <summary>
/// ✅ 0x10 C2S (78 B, v322): <c>i32 1, i32 0</c>, header (u8 slot, u8 map, u16, u16), <c>u8 tamer, name[21], u16
/// digimon, digimon name[26]</c> and 14 B.
/// </summary>
public sealed record CharacterCreateRequest(CharacterCreateHeader Header, byte TamerId, string Name, ushort DigimonId, string DigimonName)
    : ITwoWayPacket<CharacterCreateRequest>
{
    public const int DigimonNameBytes = 26;

    public static ushort Opcode => (ushort)ChannelOpcode.CharacterCreate;
    public static PacketLength Length => PacketLength.Exact(78);

    public static CharacterCreateRequest Read(PacketReader r)
    {
        r.ReadI32();
        r.ReadI32();
        var header = CharacterCreateHeader.Read(r);
        var tamerId = r.ReadU8();
        var name = r.ReadFixedString(CharacterListSlot.NameBytes);
        var digimonId = r.ReadU16();
        var digimonName = r.ReadFixedString(DigimonNameBytes);
        r.Skip(14);
        return new(header, tamerId, name, digimonId, digimonName);
    }

    public void Write(PacketWriter w)
    {
        w.WriteI32(1);
        w.WriteI32(0);
        Header.Write(w);
        w.WriteU8(TamerId);
        w.WriteFixedString(Name, CharacterListSlot.NameBytes);
        w.WriteU16(DigimonId);
        w.WriteFixedString(DigimonName, DigimonNameBytes);
        w.WriteZeros(14);
    }
}

/// <summary>✅ 0x10 S2C (64 B, v322): the result and the request bytes from 0x08 on, echoed.</summary>
public sealed record CharacterCreateReply(int ErrorCode, CharacterCreateHeader Header, byte TamerId, string Name, ushort DigimonId, string DigimonName)
    : ITwoWayPacket<CharacterCreateReply>
{
    public static ushort Opcode => (ushort)ChannelOpcode.CharacterCreate;
    public static PacketLength Length => PacketLength.Exact(64);

    public static CharacterCreateReply Read(PacketReader r)
    {
        var errorCode = ChannelResult.Read(r);
        var header = CharacterCreateHeader.Read(r);
        var tamerId = r.ReadU8();
        var name = r.ReadFixedString(CharacterListSlot.NameBytes);
        var digimonId = r.ReadU16();
        var digimonName = r.ReadFixedString(CharacterCreateRequest.DigimonNameBytes);
        return new(errorCode, header, tamerId, name, digimonId, digimonName);
    }

    public void Write(PacketWriter w)
    {
        ChannelResult.Write(w, ErrorCode);
        Header.Write(w);
        w.WriteU8(TamerId);
        w.WriteFixedString(Name, CharacterListSlot.NameBytes);
        w.WriteU16(DigimonId);
        w.WriteFixedString(DigimonName, CharacterCreateRequest.DigimonNameBytes);
    }
}

/// <summary>✅ 0x11 C2S (66 B, v322): <c>i32 1, i32 0, u32 uid, MD5 of the password[40]</c> and 14 B. The client sends the MD5 of "".</summary>
public sealed record CharacterDeleteRequest(uint CharacterUid, string PasswordMd5) : ITwoWayPacket<CharacterDeleteRequest>
{
    public const int Md5Bytes = 40;

    public static ushort Opcode => (ushort)ChannelOpcode.CharacterDelete;
    public static PacketLength Length => PacketLength.Exact(66);

    public static CharacterDeleteRequest Read(PacketReader r)
    {
        r.ReadI32();
        r.ReadI32();
        var uid = r.ReadU32();
        var passwordMd5 = r.ReadFixedString(Md5Bytes);   // ❓ the original checks it; the emulator has no local password
        r.Skip(14);
        return new(uid, passwordMd5);
    }

    public void Write(PacketWriter w)
    {
        w.WriteI32(1);
        w.WriteI32(0);
        w.WriteU32(CharacterUid);
        w.WriteFixedString(PasswordMd5, Md5Bytes);
        w.WriteZeros(14);
    }

    /// <summary>Never prints the password hash.</summary>
    public override string ToString() => $"{nameof(CharacterDeleteRequest)}({CharacterUid})";
}

/// <summary>✅ 0x11 S2C (52 B, v322, session 53): result (2/0 on success, 3/code on failure) and the uid and MD5 of the request, echoed.</summary>
public sealed record CharacterDeleteReply(int ErrorCode, uint CharacterUid, string PasswordMd5) : ITwoWayPacket<CharacterDeleteReply>
{
    public static ushort Opcode => (ushort)ChannelOpcode.CharacterDelete;
    public static PacketLength Length => PacketLength.Exact(8 + 4 + CharacterDeleteRequest.Md5Bytes);

    public bool Succeeded => ErrorCode == ChannelError.None;

    public static CharacterDeleteReply Read(PacketReader r)
    {
        var errorCode = ChannelResult.Read(r);
        var uid = r.ReadU32();
        return new(errorCode, uid, r.ReadFixedString(CharacterDeleteRequest.Md5Bytes));
    }

    public void Write(PacketWriter w)
    {
        ChannelResult.Write(w, ErrorCode);
        w.WriteU32(CharacterUid);
        w.WriteFixedString(PasswordMd5, CharacterDeleteRequest.Md5Bytes);
    }

    /// <summary>Never prints the password hash.</summary>
    public override string ToString() => $"{nameof(CharacterDeleteReply)}({ErrorCode}, {CharacterUid})";
}
