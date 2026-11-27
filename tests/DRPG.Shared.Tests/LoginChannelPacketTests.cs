using DRPG.Shared.Packets.Channel;
using DRPG.Shared.Packets.Login;
using DRPG.Shared.Protocol;

namespace DRPG.Shared.Tests;

public sealed class LoginChannelPacketTests
{
    private static byte[] Payload<T>(T packet) where T : ITwoWayPacket<T> => TwoWayPacket.EncodePayload(packet);

    private static T RoundTrip<T>(T packet) where T : ITwoWayPacket<T> => ClientPacket.Parse<T>(Payload(packet));

    /// <summary>Read(Write(x)) writes the same bytes again, for every packet (records with arrays do not compare by value).</summary>
    private static void AssertRoundTrip<T>(T packet) where T : ITwoWayPacket<T> =>
        Assert.Equal(Payload(packet), Payload(RoundTrip(packet)));

    private static CharacterListSlot Slot(byte index, uint uid = 7) =>
        new(uid, 2, "Tamer", 14, 57, 91, 619, 12, "Agumon", 6, 5, index, 6, 40 + index);

    [Fact]
    public void Every_packet_round_trips()
    {
        AssertRoundTrip(new LoginHandshakeRequest("user"));
        AssertRoundTrip(new LoginHandshakeReply("user"));
        AssertRoundTrip(new ChannelSelectRequest());
        AssertRoundTrip(new ChannelSelectReply(2));
        AssertRoundTrip(new AuthenticateRequest("user", new string('a', 40), "18-C0-4D-3B-04-C6", "", "x"));
        AssertRoundTrip(new AuthenticateReply(0, "user"));
        AssertRoundTrip(new AuthenticateReply(ChannelError.InvalidCredentials, "user", new string('b', 40), "m1", "m2", "m3"));
        AssertRoundTrip(new UserCheckRequest());
        AssertRoundTrip(new UserCheckReply("user"));
        AssertRoundTrip(new MapConnectionConfirmRequest("user", "Tamer"));
        AssertRoundTrip(new KeepAliveRequest());
        AssertRoundTrip(new KeepAliveReply());
        AssertRoundTrip(new CharacterListRequest());
        AssertRoundTrip(new CharacterListReply([Slot(0), CharacterListSlot.Empty(1, 9), Slot(2, 8), CharacterListSlot.Empty(3, 0)]));
        AssertRoundTrip(new CharacterCreateRequest(new CharacterCreateHeader(3, 0x4F, 0x3E, 0x38), 1, "Tamer", 10, "Agumon"));
        AssertRoundTrip(new CharacterCreateReply(ChannelError.NameTaken, new CharacterCreateHeader(3, 0x4F, 0x3E, 0x38), 1, "Tamer", 10, "Agumon"));
        AssertRoundTrip(new CharacterDeleteRequest(7, new string('c', 40)));
        AssertRoundTrip(new CharacterDeleteReply(ChannelError.None, 7, new string('c', 40)));
        AssertRoundTrip(new CharacterDeleteReply(2000, 7, new string('c', 40)));
        AssertRoundTrip(new CharacterDeleteReply(105, 7, new string('c', 40)));
    }

    [Fact]
    public void Fields_survive_the_round_trip()
    {
        var auth = RoundTrip(new AuthenticateRequest("user", new string('a', 40), "m1", "m2", "m3"));
        Assert.Equal(("user", new string('a', 40), "m1", "m2", "m3"), (auth.Username, auth.AuthKey, auth.Mac1, auth.Mac2, auth.Mac3));

        var list = RoundTrip(new CharacterListReply([Slot(0), CharacterListSlot.Empty(1, 9), Slot(2, 8), CharacterListSlot.Empty(3, 0)]));
        Assert.Equal(Slot(0), list.Slots[0]);
        Assert.True(list.Slots[1].IsEmpty);
        Assert.Equal(Slot(2, 8), list.Slots[2]);
        Assert.Equal((byte)3, list.Slots[3].Slot);
    }

    [Fact]
    public void Layouts_match_the_captured_packets()
    {
        // ✅ captures.db session 56 (user name replaced): 0x05 and 0x03 in both directions
        Assert.Equal(Convert.FromHexString("01000000" + "00000000" + "75736572" + new string('0', 32 + 28)), Payload(new LoginHandshakeRequest("user")));
        Assert.Equal(Convert.FromHexString("03000000" + "10270000" + "75736572" + new string('0', 32)), Payload(new LoginHandshakeReply("user", 3, 0x2710)));

        var name = Convert.ToHexString("MoveInteractive_Digimon_111"u8);
        Assert.Equal(Convert.FromHexString("01000000" + "00000000" + "00000000" + name + new string('0', 26 + 28)), Payload(new ChannelSelectRequest()));
        Assert.Equal(Convert.FromHexString("02000000" + "00000000" + "02000000" + name + new string('0', 26)), Payload(new ChannelSelectReply(2)));
    }

    [Fact]
    public void Payload_sizes_match_the_captures()
    {
        Assert.Equal(42, Payload(new LoginHandshakeRequest("u")).Length);
        Assert.Equal(28, Payload(new LoginHandshakeReply("u")).Length);
        Assert.Equal(66, Payload(new ChannelSelectRequest()).Length);
        Assert.Equal(52, Payload(new ChannelSelectReply(0)).Length);
        Assert.Equal(462, Payload(new AuthenticateRequest("u", "k")).Length);
        Assert.Equal(448, Payload(new AuthenticateReply(0, "u")).Length);
        Assert.Equal(14, Payload(new CharacterListRequest()).Length);
        Assert.Equal(500, Payload(new CharacterListReply([Slot(0), Slot(1), Slot(2), Slot(3)])).Length);
        Assert.Equal(36, Payload(new UserCheckRequest()).Length);
        Assert.Equal(22, Payload(new UserCheckReply("u")).Length);
        Assert.Equal(55, Payload(new MapConnectionConfirmRequest("u", "c")).Length);
        Assert.Empty(Payload(new KeepAliveReply()));
    }

    [Fact]
    public void A_truncated_or_padded_payload_is_malformed()
    {
        var full = Payload(new AuthenticateReply(0, "user"));
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<AuthenticateReply>(full[..^1]));
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<AuthenticateReply>(full.Append((byte)0).ToArray()));
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<CharacterListReply>(Array.Empty<byte>()));
    }

    [Fact]
    public void A_result_that_disagrees_with_its_code_is_malformed()
    {
        var payload = Payload(new ChannelSelectReply(0));
        payload[0] = 3;                         // result "error" with code 0
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<ChannelSelectReply>(payload));
    }

    [Fact]
    public void A_character_slot_with_a_wrong_check_is_malformed()
    {
        var payload = Payload(new CharacterListReply([Slot(0), Slot(1), Slot(2), Slot(3)]));
        payload[4 + 116] ^= 0x01;               // check of the first slot
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<CharacterListReply>(payload));
    }

    [Fact]
    public void A_list_must_have_exactly_four_slots()
    {
        Assert.Throws<InvalidOperationException>(() => Payload(new CharacterListReply([Slot(0)])));
    }

    [Fact]
    public void Secrets_stay_out_of_the_text_form()
    {
        Assert.DoesNotContain("secretkey", new AuthenticateRequest("user", "secretkey").ToString());
        Assert.DoesNotContain("secretkey", new AuthenticateReply(101, "user", "secretkey").ToString());
        Assert.DoesNotContain("secretmd5", new CharacterDeleteRequest(1, "secretmd5").ToString());
    }
}
