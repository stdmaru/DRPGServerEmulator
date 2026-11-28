using DRPG.Client.Net;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Tests;

/// <summary>The decoders of the map and global servers with the packets of the world entry and the ones a broken server could send.</summary>
public sealed class MapDecoderTests
{
    private static IncomingFrame Frame(ushort opcode, int size) =>
        new(new PacketHeader(opcode, (uint)(PacketHeader.Length + size), 0, 0), new byte[size]);

    [Fact]
    public void The_hello_is_decoded_with_its_seed()
    {
        var seed = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        var frame = new IncomingFrame(new PacketHeader(0x0E, (uint)(PacketHeader.Length + 16), 0, 0), seed);

        var packet = Assert.IsType<SeedHelloPacket>(MapDecoder.Map(frame));

        Assert.Equal(seed, packet.Seed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    [InlineData(30)]
    public void A_hello_with_the_wrong_size_is_rejected(int size) =>
        Assert.Throws<PacketFormatException>(() => MapDecoder.Map(Frame(0x0E, size)));

    [Fact]
    public void The_ping_is_an_empty_frame()
    {
        Assert.IsType<MapPing>(MapDecoder.Map(Frame(0xC4, 0)));
        Assert.Throws<PacketFormatException>(() => MapDecoder.Map(Frame(0xC4, 1)));
    }

    [Fact]
    public void The_exit_answer_is_an_empty_0x2b()
    {
        Assert.IsType<ExitAllowedPacket>(MapDecoder.Map(Frame(0x2B, 0)));
        Assert.Throws<PacketFormatException>(() => MapDecoder.Map(Frame(0x2B, 14)));
    }

    [Theory]
    [InlineData(0x33, 504)]
    [InlineData(0x05, 164)]
    [InlineData(0x7E, 1940)]
    [InlineData(0x7F, 2708)]
    [InlineData(0x74, 60)]
    [InlineData(0x152, 7048)]
    [InlineData(0xB4, 696)]
    [InlineData(0x73, 280)]
    [InlineData(0xE6, 32)]
    [InlineData(0x10, 204)]
    [InlineData(0xF0, 28)]
    [InlineData(0x121, 10304)]
    public void The_packets_not_used_yet_are_accepted_only_with_the_size_of_the_capture(int opcode, int size)
    {
        var ignored = Assert.IsType<IgnoredPacket>(MapDecoder.Map(Frame((ushort)opcode, size)));

        Assert.Equal(((ushort)opcode, size), (ignored.Opcode, ignored.PayloadLength));
        Assert.Throws<PacketFormatException>(() => MapDecoder.Map(Frame((ushort)opcode, size - 1)));
        Assert.Throws<PacketFormatException>(() => MapDecoder.Map(Frame((ushort)opcode, size + 1)));
        Assert.Throws<PacketFormatException>(() => MapDecoder.Map(Frame((ushort)opcode, 0)));
    }

    [Theory]
    [InlineData(0x02)]
    [InlineData(0x03)]
    public void The_world_data_with_an_empty_or_short_payload_is_rejected(int opcode)
    {
        Assert.Throws<PacketFormatException>(() => MapDecoder.Map(Frame((ushort)opcode, 0)));
        Assert.Throws<PacketFormatException>(() => MapDecoder.Map(Frame((ushort)opcode, 100)));
    }

    [Theory]
    [InlineData(0x0000)]
    [InlineData(0x7777)]
    [InlineData(0xFFFF)]
    public void An_opcode_the_client_does_not_know_is_unknown_not_an_error(int opcode)
    {
        var unknown = Assert.IsType<UnknownPacket>(MapDecoder.Map(Frame((ushort)opcode, 5)));

        Assert.Equal(((ushort)opcode, 5), (unknown.Opcode, unknown.PayloadLength));
    }

    [Fact]
    public void The_global_server_is_read_only_for_its_hello()
    {
        Assert.IsType<SeedHelloPacket>(MapDecoder.Global(Frame(0x0E, 16)));
        Assert.IsType<UnknownPacket>(MapDecoder.Global(Frame(0x02, 4000)));
        Assert.IsType<UnknownPacket>(MapDecoder.Global(Frame(0xC4, 0)));
        Assert.Throws<PacketFormatException>(() => MapDecoder.Global(Frame(0x0E, 15)));
    }
}
