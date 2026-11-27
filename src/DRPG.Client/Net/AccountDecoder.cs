using DRPG.Shared.Packets.Channel;
using DRPG.Shared.Packets.Login;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Net;

/// <summary>
/// Turns the frames of the login and channel servers into typed packets. An opcode the client does not know is an
/// error: the original server only sends the ones below on these two connections (✅ captures, sessions 52–56).
/// </summary>
public static class AccountDecoder
{
    public static object Login(IncomingFrame frame) => frame.Opcode switch
    {
        (ushort)LoginOpcode.Handshake => ClientPacket.Parse<LoginHandshakeReply>(frame.Payload),
        _ => throw Unexpected("login", frame),
    };

    public static object Channel(IncomingFrame frame) => frame.Opcode switch
    {
        (ushort)ChannelOpcode.ChannelSelect => ClientPacket.Parse<ChannelSelectReply>(frame.Payload),
        (ushort)ChannelOpcode.Authenticate => ClientPacket.Parse<AuthenticateReply>(frame.Payload),
        (ushort)ChannelOpcode.CharacterList => ClientPacket.Parse<CharacterListReply>(frame.Payload),
        (ushort)ChannelOpcode.UserCheck => ClientPacket.Parse<UserCheckReply>(frame.Payload),
        (ushort)ChannelOpcode.SecondaryPassword => ClientPacket.Parse<SecondaryPasswordReply>(frame.Payload),
        (ushort)ChannelOpcode.CharacterCreate => ClientPacket.Parse<CharacterCreateReply>(frame.Payload),
        (ushort)ChannelOpcode.CharacterDelete => ClientPacket.Parse<CharacterDeleteReply>(frame.Payload),
        (ushort)ChannelOpcode.KeepAlive => ClientPacket.Parse<KeepAliveReply>(frame.Payload),
        _ => throw Unexpected("channel", frame),
    };

    private static PacketFormatException Unexpected(string server, IncomingFrame frame) =>
        new($"unexpected opcode 0x{frame.Opcode:X} ({frame.Header.Size} B) from the {server} server");
}
