using System.Buffers;
using System.Net;
using System.Net.Sockets;
using DRPG.Client.Net;
using DRPG.Shared.Packets.Channel;
using DRPG.Shared.Packets.Login;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Tests;

/// <summary>
/// A scripted game server on a loopback port: accepts connections, records the frames it receives (opcode, in order)
/// and lets the test answer with typed packets, raw bytes or by closing.
/// </summary>
internal sealed class FakeServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly List<ushort> _received = [];
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private NetworkStream? _stream;

    public FakeServer()
    {
        _listener.Start();
        Accepting = Task.Run(AcceptLoop);
    }

    public ServerAddress Address => new("127.0.0.1", ((IPEndPoint)_listener.LocalEndpoint).Port);

    public Task Accepting { get; }

    /// <summary>Called for every received frame; answer from here.</summary>
    public Action<FakeServer, IncomingFrame>? OnFrame { get; set; }

    public IReadOnlyList<ushort> Received
    {
        get
        {
            lock (_gate)
                return [.. _received];
        }
    }

    public void Send<T>(T packet) where T : ITwoWayPacket<T> => SendRaw(TwoWayPacket.Encode(packet));

    public void SendRaw(byte[] bytes)
    {
        lock (_gate)
            _stream?.Write(bytes);
    }

    public void CloseClient()
    {
        lock (_gate)
            _stream?.Dispose();
    }

    private async Task AcceptLoop()
    {
        try
        {
            using var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
            lock (_gate)
                _stream = socket.GetStream();
            var stream = _stream!;
            var buffer = new byte[PacketHeader.MaxPacketSize * 2];
            int buffered = 0;
            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(buffered), _stop.Token);
                if (read == 0)
                    return;
                buffered += read;
                while (true)
                {
                    var sequence = new ReadOnlySequence<byte>(buffer, 0, buffered);
                    if (PacketFramer.TryRead(ref sequence, out var frame, out _) != FrameStatus.Complete)
                        break;
                    int used = buffered - (int)sequence.Length;
                    Buffer.BlockCopy(buffer, used, buffer, 0, buffered - used);
                    buffered -= used;
                    lock (_gate)
                        _received.Add(frame!.Opcode);
                    OnFrame?.Invoke(this, frame!);
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException or SocketException)
        {
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        lock (_gate)
            _stream?.Dispose();
    }
}

/// <summary>
/// <see cref="AccountSession"/> against fake login and channel servers: the order of the packets of the official
/// captures, driven step by step like the scenes do, the refusals, a broken server and the keep-alive.
/// </summary>
public sealed class AccountSessionTests : IDisposable
{
    private const string Username = "tamer";
    private const string Key = "0123456789ABCDEF0123456789ABCDEF01234567";

    private readonly FakeServer _login = new();
    private readonly FakeServer _channel = new();
    private readonly List<string> _log = [];
    private ulong _now;

    public void Dispose()
    {
        _login.Dispose();
        _channel.Dispose();
    }

    private AccountSession NewSession() =>
        new(new ClientSettings(_login.Address, _channel.Address, ClientSettings.Local.Map, ClientSettings.Local.Global), new GameCommandLine(Username, Key), _log.Add);

    private static CharacterListReply List(params CharacterListSlot[] used)
    {
        var slots = new CharacterListSlot[CharacterListReply.SlotCount];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = i < used.Length ? used[i] : CharacterListSlot.Empty((byte)i, 100 + i);
        return new CharacterListReply(slots);
    }

    private static CharacterListSlot Tamer(byte slot) =>
        new(Uid: 7, TamerId: 1, Name: "Marcus", Level: 12, X: 5, Y: 6, LeaderSpeciesId: 3, LeaderLevel: 9, LeaderName: "Agumon",
            TotalBattles: 4, TotalWins: 2, Slot: slot, MapId: 0x4F, CheckOffset: 321);

    /// <summary>Answers like the official servers: ✅ the 0x05, 0x03 and 0x04 replies, then the list and the confirmation.</summary>
    private void ScriptHappyServers(CharacterListReply list, int loginMarker = 0x2710)
    {
        _login.OnFrame = (server, frame) =>
        {
            if (frame.Opcode == (ushort)LoginOpcode.Handshake)
                server.Send(new LoginHandshakeReply(Username, 3, loginMarker));
        };
        _channel.OnFrame = (server, frame) =>
        {
            switch ((ChannelOpcode)frame.Opcode)
            {
                case ChannelOpcode.ChannelSelect:
                    server.Send(new ChannelSelectReply(2));
                    break;
                case ChannelOpcode.Authenticate:
                    server.Send(new AuthenticateReply(ChannelError.None, Username));
                    break;
                case ChannelOpcode.CharacterList:
                    server.Send(list);
                    break;
                case ChannelOpcode.UserCheck:
                    server.Send(new UserCheckReply(Username));
                    break;
                case ChannelOpcode.KeepAlive:
                    server.Send(new KeepAliveReply());
                    break;
            }
        };
    }

    /// <summary>Runs frames of 33 ms of fake time until <paramref name="done"/> or two real seconds.</summary>
    private void Pump(AccountSession session, Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!done() && DateTime.UtcNow < deadline)
        {
            _now += 33;
            session.Update(_now);
            Thread.Sleep(2);
        }
    }

    private void ToStep(AccountSession session, AccountStep step) =>
        Pump(session, () => session.Step == step || session.Step == AccountStep.Failed);

    /// <summary>The calls the scenes make, in order, until the list is in.</summary>
    private void ReachTheList(AccountSession session)
    {
        session.StartLogin(_now);
        ToStep(session, AccountStep.LoggedIn);
        session.SelectChannel(_now);
        ToStep(session, AccountStep.ChannelSelected);
        session.Authenticate(_now);
        ToStep(session, AccountStep.Authenticated);
        session.RequestCharacterList(_now);
        Pump(session, () => session.Step == AccountStep.Ready && session.UserConfirmed);
    }

    [Fact]
    public void Reaches_the_character_list_in_the_order_of_the_captures()
    {
        ScriptHappyServers(List(Tamer(0)));
        using var session = NewSession();

        ReachTheList(session);

        Assert.Equal(AccountStep.Ready, session.Step);
        Assert.Null(session.FailureMessage);
        Assert.True(session.UserConfirmed);
        Assert.Equal(2, session.FourthStarter);
        Assert.Equal([(ushort)LoginOpcode.Handshake], _login.Received);
        // ✅ 0x03, 0x04, then 0x12 and 0x14 together
        Assert.Equal(
            [(ushort)ChannelOpcode.ChannelSelect, (ushort)ChannelOpcode.Authenticate, (ushort)ChannelOpcode.CharacterList, (ushort)ChannelOpcode.UserCheck],
            _channel.Received);
        var slots = session.Characters!.Slots;
        Assert.Equal(CharacterListReply.SlotCount, slots.Count);
        Assert.Equal("Marcus", slots[0].Name);
        Assert.True(slots[1].IsEmpty);
        Assert.Contains(_log, line => line.Contains("1 of 4 slots in use"));
    }

    [Fact]
    public void Nothing_is_sent_until_the_scenes_ask_for_it()
    {
        ScriptHappyServers(List());
        using var session = NewSession();

        session.StartLogin(_now);
        ToStep(session, AccountStep.LoggedIn);
        _now += 5_000;
        session.Update(_now);
        Thread.Sleep(100);

        // ✅ 0x05 on entering the world selection, and the channel is not touched before the click
        Assert.Equal(AccountStep.LoggedIn, session.Step);
        Assert.Equal([(ushort)LoginOpcode.Handshake], _login.Received);
        Assert.Empty(_channel.Received);
    }

    [Theory]
    [InlineData(0x2710, true)]
    [InlineData(0x68, false)]
    public void The_login_reply_says_whether_battle_zone_is_open(int marker, bool enabled)
    {
        ScriptHappyServers(List(), marker);
        using var session = NewSession();

        session.StartLogin(_now);
        ToStep(session, AccountStep.LoggedIn);

        Assert.Equal(enabled, session.BattleZoneEnabled);
    }

    [Fact]
    public void The_steps_refuse_to_run_out_of_order()
    {
        ScriptHappyServers(List());
        using var session = NewSession();

        Assert.Throws<InvalidOperationException>(() => session.SelectChannel(_now));
        Assert.Throws<InvalidOperationException>(() => session.Authenticate(_now));
        Assert.Throws<InvalidOperationException>(() => session.RequestCharacterList(_now));
        session.StartLogin(_now);
        Assert.Throws<InvalidOperationException>(() => session.StartLogin(_now));
    }

    [Fact]
    public void Sends_the_user_and_the_key_of_the_command_line_and_never_logs_the_key()
    {
        AuthenticateRequest? seen = null;
        ScriptHappyServers(List());
        var happy = _channel.OnFrame!;
        _channel.OnFrame = (server, frame) =>
        {
            if (frame.Opcode == (ushort)ChannelOpcode.Authenticate)
                seen = ClientPacket.Parse<AuthenticateRequest>(frame.Payload);
            happy(server, frame);
        };
        using var session = NewSession();

        ReachTheList(session);

        Assert.Equal(new AuthenticateRequest(Username, Key), seen);
        Assert.DoesNotContain(_log, line => line.Contains(Key));
    }

    [Fact]
    public void A_refused_key_goes_back_to_the_channels_with_the_code_and_does_not_ask_for_the_list()
    {
        ScriptHappyServers(List());
        var happy = _channel.OnFrame!;
        _channel.OnFrame = (server, frame) =>
        {
            if (frame.Opcode == (ushort)ChannelOpcode.Authenticate)
                server.Send(new AuthenticateReply(ChannelError.InvalidCredentials, Username, Key));
            else
                happy(server, frame);
        };
        using var session = NewSession();
        session.StartLogin(_now);
        ToStep(session, AccountStep.LoggedIn);
        session.SelectChannel(_now);
        ToStep(session, AccountStep.ChannelSelected);

        session.Authenticate(_now);
        Pump(session, () => session.AuthenticationRefusal is not null);

        Assert.Equal(ChannelError.InvalidCredentials, session.AuthenticationRefusal);
        Assert.Equal(AccountStep.LoggedIn, session.Step);
        Assert.Null(session.FailureMessage);
        Assert.DoesNotContain(_log, line => line.Contains(Key));
        Assert.DoesNotContain((ushort)ChannelOpcode.CharacterList, _channel.Received);
    }

    [Fact]
    public void Reset_drops_everything_and_the_login_can_start_again()
    {
        ScriptHappyServers(List(Tamer(0)));
        using var session = NewSession();
        ReachTheList(session);

        session.Reset();

        Assert.Equal(AccountStep.NotStarted, session.Step);
        Assert.Null(session.Characters);
        Assert.False(session.UserConfirmed);
        session.StartLogin(_now);
        Assert.Equal(AccountStep.ConnectingLogin, session.Step);
    }

    [Fact]
    public void An_unknown_opcode_from_the_login_server_fails()
    {
        _login.OnFrame = (server, _) => server.SendRaw(new PacketWriter().ToFrame(0x7777));
        using var session = NewSession();

        session.StartLogin(_now);
        ToStep(session, AccountStep.Failed);

        Assert.Equal(AccountStep.Failed, session.Step);
        Assert.Contains("Invalid packet", session.FailureMessage);
    }

    [Fact]
    public void A_character_list_with_a_forged_check_fails()
    {
        ScriptHappyServers(List());
        var happy = _channel.OnFrame!;
        _channel.OnFrame = (server, frame) =>
        {
            if (frame.Opcode != (ushort)ChannelOpcode.CharacterList)
            {
                happy(server, frame);
                return;
            }
            var payload = TwoWayPacket.EncodePayload(List(Tamer(0)));
            payload[^4] ^= 0x55;                    // the offset of the last slot no longer matches its check
            var writer = new PacketWriter();
            foreach (var b in payload)
                writer.WriteU8(b);
            server.SendRaw(writer.ToFrame((ushort)ChannelOpcode.CharacterList));
        };
        using var session = NewSession();

        ReachTheList(session);

        Assert.Equal(AccountStep.Failed, session.Step);
        Assert.Null(session.Characters);
    }

    [Fact]
    public void A_packet_out_of_order_fails()
    {
        _login.OnFrame = (server, _) => server.Send(new KeepAliveReply());
        using var session = NewSession();

        session.StartLogin(_now);
        ToStep(session, AccountStep.Failed);

        Assert.Equal(AccountStep.Failed, session.Step);
    }

    [Fact]
    public void The_login_server_closing_the_connection_fails()
    {
        _login.OnFrame = (server, _) => server.CloseClient();
        using var session = NewSession();

        session.StartLogin(_now);
        ToStep(session, AccountStep.Failed);

        Assert.Equal(AccountStep.Failed, session.Step);
        Assert.NotNull(session.FailureMessage);
    }

    [Fact]
    public void Create_and_delete_send_requests_update_the_list_and_never_log_the_md5()
    {
        var initial = List();
        var afterCreate = List(Tamer(0));
        var afterDelete = List();
        CharacterCreateRequest? seenCreate = null;
        CharacterDeleteRequest? seenDelete = null;
        ScriptHappyServers(initial);
        var happy = _channel.OnFrame!;
        _channel.OnFrame = (server, frame) =>
        {
            switch ((ChannelOpcode)frame.Opcode)
            {
                case ChannelOpcode.CharacterCreate:
                    seenCreate = ClientPacket.Parse<CharacterCreateRequest>(frame.Payload);
                    server.Send(new CharacterCreateReply(ChannelError.None, seenCreate.Header, seenCreate.TamerId, seenCreate.Name, seenCreate.DigimonId, seenCreate.DigimonName));
                    server.Send(afterCreate);
                    break;
                case ChannelOpcode.CharacterDelete:
                    seenDelete = ClientPacket.Parse<CharacterDeleteRequest>(frame.Payload);
                    server.Send(new CharacterDeleteReply(ChannelError.None, seenDelete.CharacterUid, seenDelete.PasswordMd5));
                    server.Send(afterDelete);
                    break;
                default:
                    happy(server, frame);
                    break;
            }
        };

        using var session = NewSession();
        ReachTheList(session);
        Assert.Equal(1, session.CharacterListVersion);

        var header = new CharacterCreateHeader(0, 0x4F, 61, 56);
        session.CreateCharacter(new CharacterCreateRequest(header, 1, "Marcus", 10, "Agumon"));
        Pump(session, () => session.CharacterListVersion == 2);

        Assert.Equal(2, session.CharacterListVersion);
        Assert.Equal("Marcus", session.Characters!.Slots[0].Name);
        Assert.Null(session.TakeCreateError());

        const string md5 = "d41d8cd98f00b204e9800998ecf8427e";
        session.DeleteCharacter(7, md5);
        Pump(session, () => session.CharacterListVersion == 3);

        Assert.Equal(3, session.CharacterListVersion);
        Assert.True(session.Characters!.Slots[0].IsEmpty);
        Assert.Equal(md5, seenDelete!.PasswordMd5);
        Assert.DoesNotContain(_log, line => line.Contains(md5, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_and_delete_error_replies_expose_the_error_code()
    {
        ScriptHappyServers(List(Tamer(0)));
        var happy = _channel.OnFrame!;
        _channel.OnFrame = (server, frame) =>
        {
            switch ((ChannelOpcode)frame.Opcode)
            {
                case ChannelOpcode.CharacterCreate:
                    var create = ClientPacket.Parse<CharacterCreateRequest>(frame.Payload);
                    server.Send(new CharacterCreateReply(ChannelError.NameTaken, create.Header, create.TamerId, create.Name, create.DigimonId, create.DigimonName));
                    break;
                case ChannelOpcode.CharacterDelete:
                    var delete = ClientPacket.Parse<CharacterDeleteRequest>(frame.Payload);
                    server.Send(new CharacterDeleteReply(2000, delete.CharacterUid, delete.PasswordMd5));
                    break;
                default:
                    happy(server, frame);
                    break;
            }
        };

        using var session = NewSession();
        ReachTheList(session);

        session.CreateCharacter(new CharacterCreateRequest(new CharacterCreateHeader(1, 0x4F, 60, 56), 1, "Marcus", 10, "Gigi"));
        Pump(session, () => session.LastCreateError is not null);
        Assert.Equal(ChannelError.NameTaken, session.TakeCreateError());
        Assert.Null(session.TakeCreateError());

        session.DeleteCharacter(7, "d41d8cd98f00b204e9800998ecf8427e");
        Pump(session, () => session.LastDeleteError is not null);
        Assert.Equal(2000, session.TakeDeleteError());
        Assert.Null(session.TakeDeleteError());
    }

    [Fact]
    public void A_server_that_never_answers_times_out()
    {
        _login.OnFrame = (_, _) => { };
        using var session = NewSession();

        session.StartLogin(_now);
        Pump(session, () => session.Step == AccountStep.WaitingLoginReply && _login.Received.Count == 1);
        _now += AccountSession.StepTimeoutMs + 1;
        session.Update(_now);

        Assert.Equal(AccountStep.Failed, session.Step);
    }

    [Fact]
    public void Waiting_for_a_scene_is_not_a_timeout()
    {
        ScriptHappyServers(List());
        using var session = NewSession();
        session.StartLogin(_now);
        ToStep(session, AccountStep.LoggedIn);

        _now += AccountSession.StepTimeoutMs * 10;
        session.Update(_now);

        Assert.Equal(AccountStep.LoggedIn, session.Step);
    }

    [Fact]
    public void Nothing_is_listening_fails_to_connect()
    {
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var address = new ServerAddress("127.0.0.1", ((IPEndPoint)closed.LocalEndpoint).Port);
        closed.Stop();
        using var session = new AccountSession(new ClientSettings(address, address, address, address), new GameCommandLine(Username, Key), _log.Add);

        session.StartLogin(_now);
        ToStep(session, AccountStep.Failed);

        Assert.Equal(AccountStep.Failed, session.Step);
        Assert.Contains("Could not connect", session.FailureMessage);
    }

    [Fact]
    public void Keeps_the_channel_alive_after_the_list()
    {
        ScriptHappyServers(List());
        using var session = NewSession();
        ReachTheList(session);

        _now += AccountSession.KeepAliveIntervalMs - 1;
        session.Update(_now);
        Assert.DoesNotContain((ushort)ChannelOpcode.KeepAlive, _channel.Received);

        _now += 1;
        session.Update(_now);
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!_channel.Received.Contains((ushort)ChannelOpcode.KeepAlive) && DateTime.UtcNow < deadline)
            Thread.Sleep(2);

        Assert.Contains((ushort)ChannelOpcode.KeepAlive, _channel.Received);
        // the reply is read without complaint and the session stays up
        var until = DateTime.UtcNow.AddMilliseconds(150);
        Pump(session, () => DateTime.UtcNow > until || session.Step != AccountStep.Ready);
        Assert.Equal(AccountStep.Ready, session.Step);
    }
}
