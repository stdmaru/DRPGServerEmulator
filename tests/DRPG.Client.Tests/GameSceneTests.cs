using DRPG.Client.Net;
using DRPG.Client.Scenes;
using DRPG.Client.Startup;
using DRPG.Client.Text;
using DRPG.Client.World;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;
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

    /// <summary>A world entry whose session is connected to the map (the <c>0x49</c> ×3 are in): the scene may start.</summary>
    private WorldSession ConnectedSession(int mapId = MapId, bool firstEntry = true)
    {
        var settings = new ClientSettings(ClientSettings.Local.Login, ClientSettings.Local.Channel, _map.Address, _global.Address);
        _session = new WorldSession(settings, _fixture.Game.Command, _fixture.Log.Add);
        var slot = new CharacterListSlot(Uid: 7, TamerId: 1, Name: CharacterName, Level: 12, X: 5, Y: 6, LeaderSpeciesId: 3, LeaderLevel: 5,
            LeaderName: "Agumon", TotalBattles: 0, TotalWins: 0, Slot: 0, MapId: (byte)mapId, CheckOffset: 0);
        _fixture.Game.World = new WorldEntry(slot, 0) { Session = _session };
        _session.Start(CharacterName, firstEntry);
        Pump(() => _session.Step == WorldStep.Connected && _map.Received.Count >= 3);
        return _session;
    }

    /// <summary>Frames of the loading screen, one every <see cref="WorldLoading.FrameMs"/>, from <paramref name="t"/> while <paramref name="go"/>.</summary>
    private ulong FramesWhile(GameScene scene, ulong t, Func<bool> go)
    {
        for (int guard = 0; go() && guard < 10_000; guard++, t += WorldLoading.FrameMs)
            scene.Frame(_fixture.Renderer, t);
        return t;
    }

    /// <summary>
    /// The whole loading screen: the first run until the <c>0x01</c>, the fake server's character and bags, the second run until the field
    /// (or a lost world). Returns the time of the frame that ended it.
    /// </summary>
    private ulong RunLoading(GameScene scene, CharacterDataPacket? character = null)
    {
        ulong t = FramesWhile(scene, 0, () => scene.Loading.Phase == LoadingPhase.PreMap);
        Pump(() => _map.Received.Count >= 4);
        _map.Send(character ?? Character());
        _map.Send(ClientPacket.Parse<InventoryDataPacket>(new byte[InventoryDataPacket.Length.Min]));
        Pump(() => _session!.Step == WorldStep.DataReady);
        t = FramesWhile(scene, t, () => scene.Loading.Showing && !scene.ConnectionLost);
        return t - WorldLoading.FrameMs;
    }

    /// <summary>Connected, scene up, loading screen run: the world is entered at the returned time.</summary>
    private (GameScene Scene, ulong Entered) Enter(CharacterDataPacket? character = null, int mapId = MapId, bool firstEntry = true, int rand = 0)
    {
        ConnectedSession(mapId, firstEntry);
        var scene = NewScene(rand);
        return (scene, RunLoading(scene, character));
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
    public void The_loading_screen_shows_before_the_field_and_hides_the_cursor()
    {
        UseData();
        ConnectedSession();
        using var scene = NewScene();

        scene.Frame(_fixture.Renderer, 0);

        Assert.True(scene.Loading.Showing);
        Assert.False(scene.ShowCursor);
        Assert.Contains(_fixture.Renderer.Draws, d => (d.X, d.Y, d.Texture.Width) == (0, 0, 800));       // loading_image
        Assert.Contains(_fixture.Renderer.Draws, d => (d.X, d.Y, d.Texture.Width) == (0, 550, 800));     // loading_under_line
        Assert.Contains(_fixture.Renderer.Draws, d => (d.X, d.Y) == (740, 540));                         // game_end_gauge
        Assert.False(scene.WorldEntered);
    }

    [Fact]
    public void The_0x01_goes_out_after_16_frames_and_one_more_second()
    {
        UseData();
        var session = ConnectedSession();
        using var scene = NewScene();

        // ✅ frames every 90 ms; the 16th (t = 1350) starts the 1 s timer at its end (1430); the first loop at or past 2430 joins
        FramesWhile(scene, 0, () => scene.Loading.FramesDrawn < 27);
        Assert.Equal(WorldStep.Connected, session.Step);
        Assert.Equal(LoadingPhase.PreMap, scene.Loading.Phase);
        Assert.Equal(0, scene.Loading.Tip);

        scene.Frame(_fixture.Renderer, 2430);
        Assert.Equal(WorldStep.Joining, session.Step);
        Assert.Equal(LoadingPhase.WaitingForData, scene.Loading.Phase);
        Pump(() => _map.Received.Count >= 4);
        Assert.Equal((ushort)MapOpcode.JoinCharacter, _map.Received[3]);
    }

    [Fact]
    public void Frames_that_come_faster_than_the_loading_loop_do_not_speed_it_up()
    {
        UseData();
        ConnectedSession();
        using var scene = NewScene();

        for (ulong t = 0; t < 900; t += 16)                             // a 60 FPS display: 57 frames in 0.9 s
            scene.Frame(_fixture.Renderer, t);

        Assert.Equal(10, scene.Loading.FramesDrawn);                   // one loading frame per 90 ms
        Assert.Contains(_fixture.Renderer.Draws, d => (d.X, d.Y) == (740, 540));   // the screen is drawn on every display frame
    }

    [Fact]
    public void The_map_loading_lasts_at_least_16_frames_and_the_entry_packets_wait_for_its_end()
    {
        UseData();
        var session = ConnectedSession();
        using var scene = NewScene(rand: 7);
        ulong t = FramesWhile(scene, 0, () => scene.Loading.Phase == LoadingPhase.PreMap);
        Pump(() => _map.Received.Count >= 4);
        _map.Send(Character());
        _map.Send(ClientPacket.Parse<InventoryDataPacket>(new byte[InventoryDataPacket.Length.Min]));
        Pump(() => session.Step == WorldStep.DataReady);
        int before = scene.Loading.FramesDrawn;

        scene.Frame(_fixture.Renderer, t);                               // the map loads at once, the screen stays
        Assert.Equal(LoadingPhase.Map, scene.Loading.Phase);
        Assert.Equal(0, scene.Loading.Tip);                               // ✅ +0xf803: the frame after the 0x01 shows tip 0
        t += WorldLoading.FrameMs + WorldLoading.JoinedFrameExtraMs;
        scene.Frame(_fixture.Renderer, t);
        Assert.Equal(7, scene.Loading.Tip);                               // then rand() & 15

        FramesWhile(scene, t + WorldLoading.FrameMs, () => scene.Loading.Showing);
        Assert.Equal(WorldLoading.MinimumFrames, scene.Loading.FramesDrawn - before);
        Assert.True(scene.WorldEntered);
        Assert.True(scene.ShowCursor);
        Assert.Equal(WorldStep.InWorld, session.Step);
        Pump(() => _map.Received.Count >= 7);
        Assert.Equal([(ushort)MapOpcode.RefreshCharacterInfoReq, (ushort)MapOpcode.Rename, (ushort)MapOpcode.Unknown121], _map.Received.Skip(4).Take(3));
    }

    [Fact]
    public void A_world_sent_before_the_0x01_is_kept_and_used_after_the_join()
    {
        UseData();
        var session = ConnectedSession();
        _map.Send(Character());                                           // a server that does not wait for the 0x01
        _map.Send(ClientPacket.Parse<InventoryDataPacket>(new byte[InventoryDataPacket.Length.Min]));
        Pump(() => session.Inventory is not null);
        Assert.Equal(WorldStep.Connected, session.Step);
        using var scene = NewScene();

        ulong t = FramesWhile(scene, 0, () => scene.Loading.Phase == LoadingPhase.PreMap);
        Assert.Equal(WorldStep.DataReady, session.Step);
        FramesWhile(scene, t, () => scene.Loading.Showing);
        Assert.True(scene.WorldEntered);
    }

    [Fact]
    public void The_map_is_loaded_when_the_world_data_is_in_and_the_entry_packets_go_out_in_order()
    {
        UseData();
        var (scene, _) = Enter();
        using var owned = scene;

        Assert.True(scene.WorldEntered);
        Assert.False(scene.ConnectionLost);
        Assert.Equal(WorldStep.InWorld, _session!.Step);
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
        ConnectedSession();
        using var scene = NewScene();
        ulong t = FramesWhile(scene, 0, () => scene.Loading.Phase == LoadingPhase.PreMap);
        Pump(() => _map.Received.Count >= 4);
        _map.Send(Character());                                        // the bags are missing
        Pump(() => _session!.Character is not null);

        int frames = 0;
        FramesWhile(scene, t, () => frames++ < 40);                    // 3.6 s of frames with no bags
        Thread.Sleep(100);

        Assert.False(scene.WorldEntered);
        Assert.Equal(LoadingPhase.WaitingForData, scene.Loading.Phase);
        Assert.Equal(4, _map.Received.Count);
        Assert.False(_fixture.Game.HasEnteredWorld);
    }

    [Fact]
    public void The_121_goes_only_on_the_first_entry_of_the_run()
    {
        UseData();
        var (scene, _) = Enter(firstEntry: false);
        using var owned = scene;

        Pump(() => _map.Received.Count >= 6);
        Thread.Sleep(100);

        Assert.DoesNotContain((ushort)MapOpcode.Unknown121, _map.Received);
    }

    [Fact]
    public void The_tamer_and_the_digimon_stand_on_the_centre_of_their_cells()
    {
        UseData();
        var (scene, _) = Enter();
        using var owned = scene;

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
        var (scene, _) = Enter(Character((byte)tamerId));
        using var owned = scene;

        Assert.True(scene.WorldEntered);
        Assert.Null(scene.DigimonAnimation);
    }

    [Fact]
    public void A_character_with_no_leader_has_no_digimon_on_the_map()
    {
        UseData();
        var (scene, _) = Enter(Character(withLeader: false));
        using var owned = scene;

        Assert.True(scene.WorldEntered);
        Assert.Null(scene.DigimonAnimation);
        Assert.NotNull(scene.TamerAnimation);
    }

    [Fact]
    public void The_initial_phase_of_the_loop_is_a_random_number_below_8()
    {
        UseData();
        var (scene, _) = Enter(rand: 1234);
        using var owned = scene;

        Assert.Equal(1234 % 8, scene.TamerAnimation!.Offset);
        Assert.Equal(1234 % 8, scene.DigimonAnimation!.Offset);
    }

    [Fact]
    public void The_logic_runs_in_fixed_steps_of_the_real_time()
    {
        UseData();
        var (scene, entered) = Enter();                                 // the clock of the logic starts when the loading screen ends
        using var owned = scene;
        Assert.Equal(0, scene.DigimonAnimation!.IdleCounter);

        scene.Frame(_fixture.Renderer, entered + 500);                  // half a second: 15 steps (✅ 30 a second)
        Assert.Equal(15, scene.DigimonAnimation.IdleCounter);

        scene.Frame(_fixture.Renderer, entered + 500 + 20);             // 20 ms are not a whole step (33.3 ms)
        Assert.Equal(15, scene.DigimonAnimation.IdleCounter);

        scene.Frame(_fixture.Renderer, entered + 1000);                 // a second since the start: 30 steps in all
        Assert.Equal(30, scene.DigimonAnimation.IdleCounter);
    }

    [Fact]
    public void A_stall_of_many_seconds_runs_at_most_the_cap_of_steps_in_one_frame()
    {
        UseData();
        var (scene, entered) = Enter();
        using var owned = scene;

        // 1000 s = 30000 due steps: the frame runs 10000 of them and the next frames go on from the real time, not from a backlog
        scene.Frame(_fixture.Renderer, entered + 1_000_000);
        scene.Frame(_fixture.Renderer, entered + 1_000_000 + 1000);

        Assert.Equal((10_000 + 30) % 320, scene.DigimonAnimation!.IdleCounter);
        Assert.False(scene.ConnectionLost);
    }

    [Fact]
    public void The_field_shows_up_at_once_without_a_brightening()
    {
        UseData();
        var (scene, entered) = Enter();
        using var owned = scene;

        // ✅ 0x00b4e550: the 0x5f marker of the 0x03 handler goes back to 0 on the first field frame, nothing darkens the screen
        Assert.Empty(_fixture.Renderer.Darkens);
        scene.Frame(_fixture.Renderer, entered + 1000);
        scene.Frame(_fixture.Renderer, entered + 2000);
        Assert.Empty(_fixture.Renderer.Darkens);
    }

    /// <summary>The field's draws of the digimon (003_1, 80 wide) and of the tamer's body (a_00, 100 wide), in order.</summary>
    private (List<FakeRenderer.DrawCall> Digimon, List<FakeRenderer.DrawCall> Body) FieldDraws(SpriteOptions options)
    {
        UseData();
        _fixture.Game.SpriteOptions = options;
        var (scene, entered) = Enter();
        using var owned = scene;
        _fixture.Renderer.Draws.Clear();

        scene.Frame(_fixture.Renderer, entered + 16);

        var draws = _fixture.Renderer.Draws;
        return (draws.Where(d => d.Texture.Width == 80).ToList(), draws.Where(d => d.Texture.Width == 100).ToList());
    }

    [Fact]
    public void With_the_options_the_digimon_is_its_shadow_its_edge_and_its_sprite_at_the_same_corner()
    {
        var (digimon, body) = FieldDraws(SpriteOptions.Default);

        Assert.Equal([SpriteEffects.NormalAlpha, SpriteEffects.NormalAlpha, IRenderer.OpaqueAlpha], digimon.Select(d => d.Alpha));
        Assert.Single(digimon.Select(d => (d.X, d.Y)).Distinct());
        Assert.Equal([SpriteEffects.NormalAlpha, IRenderer.OpaqueAlpha], body.Select(d => d.Alpha));     // the tamer's shadow, then its body
        Assert.Single(body.Select(d => (d.X, d.Y)).Distinct());
    }

    [Fact]
    public void Without_the_shadow_and_anti_options_only_the_sprites_are_drawn()
    {
        var (digimon, body) = FieldDraws(SpriteOptions.Default with { Shadow = false, Edge = false });

        Assert.Equal([IRenderer.OpaqueAlpha], digimon.Select(d => d.Alpha));
        Assert.Equal([IRenderer.OpaqueAlpha], body.Select(d => d.Alpha));
    }

    [Fact]
    public void The_anti_option_alone_draws_only_the_digimon_edge()
    {
        var (digimon, body) = FieldDraws(SpriteOptions.Default with { Shadow = false });

        Assert.Equal([SpriteEffects.NormalAlpha, IRenderer.OpaqueAlpha], digimon.Select(d => d.Alpha));
        Assert.Single(body);
    }

    [Fact]
    public void The_field_and_the_hud_are_drawn_every_frame_of_the_scene()
    {
        UseData();
        var (scene, entered) = Enter();
        using var owned = scene;
        _fixture.Renderer.Draws.Clear();

        scene.Frame(_fixture.Renderer, entered + 16);

        Assert.NotEmpty(_fixture.Renderer.Draws);
        Assert.DoesNotContain(_fixture.Renderer.Draws, d => (d.X, d.Y) == (740, 540));   // the loading screen is gone
    }

    [Fact]
    public void A_map_the_table_does_not_know_is_a_lost_world_and_nothing_is_sent_to_the_server()
    {
        UseData(mapId: 99);                                            // the character's map (1) has no row
        var (scene, _) = Enter();
        using var owned = scene;
        Thread.Sleep(100);

        Assert.True(scene.ConnectionLost);
        Assert.False(scene.WorldEntered);
        Assert.False(_fixture.Game.HasEnteredWorld);
        Assert.Equal(4, _map.Received.Count);                          // 0x49 ×3 and 0x01; no 0x04, no 0xF0
        Assert.Contains(_fixture.Log, line => line.Contains("LoadMap Error"));
    }

    [Fact]
    public void A_map_without_a_code_or_missing_from_the_pack_is_a_lost_world()
    {
        UseData(code: "");
        var (scene, _) = Enter();
        using var owned = scene;
        Assert.True(scene.ConnectionLost);
    }

    [Fact]
    public void A_corrupt_map_file_is_a_lost_world_not_a_crash()
    {
        UseData(code: "R002");                                        // map\r002.map is three bytes
        var (scene, _) = Enter();
        using var owned = scene;

        Assert.True(scene.ConnectionLost);
        Assert.False(scene.WorldEntered);
        Assert.Contains(_fixture.Log, line => line.Contains("LoadMap Error"));
    }

    [Fact]
    public void A_connection_lost_during_the_loading_screen_ends_it()
    {
        UseData();
        ConnectedSession();
        using var scene = NewScene();
        scene.Frame(_fixture.Renderer, 0);

        _map.CloseClient();
        Pump(() => _session!.Step == WorldStep.Failed);
        _fixture.Renderer.Draws.Clear();
        scene.Frame(_fixture.Renderer, 90);

        Assert.True(scene.ConnectionLost);
        Assert.DoesNotContain(_fixture.Renderer.Draws, d => (d.X, d.Y) == (740, 540));
        Assert.Equal(3, _map.Received.Count);                          // the 0x01 never went out
    }

    [Fact]
    public void A_lost_connection_shows_the_text_and_a_click_goes_back_to_the_world_selection()
    {
        UseData();
        var (scene, entered) = Enter();
        using var owned = scene;
        Pump(() => _map.Received.Count >= 7);

        _map.CloseClient();
        Pump(() => _session!.Step == WorldStep.Failed);
        scene.Frame(_fixture.Renderer, entered + 100);
        Assert.True(scene.ConnectionLost);
        Assert.Empty(_switches);                                       // not yet: the text waits for the click

        _fixture.Game.Mouse.Handle(new MouseButtonEvent(400, 300, true), entered + 200);
        scene.Frame(_fixture.Renderer, entered + 200);
        _fixture.Game.Scenes.Frame(_fixture.Renderer, entered + 200);

        Assert.Equal([SceneId.Server], _switches);
        Assert.Null(_fixture.Game.World);
    }

    [Fact]
    public void The_scene_logs_its_frame_statistics_when_it_ends()
    {
        UseData();
        var (scene, entered) = Enter();
        scene.Frame(_fixture.Renderer, entered + 16);

        scene.Dispose();

        Assert.Contains(_fixture.Log, line => line.StartsWith("game scene:"));
    }
}
