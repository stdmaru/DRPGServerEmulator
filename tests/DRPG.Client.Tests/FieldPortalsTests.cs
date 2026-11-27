using DRPG.Client.Features.Field;
using DRPG.Client.Net;
using DRPG.Client.Scenes;
using DRPG.Client.Startup;
using DRPG.Client.Text;
using DRPG.Client.World;
using DRPG.Engine.Platform;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Formats.Sprite;
using DRPG.Shared.Packets.Channel;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Tests;

/// <summary>
/// Portal rendering cadence and full cross-map <c>0xB0</c> transition tests (<c>0x00b380d0</c>, <c>0x00a121f0</c>,
/// <c>0x00acb072</c>, <c>0x00af0d70</c>, <c>0x00aba410</c>): loading screen, map reload, BGM switch, clearing wilds,
/// respawning NPCs for the new map, sending <c>C2S 0x04</c> + <c>C2S 0xB0</c>, and hostile unknown map id handling.
/// </summary>
public sealed class FieldPortalsTests : IDisposable
{
    private const string CharacterName = "Marcus";

    private readonly SceneFixture _fixture = new();
    private readonly FakeServer _map = new();
    private readonly FakeServer _global = new();
    private WorldSession? _session;

    public void Dispose()
    {
        _session?.Dispose();
        _fixture.Game.World?.Dispose();
        _map.Dispose();
        _global.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public void Portal_animation_advances_every_2_steps_for_standard_and_4_for_square()
    {
        var portals = new FieldPortals(NoFieldSprites.Instance);
        Assert.Equal((0, 0), (portals.PortalFrame, portals.SquareFrame));

        portals.Step();
        Assert.Equal((0, 0), (portals.PortalFrame, portals.SquareFrame));
        portals.Step();
        Assert.Equal((1, 0), (portals.PortalFrame, portals.SquareFrame));
        portals.Step();
        portals.Step();
        Assert.Equal((2, 1), (portals.PortalFrame, portals.SquareFrame));
    }

    [Fact]
    public void WorldLoading_map_transition_runs_16_frames_and_returns_MapTransitionDone()
    {
        var loading = new WorldLoading(new Random(0), backgroundCount: 3);
        loading.BeginMapTransition(backgroundCount: 3);
        loading.MapLoaded();

        Assert.Equal(LoadingPhase.Map, loading.Phase);
        Assert.True(loading.Showing);

        ulong t = 0;
        LoadingAction action = LoadingAction.None;
        for (int i = 0; i < WorldLoading.MinimumFrames + 2 && action == LoadingAction.None; i++, t += WorldLoading.FrameMs)
            action = loading.Update(t);

        Assert.Equal(LoadingAction.MapTransitionDone, action);
        Assert.Equal(LoadingPhase.Done, loading.Phase);
        Assert.False(loading.Showing);
        Assert.Equal(WorldLoading.MinimumFrames, loading.FramesDrawn);
    }

    [Fact]
    public void Portal_cursor_matches_cell_type_range_1_to_8999()
    {
        Assert.False(FieldPortals.IsPortalCursorType(0));
        Assert.True(FieldPortals.IsPortalCursorType(1));
        Assert.True(FieldPortals.IsPortalCursorType(FieldPortals.TypeStandard));
        Assert.True(FieldPortals.IsPortalCursorType(8999));
        Assert.False(FieldPortals.IsPortalCursorType(9000));
    }

    [Fact]
    public void Cross_map_0xB0_waits_for_tamer_to_stop_walking_before_starting_loading()
    {
        UseTwoMaps();
        var session = Connect(mapId: 1);
        var scene = new GameScene(_fixture.Game, new Random(0));
        scene.Init();
        ulong t = EnterWorld(scene, session);

        // Start walking 2 cells east (16 logic steps at Run speed)
        scene.State!.Party.Tamer.SetDestination(new Cell(7, 6));
        scene.Step();
        Assert.True(scene.State!.Party.Tamer.IsMoving);

        // Server sends cross-map 0xB0 while tamer is still walking to the portal
        _map.Send(new TeleportCharacterPacket(2, 4, 5));
        t += 34;
        Pump(() =>
        {
            scene.Frame(_fixture.Renderer, t);
            t += 34;
            // Must NOT snap to loading while tamer is still walking
            return !scene.State!.Party.Tamer.IsMoving || scene.Loading.Showing;
        });

        // After stopping on the portal cell, the warp effect triggers and then loading begins
        Assert.False(scene.State!.Party.Tamer.IsMoving);
        Pump(() =>
        {
            scene.Frame(_fixture.Renderer, t);
            t += WorldLoading.FrameMs;
            return scene.Loading.Showing;
        });
        Assert.Equal(2, _fixture.Game.World!.MapId);
        Assert.Equal(new Cell(4, 5), scene.State!.Party.Tamer.Cell);
    }

    [Fact]
    public void Cross_map_0xB0_shows_loading_reloads_map_clears_wilds_swaps_npcs_and_sends_0x04_and_0xB0()
    {
        UseTwoMaps();
        var session = Connect(mapId: 1);
        var scene = new GameScene(_fixture.Game, new Random(0));
        scene.Init();
        ulong t = EnterWorld(scene, session);

        // Map 1 starts with its NPC and receives a wild digimon
        Assert.Single(scene.State!.Area.Npcs.All);
        Assert.Equal(101, scene.State!.Area.Npcs.All[0].NpcId);

        _map.Send(new FieldDigimonPacket(new Serial(9, 9), "Yuramon", 14, 1, 2, 2, 1, 0, 6, 6, 6, 6));
        Pump(() =>
        {
            scene.Frame(_fixture.Renderer, t);
            t += 34;
            return scene.State!.Area.Wilds.All.Count == 1;
        });

        int sentBefore = _map.Received.Count;

        // Server sends cross-map 0xB0 to map 2 at (4, 5)
        _map.Send(new TeleportCharacterPacket(2, 4, 5));
        Pump(() =>
        {
            scene.Frame(_fixture.Renderer, t);
            t += WorldLoading.FrameMs;
            return scene.Loading.Showing;
        });

        Assert.Equal(2, _fixture.Game.World!.MapId);
        Assert.Empty(scene.State!.Area.Wilds.All);
        Assert.Single(scene.State!.Area.Npcs.All);
        Assert.Equal(202, scene.State!.Area.Npcs.All[0].NpcId);
        Assert.Equal(new Cell(4, 5), scene.State!.Party.Tamer.Cell);
        Assert.Equal(new Cell(3, 4), scene.State!.Party.Partner!.Cell);

        // Finish the 16-frame transition loading screen
        for (int guard = 0; scene.Loading.Showing && guard < 100; guard++, t += WorldLoading.FrameMs)
            scene.Frame(_fixture.Renderer, t);

        Assert.False(scene.Loading.Showing);
        Assert.False(scene.ConnectionLost);

        // Client sends C2S 0x04 (RefreshCharacterInfoReq) followed by C2S 0xB0 (TeleportAckRequest)
        Pump(() => _map.Received.Count >= sentBefore + 2);
        var post = _map.Received.Skip(sentBefore).Take(2).ToArray();
        Assert.Equal([(ushort)MapOpcode.RefreshCharacterInfoReq, (ushort)MapOpcode.TeleportCharacter], post);
    }

    [Fact]
    public void Cross_map_0xB0_to_unknown_map_loses_connection_safely()
    {
        UseTwoMaps();
        var session = Connect(mapId: 1);
        var scene = new GameScene(_fixture.Game, new Random(0));
        scene.Init();
        ulong t = EnterWorld(scene, session);

        _map.Send(new TeleportCharacterPacket(999, 5, 5));
        Pump(() =>
        {
            scene.Frame(_fixture.Renderer, t);
            t += WorldLoading.FrameMs;
            return scene.ConnectionLost;
        });

        Assert.True(scene.ConnectionLost);
    }

    private void UseTwoMaps()
    {
        var table = PackArchive.Open(_fixture.Game.Install.TablePath);
        var rows = new Dictionary<string, IReadOnlyList<string[]>>(StringComparer.OrdinalIgnoreCase)
        {
            ["map"] = new List<string[]>
            {
                new[] { "1", "Map One", "0", "0", "R001" },
                new[] { "2", "Map Two", "0", "0", "R001" },
            },
            ["NPC_INFO"] = new List<string[]>
            {
                new[] { "1", "101", "NPC One", "0", "2", "2", "NPC_a", "icon", "0", "1", "0", "0", "0", "2", "1" },
                new[] { "2", "202", "NPC Two", "0", "3", "3", "NPC_b", "icon", "0", "1", "0", "0", "0", "2", "1" },
            },
        };
        _fixture.Game.Data = new GameData(_fixture.Resource, table, rows, new Dictionary<string, byte[]>(), new TextAttach(), new ProhibitedWords(),
            new Dictionary<string, SpriteFile>());
    }

    private WorldSession Connect(int mapId)
    {
        var settings = new ClientSettings(ClientSettings.Local.Login, ClientSettings.Local.Channel, _map.Address, _global.Address);
        _session = new WorldSession(settings, _fixture.Game.Command, _fixture.Log.Add);
        var slot = new CharacterListSlot(Uid: 7, TamerId: 1, Name: CharacterName, Level: 12, X: 5, Y: 6, LeaderSpeciesId: 3, LeaderLevel: 5,
            LeaderName: "Agumon", TotalBattles: 0, TotalWins: 0, Slot: 0, MapId: (byte)mapId, CheckOffset: 0);
        _fixture.Game.World = new WorldEntry(slot, 0) { Session = _session };
        _session.Start(CharacterName, firstEntry: true);
        Pump(() => _session.Step == WorldStep.Connected && _map.Received.Count >= 3);
        return _session;
    }

    private ulong EnterWorld(GameScene scene, WorldSession session)
    {
        ulong t = 0;
        for (int guard = 0; scene.Loading.Phase == LoadingPhase.PreMap && guard < 10_000; guard++, t += WorldLoading.FrameMs)
            scene.Frame(_fixture.Renderer, t);
        Pump(() => _map.Received.Count >= 4);
        var character = ClientPacket.Parse<CharacterDataPacket>(new byte[CharacterDataPacket.Length.Min])
            with { Uid = 7, Serial = new Serial(1, 2), Name = CharacterName, TamerId = 1, Level = 12, X = 5, Y = 6, ServerUnixTime = 1_800_000_000 };
        var party = character.Party.ToArray();
        party[0] = DigimonStatus.Empty with { Uid = 9, SpeciesId = 3, Name = "Agumon", Level = 5, Hp = 50, MaxHp = 100 };
        character = character with { Party = party };
        _map.Send(character);
        _map.Send(ClientPacket.Parse<InventoryDataPacket>(new byte[InventoryDataPacket.Length.Min]));
        Pump(() => session.Step == WorldStep.DataReady);
        for (int guard = 0; scene.Loading.Showing && !scene.ConnectionLost && guard < 10_000; guard++, t += WorldLoading.FrameMs)
            scene.Frame(_fixture.Renderer, t);
        Assert.True(scene.WorldEntered);
        return t;
    }

    private void Pump(Func<bool> done)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (!done())
        {
            _session?.Update();
            if (DateTime.UtcNow > limit)
                throw new TimeoutException("timed out waiting for fake server");
            Thread.Sleep(5);
        }
    }
}
