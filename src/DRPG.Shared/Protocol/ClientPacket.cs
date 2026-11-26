namespace DRPG.Shared.Protocol;

/// <summary>Accepted payload length of a C2S packet (without the 14-byte header).</summary>
public readonly record struct PacketLength(int Min, int Max)
{
    /// <summary>The client always sends this length (✅ in captures or in the binary).</summary>
    public static PacketLength Exact(int length) => new(length, length);

    /// <summary>The length really varies; the request itself reads or skips the rest.</summary>
    public static PacketLength AtLeast(int length) => new(length, PacketHeader.MaxPacketSize - PacketHeader.Length);

    public bool Accepts(int length) => length >= Min && length <= Max;

    public override string ToString() => Min == Max ? $"{Min} B" : $"{Min}..{Max} B";
}

/// <summary>
/// A C2S request: describes the payload length and layout. Only <see cref="Read"/> touches bytes; handlers get the
/// typed request (docs/emulador/handlers.md).
/// </summary>
public interface IClientPacket<TSelf> where TSelf : IClientPacket<TSelf>
{
    static abstract PacketLength Length { get; }

    /// <summary>Reads the whole payload: unused fields are skipped here, with a comment saying what they are.</summary>
    static abstract TSelf Read(PacketReader reader);
}

public static class ClientPacket
{
    /// <summary>
    /// Checks the length, reads the request and requires the layout to consume the whole payload.
    /// Any mismatch is a <see cref="PacketFormatException"/>.
    /// </summary>
    public static T Parse<T>(ReadOnlyMemory<byte> payload) where T : IClientPacket<T>
    {
        if (!T.Length.Accepts(payload.Length))
            throw new PacketFormatException($"{typeof(T).Name}: {payload.Length} B, expected {T.Length}");
        var reader = new PacketReader(payload);
        var request = T.Read(reader);
        if (reader.Remaining != 0)
            throw new PacketFormatException($"{typeof(T).Name}: {reader.Remaining} B left after the layout");
        return request;
    }
}

/// <summary>Payload of a C2S packet the receiver does not read (echo, ping, unused data). Any length.</summary>
public sealed record IgnoredPayload : IClientPacket<IgnoredPayload>
{
    private static readonly IgnoredPayload Instance = new();

    public static PacketLength Length => PacketLength.AtLeast(0);

    public static IgnoredPayload Read(PacketReader reader)
    {
        reader.Skip(reader.Remaining);
        return Instance;
    }
}
