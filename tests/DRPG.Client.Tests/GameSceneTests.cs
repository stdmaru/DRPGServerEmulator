using DRPG.Client.Net;
using DRPG.Client.Scenes;
using DRPG.Client.Startup;
using DRPG.Client.Text;
using DRPG.Engine.Platform;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Formats.Sprite;
using DRPG.Shared.Packets.Channel;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Tests;

/// <summary>
/// Scene 5 against fake map and global servers: it enters only with a connected world, loads the map when the <c>0x02</c> and <c>0x03</c>
/// are in, puts the characters on their cells, sends the entry packets once, runs its logic in fixed steps and treats a lost connection,
/// a missing map and a corrupt map as a lost world.
/// </summary>
public sealed class GameSceneTests : IDisposable
{
    private const string CharacterName = "Marcus";
    private const int MapId = 1;

    private sealed class FixedRandom(int value) : Random
    {
        public override int Next(int maxValue) => value % maxValue;
    }

    private readonly SceneFixture _fixture = new();
    private readonly FakeServer _map = new();
    private readonly FakeServer _global = new();
    private readonly List<SceneId> _switches = [];
    private WorldSession? _session;

    public GameSceneTests()
    {
        _fixture.Game.Scenes = new SceneManager(id =>
        {
            _switches.Add(id);
            return new Stub();
        });
    }

    private sealed class Stub : IScene
    {
        public int FrameIntervalMs => 0;
        public void Init() { }
        public void Handle(InputEvent input) { }
        public void Frame(DRPG.Engine.Rendering.IRenderer renderer, ulong nowMs) { }
        public void Dispose() { }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _fixture.Game.World?.Dispose();
        _map.Dispose();
        _global.Dispose();
        _fixture.Dispose();
    }

    private void UseData(int mapId = MapId, string? code = "R001")
    {
        var table = PackArchive.Open(_fixture.Game.Install.TablePath);
        var rows = new Dictionary<string, IReadOnlyList<string[]>>
        {
            ["map"] = new List<string[]> { new[] { $"{mapId}", "Test Plains", "0", "0", code ?? "" } },
        };
        _fixture.Game.Data = new GameData(_fixture.Resource, table, rows, new Dictionary<string, byte[]>(), new TextAttach(), new ProhibitedWords(),
            new Dictionary<string, SpriteFile>());
    }

    private static CharacterDataPacket Character(byte tamerId = 1, ushort species = 3, bool withLeader = true)
    {
        var packet = ClientPacket.Parse<CharacterDataPacket>(new byte[CharacterDataPacket.Length.Min])
            with { Uid = 7, Serial = new Serial(1, 2), Name = CharacterName, TamerId = tamerId, Level = 12, X = 5, Y = 6, ServerUnixTime = 1_800_000_000 };
        if (withLeader)
        {
            var party = packet.Party.ToArray();
            party[0] = DigimonStatus.Empty with { Uid = 9, SpeciesId = species, Name = "Agumon", Level = 5, Hp = 50, MaxHp = 100 };
            packet = packet with { Party = party };
        }
        return packet;
    }

    /// <summary>A world entry whose session is connected, joined and has the character and the bags: the scene may load the map.</summary>
    private WorldSession ReadySession(CharacterDataPacket? character = null, int mapId = MapId, bool firstEntry = true)
    {
        var settings = new ClientSettings(ClientSettings.Local.Login, ClientSettings.Local.Channel, _map.Address, _global.Address);
        _session = new WorldSession(settings, _fixture.Game.Command, _fixture.Log.Add);
        var slot = new CharacterListSlot(Uid: 7, TamerId: 1, Name: CharacterName, Level: 12, X: 5, Y: 6, LeaderSpeciesId: 3, LeaderLevel: 5,
            LeaderName: "Agumon", TotalBattles: 0, TotalWins: 0, Slot: 0, MapId: (byte)mapId, CheckOffset: 0);
        _fixture.Game.World = new WorldEntry(slot, 0) { Session = _session };
        _session.Start(CharacterName, firstEntry);
        Pump(() => _map.Received.Count >= 4);
        _map.Send(character ?? Character());
        _map.Send(ClientPacket.Parse<InventoryDataPacket>(new byte[InventoryDataPacket.Length.Min]));
        Pump(() => _session.Step == WorldStep.DataReady);
        return _session;
    }

    private void Pump(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline)
        {
            _session?.Update();
            Thread.Sleep(5);
        }
        Assert.True(done(), "the condition did not come in time");
    }

    private GameScene NewScene(int rand = 0)
    {
        var scene = new GameScene(_fixture.Game, new FixedRandom(rand));
        scene.Init();
        return scene;
    }

    [Fact]
    public void Without_a_connection_to_the_map_the_scene_goes_back_to_the_world_selection()
    {
        UseData();
        using var scene = new GameScene(_fixture.Game, new FixedRandom(0));

        scene.Init();
        _fixture.Game.Scenes.Frame(_fixture.Renderer, 0);

        Assert.Equal([SceneId.Server], _switches);
        scene.Frame(_fixture.Renderer, 10);                            // an aborted scene draws and sends nothing
        Assert.Empty(_fixture.Renderer.Draws);
    }

    [Fact]
    public void A_world_that_is_not_connected_yet_also_goes_back()
    {
        UseData();
        var settings = new ClientSettings(ClientSettings.Local.Login, ClientSettings.Local.Channel, _map.Address, _global.Address);
        _session = new WorldSession(settings, _fixture.Game.Command, _fixture.Log.Add);       // never started
        _fixture.Game.World = new WorldEntry(new CharacterListSlot(7, 1, CharacterName, 1, 0, 0, 3, 1, "A", 0, 0, 0, 1, 0), 0) { Session = _session };
        using var scene = new GameScene(_fixture.Game, new FixedRandom(0));

        scene.Init();
        _fixture.Game.Scenes.Frame(_fixture.Renderer, 0);

        Assert.Equal([SceneId.Server], _switches);
    }

    [Fact]
    public void The_map_is_loaded_when_the_world_data_is_in_and_the_entry_packets_go_out_in_order()
    {
        UseData();
        var session = ReadySession();
        using var scene = NewScene();

        scene.Frame(_fixture.Renderer, 1000);

        Assert.True(scene.WorldEntered);
        Assert.False(scene.ConnectionLost);
        Assert.Equal(WorldStep.InWorld, session.Step);
        Assert.True(_fixture.Game.HasEnteredWorld);
        Assert.Equal(20 * 64, scene.Field!.PixelWidth);
        Pump(() => _map.Received.Count >= 7);
        Assert.Equal([(ushort)MapOpcode.RefreshCharacterInfoReq, (ushort)MapOpcode.Rename, (ushort)MapOpcode.Unknown121], _map.Received.Skip(4).Take(3));
        Pump(() => _global.Received.Count >= 1);
    }

    [Fact]
    public void The_entry_packets_are_not_sent_while_the_data_is_missing()
    {
        UseData();
        var settings = new ClientSettings(ClientSettings.Local.Login, ClientSettings.Local.Channel, _map.Address, _global.Address);
        _session = new WorldSession(settings, _fixture.Game.Command, _fixture.Log.Add);
        _fixture.Game.World = new WorldEntry(new CharacterListSlot(7, 1, CharacterName, 1, 0, 0, 3, 1, "A", 0, 0, 0, MapId, 0), 0) { Session = _session };
        _session.Start(CharacterName, true);
        Pump(() => _map.Received.Count >= 4);
        _map.Send(Character());                                        // the bags are missing
        Pump(() => _session.Character is not null);
        using var scene = NewScene();

        scene.Frame(_fixture.Renderer, 1000);
        Thread.Sleep(100);

        Assert.False(scene.WorldEntered);
        Assert.Equal(4, _map.Received.Count);
        Assert.False(_fixture.Game.HasEnteredWorld);
    }

    [Fact]
    public void The_121_goes_only_on_the_first_entry_of_the_run()
    {
        UseData();
        ReadySession(firstEntry: false);
        using var scene = NewScene();

        scene.Frame(_fixture.Renderer, 1000);
        Pump(() => _map.Received.Count >= 6);
        Thread.Sleep(100);

        Assert.DoesNotContain((ushort)MapOpcode.Unknown121, _map.Received);
    }

    [Fact]
    public void The_tamer_and_the_digimon_stand_on_the_centre_of_their_cells()
    {
        UseData();
        ReadySession();
        using var scene = NewScene();

        scene.Frame(_fixture.Renderer, 1000);

        Assert.Equal((5 * 64 + 32, 6 * 32 + 16), scene.TamerWorld);
        Assert.Equal((4 * 64 + 32, 5 * 32 + 16), scene.DigimonWorld);          // one cell up and to the left
        Assert.NotNull(scene.TamerAnimation);
        Assert.NotNull(scene.DigimonAnimation);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void These_tamers_have_no_digimon_on_the_map(int tamerId)
    {
        UseData();
        ReadySession(Character((byte)tamerId));
        using var scene = NewScene();

        scene.Frame(_fixture.Renderer, 1000);

        Assert.True(scene.WorldEntered);
        Assert.Null(scene.DigimonAnimation);
    }

    [Fact]
    public void A_character_with_no_leader_has_no_digimon_on_the_map()
    {
        UseData();
        ReadySession(Character(withLeader: false));
        using var scene = NewScene();

        scene.Frame(_fixture.Renderer, 1000);

        Assert.True(scene.WorldEntered);
        Assert.Null(scene.DigimonAnimation);
        Assert.NotNull(scene.TamerAnimation);
    }

    [Fact]
    public void The_initial_phase_of_the_loop_is_a_random_number_below_8()
    {
        UseData();
        ReadySession();
        using var scene = NewScene(rand: 1234);

        scene.Frame(_fixture.Renderer, 1000);

        Assert.Equal(1234 % 8, scene.TamerAnimation!.Offset);
        Assert.Equal(1234 % 8, scene.DigimonAnimation!.Offset);
    }

    [Fact]
    public void The_logic_runs_in_fixed_steps_of_the_real_time()
    {
        UseData();
        ReadySession();
        using var scene = NewScene();
        scene.Frame(_fixture.Renderer, 1000);                           // the world loads here: the clock of the logic starts
        Assert.Equal(0, scene.DigimonAnimation!.IdleCounter);

        scene.Frame(_fixture.Renderer, 1000 + 500);                     // half a second: 30 steps
        Assert.Equal(30, scene.DigimonAnimation.IdleCounter);

        scene.Frame(_fixture.Renderer, 1000 + 500 + 8);                 // 8 ms are not a whole step (16.6 ms)
        Assert.Equal(30, scene.DigimonAnimation.IdleCounter);

        scene.Frame(_fixture.Renderer, 1000 + 1000);                    // a second since the start: 60 steps in all
        Assert.Equal(60, scene.DigimonAnimation.IdleCounter);
    }

    [Fact]
    public void A_stall_of_many_seconds_runs_at_most_the_cap_of_steps_in_one_frame()
    {
        UseData();
        ReadySession();
        using var scene = NewScene();
        scene.Frame(_fixture.Renderer, 0);

        // 1000 s = 60000 due steps: the frame runs 10000 of them and the next frames go on from the real time, not from a backlog
        scene.Frame(_fixture.Renderer, 1_000_000);
        scene.Frame(_fixture.Renderer, 1_000_000 + 1000);

        Assert.Equal(0, scene.Transition);                              // the fade (105 steps) is over, and stays over
        Assert.Equal((10_000 + 60) % 320, scene.DigimonAnimation!.IdleCounter);
        Assert.False(scene.ConnectionLost);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(95, 255)]
    [InlineData(99, 255)]
    [InlineData(100, 255)]
    [InlineData(150, 127)]
    [InlineData(199, 2)]
    [InlineData(200, 0)]
    [InlineData(5000, 0)]
    public void The_darkness_of_the_entry_goes_from_black_to_clear(int transition, int darkness) =>
        Assert.Equal(darkness, GameScene.DarknessOf(transition));

    [Fact]
    public void The_entry_starts_dark_and_ends_clear()
    {
        UseData();
        ReadySession();
        using var scene = NewScene();

        scene.Frame(_fixture.Renderer, 1000);
        Assert.Equal(GameScene.FadeStart, scene.Transition);
        Assert.Contains(255, _fixture.Renderer.Darkens);

        for (int step = 0; step < GameScene.FadeEnd; step++)
            scene.Step();
        Assert.Equal(0, scene.Transition);
    }

    [Fact]
    public void The_field_and_the_hud_are_drawn_every_frame_of_the_scene()
    {
        UseData();
        ReadySession();
        using var scene = NewScene();
        scene.Frame(_fixture.Renderer, 1000);
        _fixture.Renderer.Draws.Clear();

        scene.Frame(_fixture.Renderer, 1016);

        Assert.NotEmpty(_fixture.Renderer.Draws);
    }

    [Fact]
    public void A_map_the_table_does_not_know_is_a_lost_world_and_nothing_is_sent_to_the_server()
    {
        UseData(mapId: 99);                                            // the character's map (1) has no row
        ReadySession();
        using var scene = NewScene();

        scene.Frame(_fixture.Renderer, 1000);
        Thread.Sleep(100);

        Assert.True(scene.ConnectionLost);
        Assert.False(scene.WorldEntered);
        Assert.False(_fixture.Game.HasEnteredWorld);
        Assert.Equal(4, _map.Received.Count);                          // no 0x04, no 0xF0
        Assert.Contains(_fixture.Log, line => line.Contains("LoadMap Error"));
    }

    [Fact]
    public void A_map_without_a_code_or_missing_from_the_pack_is_a_lost_world()
    {
        UseData(code: "");
        ReadySession();
        using var scene = NewScene();
        scene.Frame(_fixture.Renderer, 1000);
        Assert.True(scene.ConnectionLost);
    }

    [Fact]
    public void A_corrupt_map_file_is_a_lost_world_not_a_crash()
    {
        UseData(code: "R002");                                        // map\r002.map is three bytes
        ReadySession();
        using var scene = NewScene();

        scene.Frame(_fixture.Renderer, 1000);

        Assert.True(scene.ConnectionLost);
        Assert.False(scene.WorldEntered);
        Assert.Contains(_fixture.Log, line => line.Contains("LoadMap Error"));
    }

    [Fact]
    public void A_lost_connection_shows_the_text_and_a_click_goes_back_to_the_world_selection()
    {
        UseData();
        ReadySession();
        using var scene = NewScene();
        scene.Frame(_fixture.Renderer, 1000);
        Pump(() => _map.Received.Count >= 7);

        _map.CloseClient();
        Pump(() => _session!.Step == WorldStep.Failed);
        scene.Frame(_fixture.Renderer, 1100);
        Assert.True(scene.ConnectionLost);
        Assert.Empty(_switches);                                       // not yet: the text waits for the click

        _fixture.Game.Mouse.Handle(new MouseButtonEvent(400, 300, true), 1200);
        scene.Frame(_fixture.Renderer, 1200);
        _fixture.Game.Scenes.Frame(_fixture.Renderer, 1200);

        Assert.Equal([SceneId.Server], _switches);
        Assert.Null(_fixture.Game.World);
    }

    [Fact]
    public void The_scene_logs_its_frame_statistics_when_it_ends()
    {
        UseData();
        ReadySession();
        var scene = NewScene();
        scene.Frame(_fixture.Renderer, 1000);
        scene.Frame(_fixture.Renderer, 1016);

        scene.Dispose();

        Assert.Contains(_fixture.Log, line => line.StartsWith("game scene:"));
    }
}
