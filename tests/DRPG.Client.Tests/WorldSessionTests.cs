using DRPG.Client.Net;
using DRPG.Shared.Packets.Channel;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Tests;

/// <summary>
/// <see cref="WorldSession"/> against fake map and global servers: the order of the packets of the official capture (session 52,
/// connections 88 and 89), the echo of the hello, the ping, the first-entry-only <c>0x121</c>, and a broken or hostile server.
/// </summary>
public sealed class WorldSessionTests : IDisposable
{
    private const string Username = "tamer";
    private const string CharacterName = "Marcus";

    private readonly FakeServer _map = new();
    private readonly FakeServer _global = new();
    private readonly List<string> _log = [];

    public void Dispose()
    {
        _map.Dispose();
        _global.Dispose();
    }

    private WorldSession NewSession() =>
        new(new ClientSettings(ClientSettings.Local.Login, ClientSettings.Local.Channel, _map.Address, _global.Address),
            new GameCommandLine(Username, "0123456789ABCDEF0123456789ABCDEF01234567"), _log.Add);

    private static CharacterDataPacket Character() =>
        ClientPacket.Parse<CharacterDataPacket>(new byte[CharacterDataPacket.Length.Min])
            with { Uid = 7, Serial = new Serial(1, 2), Name = CharacterName, X = 5, Y = 6 };

    private static InventoryDataPacket Inventory() => ClientPacket.Parse<InventoryDataPacket>(new byte[InventoryDataPacket.Length.Min]);

    private static void Pump(WorldSession session, Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline)
        {
            session.Update();
            Thread.Sleep(5);
        }
    }

    private static void Hello(FakeServer server) => server.Send(new SeedHelloPacket(Enumerable.Range(1, 16).Select(i => (byte)i).ToArray()));

    /// <summary>Connected and joined: the server said hello, then sent the character and the bags.</summary>
    private WorldSession JoinedSession(bool firstEntry = true)
    {
        var session = NewSession();
        session.Start(CharacterName, firstEntry);
        Pump(session, () => _map.Received.Count >= 4);
        Hello(_map);
        _map.Send(Character());
        _map.Send(Inventory());
        Pump(session, () => session.Step is WorldStep.DataReady or WorldStep.Failed && _map.Received.Count >= 5);
        return session;
    }

    [Fact]
    public void Joining_sends_the_handshake_three_times_then_the_character()
    {
        using var session = NewSession();
        session.Start(CharacterName, firstEntry: true);
        Pump(session, () => _map.Received.Count >= 4);

        Assert.Equal(WorldStep.Joining, session.Step);
        Assert.True(session.MapConnected);
        Assert.Equal([(ushort)MapOpcode.JoinHandshakeReq, 0x49, 0x49, (ushort)MapOpcode.JoinCharacter], _map.Received.Take(4));
    }

    [Fact]
    public void The_hello_is_echoed_and_its_seed_is_kept()
    {
        using var session = NewSession();
        session.Start(CharacterName, firstEntry: true);
        Pump(session, () => _map.Received.Count >= 4);
        Hello(_map);
        Pump(session, () => _map.Received.Count >= 5);

        Assert.Equal(0x0E, _map.Received[4]);
        Assert.Equal(Enumerable.Range(1, 16).Select(i => (byte)i), session.MapSeed);
    }

    [Fact]
    public void The_world_data_is_ready_after_the_character_and_the_bags()
    {
        using var session = NewSession();
        session.Start(CharacterName, firstEntry: true);
        Pump(session, () => _map.Received.Count >= 4);

        _map.Send(Character());
        Pump(session, () => session.Character is not null);
        Assert.Equal(WorldStep.Joining, session.Step);                     // the bags are still missing

        _map.Send(Inventory());
        Pump(session, () => session.Step == WorldStep.DataReady);
        Assert.Equal(CharacterName, session.Character!.Name);
        Assert.NotNull(session.Inventory);
    }

    [Fact]
    public void Entering_the_world_sends_0x04_0xF0_0x121_and_joins_the_global_server()
    {
        using var session = JoinedSession(firstEntry: true);
        Assert.Equal(WorldStep.DataReady, session.Step);

        session.EnterWorld();
        Pump(session, () => _global.Received.Count >= 4);
        Pump(session, () => _map.Received.Count >= 8);

        Assert.Equal(WorldStep.InWorld, session.Step);
        Assert.Equal([(ushort)MapOpcode.RefreshCharacterInfoReq, (ushort)MapOpcode.Rename, (ushort)MapOpcode.Unknown121], _map.Received.Skip(5).Take(3));
        Hello(_global);
        Pump(session, () => _global.Received.Count >= 4);
        Assert.True(session.GlobalConnected);
        Assert.Equal(WorldSession.GlobalJoinCount, _global.Received.Count(op => op == 0xDE));
        Assert.Equal(0x64, _global.Received[0]);
    }

    [Fact]
    public void The_0x121_goes_only_on_the_first_entry()
    {
        using var session = JoinedSession(firstEntry: false);

        session.EnterWorld();
        Pump(session, () => _map.Received.Count >= 7);
        Thread.Sleep(100);                                                  // time for a 0x121 to show up if it were sent

        Assert.DoesNotContain((ushort)MapOpcode.Unknown121, _map.Received);
        Assert.Equal(7, _map.Received.Count);
    }

    [Fact]
    public void The_map_ping_is_answered_at_once()
    {
        using var session = JoinedSession();
        int before = _map.Received.Count;

        _map.SendRaw(PingFrame());
        Pump(session, () => _map.Received.Count > before);

        Assert.Equal((ushort)MapOpcode.KeepAlive, _map.Received[before]);
    }

    [Fact]
    public void Packets_the_client_does_not_use_yet_are_counted_and_unknown_opcodes_logged_once()
    {
        using var session = JoinedSession();

        _map.SendRaw(Frame(0x74, new byte[60]));
        _map.SendRaw(Frame(0x7E, new byte[1940]));
        _map.SendRaw(Frame(0x7E, new byte[1940]));
        _map.SendRaw(Frame(0x7777, new byte[3]));
        _map.SendRaw(Frame(0x7777, new byte[3]));
        Pump(session, () => session.IgnoredCounts.GetValueOrDefault((ushort)0x7E) == 2 && session.UnknownOpcodes.Count == 1);

        Assert.Equal(WorldStep.DataReady, session.Step);
        Assert.Equal(1, session.IgnoredCounts[0x74]);
        Assert.Equal([(ushort)0x7777], session.UnknownOpcodes);
        Assert.Single(_log, line => line.Contains("0x7777"));
    }

    [Theory]
    [InlineData(0x74, 59)]
    [InlineData(0x74, 61)]
    [InlineData(0x7F, 0)]
    [InlineData(0x152, 7047)]
    [InlineData(0xC4, 1)]
    [InlineData(0x02, 10)]
    [InlineData(0x03, 4415)]
    public void A_packet_with_the_wrong_size_fails_the_session(int opcode, int size)
    {
        using var session = NewSession();
        session.Start(CharacterName, firstEntry: true);
        Pump(session, () => _map.Received.Count >= 4);

        _map.SendRaw(Frame((ushort)opcode, new byte[size]));
        Pump(session, () => session.Step == WorldStep.Failed);

        Assert.Equal(WorldStep.Failed, session.Step);
        Assert.False(session.MapConnected);
        Assert.NotNull(session.FailureMessage);
    }

    [Fact]
    public void A_map_server_that_closes_fails_the_session()
    {
        using var session = NewSession();
        session.Start(CharacterName, firstEntry: true);
        Pump(session, () => _map.Received.Count >= 4);

        _map.CloseClient();
        Pump(session, () => session.Step == WorldStep.Failed);

        Assert.Equal(WorldStep.Failed, session.Step);
    }

    [Fact]
    public void A_refused_connection_fails_the_session()
    {
        var port = _map.Address.Port;
        _map.Dispose();
        using var session = new WorldSession(
            new ClientSettings(ClientSettings.Local.Login, ClientSettings.Local.Channel, new ServerAddress("127.0.0.1", port), _global.Address),
            new GameCommandLine(Username, "k"), _log.Add);

        session.Start(CharacterName, firstEntry: true);
        Pump(session, () => session.Step == WorldStep.Failed);

        Assert.Equal(WorldStep.Failed, session.Step);
        Assert.False(session.MapConnected);
        Assert.StartsWith("Could not connect", session.FailureMessage);
    }

    [Fact]
    public void A_global_server_that_closes_fails_the_session()
    {
        using var session = JoinedSession();
        session.EnterWorld();
        Pump(session, () => _global.Received.Count >= 1);

        _global.CloseClient();
        Pump(session, () => session.Step == WorldStep.Failed);

        Assert.Equal(WorldStep.Failed, session.Step);
    }

    [Fact]
    public void Entering_the_world_before_the_data_is_in_is_refused()
    {
        using var session = NewSession();
        Assert.Throws<InvalidOperationException>(session.EnterWorld);

        session.Start(CharacterName, firstEntry: true);
        Pump(session, () => _map.Received.Count >= 4);
        Assert.Throws<InvalidOperationException>(session.EnterWorld);
    }

    [Fact]
    public void A_session_starts_only_once()
    {
        using var session = NewSession();
        session.Start(CharacterName, firstEntry: true);
        Assert.Throws<InvalidOperationException>(() => session.Start(CharacterName, firstEntry: true));
    }

    [Fact]
    public void Nothing_that_identifies_the_player_or_the_machine_is_logged()
    {
        using var session = JoinedSession();
        session.EnterWorld();
        Pump(session, () => _global.Received.Count >= 1);

        Assert.DoesNotContain(_log, line => line.Contains("0123456789ABCDEF") || line.Contains(Username));
    }

    private static byte[] Frame(ushort opcode, byte[] payload)
    {
        var frame = new byte[PacketHeader.Length + payload.Length];
        new PacketHeader(opcode, (uint)frame.Length, 0, 0).Write(frame);
        payload.CopyTo(frame, PacketHeader.Length);
        return frame;
    }

    private static byte[] PingFrame() => Frame((ushort)MapOpcode.KeepAlive, []);
}
