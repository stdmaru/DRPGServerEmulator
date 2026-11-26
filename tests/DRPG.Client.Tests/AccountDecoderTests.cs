using DRPG.Client.Net;
using DRPG.Shared.Packets.Channel;
using DRPG.Shared.Packets.Login;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Tests;

/// <summary>The decoders of the login and channel servers with packets a broken or forged server could send.</summary>
public sealed class AccountDecoderTests
{
    private static IncomingFrame Frame(ushort opcode, byte[] payload) =>
        new(new PacketHeader(opcode, (uint)(PacketHeader.Length + payload.Length), 0, 0), payload);

    [Fact]
    public void Decodes_the_replies_the_client_expects()
    {
        var login = AccountDecoder.Login(Frame((ushort)LoginOpcode.Handshake, TwoWayPacket.EncodePayload(new LoginHandshakeReply("tamer"))));
        var channel = AccountDecoder.Channel(Frame((ushort)ChannelOpcode.UserCheck, TwoWayPacket.EncodePayload(new UserCheckReply("tamer"))));

        Assert.Equal(new LoginHandshakeReply("tamer"), login);
        Assert.Equal(new UserCheckReply("tamer"), channel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(27)]
    [InlineData(29)]
    [InlineData(4096)]
    public void A_login_reply_with_the_wrong_size_is_rejected(int size) =>
        Assert.Throws<PacketFormatException>(() => AccountDecoder.Login(Frame((ushort)LoginOpcode.Handshake, new byte[size])));

    [Theory]
    [InlineData(ChannelOpcode.ChannelSelect, 51)]
    [InlineData(ChannelOpcode.Authenticate, 447)]
    [InlineData(ChannelOpcode.CharacterList, 499)]
    [InlineData(ChannelOpcode.CharacterList, 501)]
    [InlineData(ChannelOpcode.UserCheck, 0)]
    public void A_channel_reply_with_the_wrong_size_is_rejected(ChannelOpcode opcode, int size) =>
        Assert.Throws<PacketFormatException>(() => AccountDecoder.Channel(Frame((ushort)opcode, new byte[size])));

    [Theory]
    [InlineData(0x0000)]
    [InlineData(0xFFFF)]
    public void An_opcode_the_login_server_never_sends_is_rejected(ushort opcode) =>
        Assert.Throws<PacketFormatException>(() => AccountDecoder.Login(Frame(opcode, [])));

    [Theory]
    [InlineData(0x0000)]
    [InlineData(0x0013)]
    [InlineData(0x0018)]
    [InlineData(0xFFFF)]
    public void An_opcode_the_channel_server_never_sends_is_rejected(ushort opcode) =>
        Assert.Throws<PacketFormatException>(() => AccountDecoder.Channel(Frame(opcode, [])));
}
