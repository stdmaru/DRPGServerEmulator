using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Net;

/// <summary>A packet the client accepts and does not use yet (the later steps of milestone 3 read them): only its size was checked.</summary>
public sealed record IgnoredPacket(ushort Opcode, int PayloadLength);

/// <summary>An opcode the client does not know yet. Not an error: the world sends many packets the later steps will read.</summary>
public sealed record UnknownPacket(ushort Opcode, int PayloadLength);

/// <summary>The S2C <c>0xC4</c> ping of the map server: the client answers with its own <c>0xC4</c> (✅ capture: 5.35 s and 10.54 s, answered at once).</summary>
public sealed record MapPing;

/// <summary>
/// Turns the frames of the map and global servers into typed packets. The packets of the world entry that this step
/// reads are typed; the others that the official server sends on entry are checked for their size (✅ the sizes of
/// captures.db session 52, connection 88) and ignored; an opcode the client does not know is an <see cref="UnknownPacket"/>.
/// </summary>
public static class MapDecoder
{
    /// <summary>✅ Payload sizes of the entry packets that are not read yet (docs/cliente/cena-jogo.md#sequência-na-rede).</summary>
    private static readonly Dictionary<ushort, int> IgnoredSizes = new()
    {
        [0x33] = 504,           // join handshake reply, three times
        [0x05] = 164,
        [0x7E] = 1940,          // seven pages
        [0x7F] = 2708,          // seven pages
        [0x74] = 60,
        [0x152] = 7048,
        [0xB4] = 696,           // the leader's status block
        [0x73] = 280,
        [0xE6] = 32,            // the EXP event (AES with the seed of 0x0E)
        [0x10] = 204,           // answer to 0x04
        [0xF0] = 28,            // answer to the rename query
        [0x121] = 10304,
    };

    public static object Map(IncomingFrame frame)
    {
        switch (frame.Opcode)
        {
            case (ushort)MapOpcode.Hello:
                return ClientPacket.Parse<SeedHelloPacket>(frame.Payload);
            case (ushort)MapOpcode.CharacterData:
                return ClientPacket.Parse<CharacterDataPacket>(frame.Payload);
            case (ushort)MapOpcode.InventoryData:
                return ClientPacket.Parse<InventoryDataPacket>(frame.Payload);
            case (ushort)MapOpcode.KeepAlive:
                if (frame.Payload.Length != 0)
                    throw new PacketFormatException($"keep-alive with {frame.Payload.Length} B");
                return new MapPing();
            case (ushort)MapOpcode.TeleportCharacter:
                return ClientPacket.Parse<TeleportCharacterPacket>(frame.Payload);
        }
        if (IgnoredSizes.TryGetValue(frame.Opcode, out int size))
        {
            if (frame.Payload.Length != size)
                throw new PacketFormatException($"opcode 0x{frame.Opcode:X}: {frame.Payload.Length} B, expected {size}");
            return new IgnoredPacket(frame.Opcode, size);
        }
        return new UnknownPacket(frame.Opcode, frame.Payload.Length);
    }

    /// <summary>The global server: only its hello is read.</summary>
    public static object Global(IncomingFrame frame) =>
        frame.Opcode == (ushort)MapOpcode.Hello
            ? ClientPacket.Parse<SeedHelloPacket>(frame.Payload)
            : new UnknownPacket(frame.Opcode, frame.Payload.Length);
}
