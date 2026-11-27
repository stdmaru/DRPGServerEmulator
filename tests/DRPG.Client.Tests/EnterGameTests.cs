using System.Text.RegularExpressions;
using DRPG.Client.Net;
using DRPG.Client.Scenes;
using DRPG.Client.Startup;
using DRPG.Client.Text;
using DRPG.Engine.Platform;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Formats.Sprite;
using DRPG.Shared.Packets.Channel;
using DRPG.Shared.Packets.Login;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Tests;

/// <summary>
/// "Game Start" of the character selection (✅ <c>0x00b48670</c>): the fade, then <c>0x16</c> and <c>0x18</c> on the channel, the map
/// connection and scene 5; a map server that cannot be reached shows <c>0xee6</c> and leaves the player in the selection.
/// </summary>
public sealed partial class EnterGameTests : IDisposable
{
    private const string Username = "tamer";
    private const string FailedText = "Cannot connect to the map server.";
    private const ushort MapConnectionConfirm = 0x16, HardwareInfo = 0x18;

    private SceneFixture _fixture = null!;                          // created by Open, with the addresses of the fake servers
    private readonly FakeServer _login = new();
    private readonly FakeServer _channel = new();
    private readonly FakeServer _map = new();
    private readonly FakeServer _global = new();
    private readonly List<SceneId> _switches = [];
    private AccountSession? _account;
    private CharacterSelectScene? _scene;
    private ulong _now;

    private sealed class Stub : IScene
    {
        public int FrameIntervalMs => 0;
        public void Init() { }
        public void Handle(InputEvent input) { }
        public void Frame(DRPG.Engine.Rendering.IRenderer renderer, ulong nowMs) { }
        public void Dispose() { }
    }

    private sealed class FixedRandom : Random
    {
        public override int Next(int maxValue) => 0;
    }

    public void Dispose()
    {
        _scene?.Dispose();
        _fixture?.Game.World?.Dispose();
        _account?.Dispose();
        _login.Dispose();
        _channel.Dispose();
        _map.Dispose();
        _global.Dispose();
        _fixture?.Dispose();
    }

    [GeneratedRegex("([0-9A-Fa-f]{2}[-:]){5}[0-9A-Fa-f]{2}")]
    private static partial Regex MacAddress();

    private static CharacterListReply List()
    {
        var slots = new CharacterListSlot[CharacterListReply.SlotCount];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = i == 0
                ? new CharacterListSlot(7, 1, "Marcus", 12, 5, 6, 3, 9, "Agumon", 4, 2, 0, 1, 321)
                : CharacterListSlot.Empty((byte)i, 100 + i);
        return new CharacterListReply(slots);
    }

    /// <summary>Scene 4 on a character list from fake login and channel servers, the map server at <paramref name="mapAddress"/>.</summary>
    private void Open(ServerAddress? mapAddress = null)
    {
        _login.OnFrame = (server, frame) =>
        {
            if (frame.Opcode == (ushort)LoginOpcode.Handshake)
                server.Send(new LoginHandshakeReply(Username, 3, 0x2710));
        };
        _channel.OnFrame = (server, frame) =>
        {
            switch ((ChannelOpcode)frame.Opcode)
            {
                case ChannelOpcode.ChannelSelect: server.Send(new ChannelSelectReply(2)); break;
                case ChannelOpcode.Authenticate: server.Send(new AuthenticateReply(ChannelError.None, Username)); break;
                case ChannelOpcode.CharacterList: server.Send(List()); break;
                case ChannelOpcode.UserCheck: server.Send(new UserCheckReply(Username)); break;
            }
        };
        var settings = new ClientSettings(_login.Address, _channel.Address, mapAddress ?? _map.Address, _global.Address);
        _fixture = new SceneFixture(settings);
        _fixture.Game.Scenes = new SceneManager(id =>
        {
            _switches.Add(id);
            return new Stub();
        });
        var game = _fixture.Game;
        var texts = new TextAttach();
        texts.AddTableRows([[$"{CharacterSelectScene.MapConnectFailedText}", FailedText]]);
        game.Data = new GameData(_fixture.Resource, PackArchive.Open(game.Install.TablePath), new Dictionary<string, IReadOnlyList<string[]>>(),
            new Dictionary<string, byte[]>(), texts, new ProhibitedWords(), new Dictionary<string, SpriteFile>());
        _account = new AccountSession(settings, game.Command, _fixture.Log.Add);
        game.Account = _account;
        // the world selection already ran: the real session follows the calls of scenes 2 and 4
        _account.StartLogin(_now);
        PumpAccount(() => _account.Step == AccountStep.LoggedIn);
        _account.SelectChannel(_now);
        PumpAccount(() => _account.Step == AccountStep.ChannelSelected);
        _account.Authenticate(_now);
        PumpAccount(() => _account.Step == AccountStep.Authenticated);

        _scene = new CharacterSelectScene(game, new FixedRandom());
        _scene.Init();
        RunFrames(() => _account.Characters is not null && _account.Step == AccountStep.Ready && _account.UserConfirmed);
    }

    private void PumpAccount(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < deadline)
        {
            _now += 33;
            _account!.Update(_now);
            Thread.Sleep(2);
        }
        Assert.True(done(), "the account did not get there");
    }

    /// <summary>Frames of 33 ms of fake time until <paramref name="done"/> (or five real seconds).</summary>
    private void RunFrames(Func<bool> done, int max = 2000)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        for (int i = 0; i < max && !done() && DateTime.UtcNow < deadline; i++)
        {
            Frame();
            Thread.Sleep(2);
        }
        Assert.True(done(), "the condition did not come in time");
    }

    private void Frame()
    {
        _now += 33;
        _scene!.Frame(_fixture.Renderer, _now);
        _fixture.Game.Mouse.EndFrame();
        _fixture.Game.Scenes.Frame(_fixture.Renderer, _now);
    }

    private void Click(int x, int y)
    {
        var mouse = _fixture.Game.Mouse;
        mouse.Handle(new MouseMoveEvent(x, y), _now);
        Frame();
        mouse.Handle(new MouseButtonEvent(x, y, true), _now);
        Frame();
        mouse.Handle(new MouseButtonEvent(x, y, false), _now);
        Frame();
    }

    private void SelectFirstSlot() => Click(100, 120);

    private void ClickGameStart() => Click(560, 565);

    [Fact]
    public void Game_start_without_a_selected_character_does_nothing()
    {
        Open();

        ClickGameStart();
        RunFrames(() => true);
        for (int i = 0; i < 60; i++)
            Frame();

        Assert.Null(_fixture.Game.World);
        Assert.Empty(_switches);
        Assert.DoesNotContain(_fixture.Log, line => line.StartsWith("Map Server"));
        Assert.DoesNotContain(MapConnectionConfirm, _channel.Received);
    }

    [Fact]
    public void Game_start_logs_the_map_server_keeps_the_character_and_fades_before_anything_is_sent()
    {
        Open();
        SelectFirstSlot();
        Assert.Equal(0, _scene!.SelectedSlot);
        int channelFrames = _channel.Received.Count;

        ClickGameStart();

        Assert.Contains($"Map Server : {_map.Address.Host} {_map.Address.Port}", _fixture.Log);
        Assert.NotNull(_fixture.Game.World);
        Assert.Equal("Marcus", _fixture.Game.World!.Slot.Name);
        Assert.Equal(0, _fixture.Game.World.SlotIndex);
        Assert.Equal(1, _fixture.Game.World.MapId);
        Assert.NotEmpty(_fixture.Renderer.Darkens);                      // the screen is going dark
        Assert.Equal(channelFrames, _channel.Received.Count);            // nothing goes out before the end of the fade
        Assert.Null(_fixture.Game.World.Session);
        Assert.Empty(_switches);
    }

    [Fact]
    public void At_the_end_of_the_fade_the_channel_gets_0x16_then_0x18_and_the_scene_switches_to_the_game()
    {
        Open();
        SelectFirstSlot();
        int before = _channel.Received.Count;

        ClickGameStart();
        RunFrames(() => _switches.Contains(SceneId.Game));

        Assert.Equal([MapConnectionConfirm, HardwareInfo], _channel.Received.Skip(before));
        var session = _fixture.Game.World!.Session!;
        Assert.True(session.MapConnected);
        Assert.Equal([SceneId.Game], _switches);
        Assert.Contains($"connecting to {_map.Address}", _fixture.Log);
        Assert.True(_fixture.Renderer.Darkens.Count > 0);
    }

    [Fact]
    public void The_hardware_addresses_never_reach_the_log()
    {
        Open();
        SelectFirstSlot();
        ClickGameStart();
        RunFrames(() => _switches.Contains(SceneId.Game));

        Assert.DoesNotContain(_fixture.Log, line => MacAddress().IsMatch(line));
        Assert.DoesNotContain(_fixture.Log, line => line.Contains("0123456789ABCDEF"));
    }

    [Fact]
    public void A_second_press_during_the_fade_does_not_start_another_entry()
    {
        Open();
        SelectFirstSlot();

        ClickGameStart();
        var world = _fixture.Game.World;
        ClickGameStart();
        ClickGameStart();

        Assert.Same(world, _fixture.Game.World);
        Assert.Single(_fixture.Log, line => line.StartsWith("Map Server"));
    }

    [Fact]
    public void A_map_server_that_cannot_be_reached_shows_the_text_and_leaves_the_player_in_the_selection()
    {
        var closed = new FakeServer();
        var closedAddress = closed.Address;
        closed.Dispose();                                                // nothing listens there any more
        Open(closedAddress);
        SelectFirstSlot();

        ClickGameStart();
        RunFrames(() => _scene!.ActiveMessage is not null);

        Assert.Equal(FailedText, _scene!.ActiveMessage);
        Assert.Null(_fixture.Game.World);                                // the entry is dropped
        Assert.Empty(_switches);
        Assert.Equal(0, _scene.SelectedSlot);                            // still selected: the player may try again
    }

    [Fact]
    public void The_failure_text_goes_away_after_80_frames_and_the_player_can_try_again()
    {
        var closed = new FakeServer();
        var closedAddress = closed.Address;
        closed.Dispose();
        Open(closedAddress);
        SelectFirstSlot();
        ClickGameStart();
        RunFrames(() => _scene!.ActiveMessage is not null);

        for (int frame = 0; frame < 79; frame++)
            Frame();
        Assert.NotNull(_scene!.ActiveMessage);
        Frame();
        Frame();
        Assert.Null(_scene.ActiveMessage);

        ClickGameStart();                                                // a new attempt starts: the fade again
        Assert.NotNull(_fixture.Game.World);
    }

    [Fact]
    public void A_failed_attempt_does_not_mark_the_world_as_entered()
    {
        var closed = new FakeServer();
        var closedAddress = closed.Address;
        closed.Dispose();
        Open(closedAddress);
        SelectFirstSlot();

        ClickGameStart();
        RunFrames(() => _scene!.ActiveMessage is not null);

        Assert.False(_fixture.Game.HasEnteredWorld);
    }
}
