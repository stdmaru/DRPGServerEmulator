using DRPG.Shared.Packets.Channel;
using DRPG.Shared.Packets.Global;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Shared.Tests;

/// <summary>The packets of the world entry (map, channel and global), checked against the bytes of captures.db session 52.</summary>
public sealed class WorldEntryPacketTests
{
    private static byte[] Payload<T>(T packet) where T : ITwoWayPacket<T> => TwoWayPacket.EncodePayload(packet);

    private static T RoundTrip<T>(T packet) where T : ITwoWayPacket<T> => ClientPacket.Parse<T>(Payload(packet));

    private static readonly byte[] Seed = Convert.FromHexString("1B16AEFDBDA5EBCEF639B03BBB043823");

    [Fact]
    public void The_hello_and_its_echo_match_the_capture()
    {
        Assert.Equal(Seed, Payload(new SeedHelloPacket(Seed)));
        Assert.Equal(Seed, RoundTrip(new SeedHelloPacket(Seed)).Seed);
        Assert.Equal(Convert.FromHexString("1B16AEFDBDA5EBCEF639B03BBB043823" + new string('0', 28)), Payload(new SeedHelloEcho(Seed)));
        Assert.Equal(Seed, RoundTrip(new SeedHelloEcho(Seed)).Seed);
    }

    [Fact]
    public void A_seed_of_another_size_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => Payload(new SeedHelloPacket([1, 2, 3])));
        Assert.Throws<InvalidOperationException>(() => Payload(new SeedHelloEcho([])));
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<SeedHelloPacket>(new byte[15]));
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<SeedHelloEcho>(new byte[16]));
    }

    [Fact]
    public void The_map_requests_have_the_captured_sizes_and_bytes()
    {
        Assert.Equal(Convert.FromHexString("01" + new string('0', 34)), Payload(new JoinHandshakeRequest()));
        Assert.Equal(30, Payload(new RefreshCharacterInfoRequest(new Serial(1, 2))).Length);
        Assert.Equal(new Serial(1, 2), RoundTrip(new RefreshCharacterInfoRequest(new Serial(1, 2))).Character);
        Assert.Equal(Convert.FromHexString("0001000000000000" + new string('0', 68)), Payload(new RenameRequest(RenameKind.Tamer, 0, "")));
        Assert.Equal(new RenameRequest(RenameKind.Digimon, 77, "Gabu"), RoundTrip(new RenameRequest(RenameKind.Digimon, 77, "Gabu")));
        Assert.Equal(new byte[14], Payload(new Unknown121Request()));
        Assert.Equal(57, Payload(new JoinCharacterRequest("a", "b")).Length);
        Assert.Equal(1, Payload(new JoinCharacterRequest("a", "b"))[42]);          // ✅ the capture has 1 after the two names
    }

    [Fact]
    public void The_channel_requests_before_the_map_match_the_capture()
    {
        var confirm = Payload(new MapConnectionConfirmRequest("debelzak26", "Debelzak"));
        Assert.Equal(55, confirm.Length);
        Assert.Equal("debelzak26", System.Text.Encoding.ASCII.GetString(confirm, 0, 10));

        var macs = new[] { "18-C0-4D-3B-04-C6", "52-54-00-30-C0-76", "02-8D-24-D0-4F-3D" };
        var hardware = Payload(new ClientHardwareInfoRequest(macs));
        Assert.Equal(74, hardware.Length);
        Assert.Equal(Convert.FromHexString("31382D43302D34442D33422D30342D4336000000"), hardware[..20]);
        Assert.Equal(macs, RoundTrip(new ClientHardwareInfoRequest(macs)).MacAddresses);
        Assert.Equal(new string('0', 28), Convert.ToHexString(hardware[60..]));
    }

    [Fact]
    public void Fewer_than_three_addresses_leave_the_fields_empty()
    {
        var read = RoundTrip(new ClientHardwareInfoRequest(["AA-BB-CC-DD-EE-FF"]));

        Assert.Equal(["AA-BB-CC-DD-EE-FF", "", ""], read.MacAddresses);
        Assert.All(RoundTrip(new ClientHardwareInfoRequest([])).MacAddresses, m => Assert.Equal("", m));
    }

    [Fact]
    public void The_hardware_packet_never_prints_the_addresses()
    {
        var text = new ClientHardwareInfoRequest(["18-C0-4D-3B-04-C6"]).ToString();

        Assert.DoesNotContain("18-C0", text);
    }

    [Fact]
    public void The_global_requests_match_the_capture()
    {
        var auth = Payload(new GlobalUserAuthRequest("debelzak26", "Debelzak", 44021));
        Assert.Equal(114, auth.Length);
        Assert.Equal(3u, BitConverter.ToUInt32(auth, 64));
        Assert.Equal(1u, BitConverter.ToUInt32(auth, 68));
        Assert.Equal(1u, BitConverter.ToUInt32(auth, 72));
        Assert.Equal(44021u, BitConverter.ToUInt32(auth, 96));
        Assert.Equal(new GlobalUserAuthRequest("debelzak26", "Debelzak", 44021), RoundTrip(new GlobalUserAuthRequest("debelzak26", "Debelzak", 44021)));

        var join = Payload(new GlobalJoinRequest("Debelzak", 26, 37));
        Assert.Equal(Convert.FromHexString("03000000446562656C7A616B000000000000000000000000000000001A000000250000000000000000000000000000000000"), join);
        Assert.Equal(new GlobalJoinRequest("Debelzak", 26, 37), RoundTrip(new GlobalJoinRequest("Debelzak", 26, 37)));
    }

    [Fact]
    public void The_global_auth_never_prints_the_user_in_a_wrong_size_message()
    {
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<GlobalUserAuthRequest>(new byte[113]));
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<GlobalJoinRequest>(new byte[51]));
    }
}
