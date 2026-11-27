using System.Text;
using DRPG.Client.Net;
using DRPG.Client.Scenes;
using DRPG.Client.Startup;
using DRPG.Client.Text;
using DRPG.Client.Ui;
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
/// Stage 1 movement tests: 8-way direction math, 30 FPS cell physics (<c>0x0085e5b0</c>), partner digimon following
/// (<c>0x008671c0</c>), <c>0x20</c> 800 ms throttle (<c>0x00b4b360</c>), directional animation and <see cref="GameScene"/> walking.
/// </summary>
public sealed class FieldMovementTests
{
    [Theory]
    [InlineData(5, 5, 5, 6, 0, 0, 2)]       // S
    [InlineData(5, 5, 4, 6, 1, -4, 2)]      // SW
    [InlineData(5, 5, 4, 5, 2, -4, 0)]      // W
    [InlineData(5, 5, 4, 4, 3, -4, -2)]     // NW
    [InlineData(5, 5, 5, 4, 4, 0, -2)]      // N
    [InlineData(5, 5, 6, 4, 5, 4, -2)]      // NE
    [InlineData(5, 5, 6, 5, 6, 4, 0)]       // E
    [InlineData(5, 5, 6, 6, 7, 4, 2)]       // SE
    [InlineData(5, 5, 5, 5, -1, 0, 0)]      // same cell
    public void Direction_and_base_velocity_match_0x007f3880_and_0x0085e5b0(
        int fx, int fy, int tx, int ty, int expectedDir, int dx, int dy)
    {
        Assert.Equal(expectedDir, FieldDirection.FromTo(fx, fy, tx, ty));
        Assert.Equal((dx, dy), FieldDirection.BaseVelocity(expectedDir));
    }

    [Theory]
    [InlineData(0, 5, 4)]   // S -> behind is N
    [InlineData(1, 6, 4)]   // SW -> behind is NE
    [InlineData(2, 6, 5)]   // W -> behind is E
    [InlineData(3, 6, 6)]   // NW -> behind is SE
    [InlineData(4, 5, 6)]   // N -> behind is S
    [InlineData(5, 4, 6)]   // NE -> behind is SW
    [InlineData(6, 4, 5)]   // E -> behind is W
    [InlineData(7, 4, 4)]   // SE -> behind is NW
    public void Cell_behind_is_one_step_opposite_to_the_direction(int dir, int expectedX, int expectedY) =>
        Assert.Equal((expectedX, expectedY), FieldDirection.CellBehind(5, 5, dir));

    [Theory]
    [InlineData(MoveState.Walk, 16)]    // ×1: ±4, ±2 -> 16 steps/cell
    [InlineData(MoveState.Run, 8)]      // ×2: ±8, ±4 -> 8 steps/cell (3.75 cells/s at 30 FPS)
    [InlineData(MoveState.Fast, 4)]     // ×4: ±16, ±8 -> 4 steps/cell (7.5 cells/s at 30 FPS)
    public void Moving_one_diagonal_cell_takes_the_exact_number_of_steps(MoveState state, int stepsPerCell)
    {
        var entity = new FieldEntity(5, 5);
        entity.SetDestination(6, 6, state);

        for (int i = 0; i < stepsPerCell - 1; i++)
        {
            entity.StepPhysics();
            Assert.False(entity.IsAtCellCenter);
            Assert.Equal((5, 5), (entity.CellX, entity.CellY));
        }

        entity.StepPhysics();
        Assert.True(entity.IsAtCellCenter);
        Assert.Equal((6, 6), (entity.CellX, entity.CellY));
        Assert.Equal(7, entity.Direction);

        // Next step detects arrival and returns to Standing
        entity.StepPhysics();
        Assert.Equal(MoveState.Standing, entity.State);
    }

    [Fact]
    public void Switching_to_fast_mid_cell_aligns_half_step_pixels_so_it_still_lands_on_center()
    {
        var entity = new FieldEntity(5, 5);
        entity.SetDestination(6, 6, MoveState.Run);
        entity.StepPhysics();                   // +8, +4 -> WorldX % 16 == 8, WorldY % 8 == 4
        entity.SetDestination(6, 6, MoveState.Fast);

        for (int i = 0; i < 6; i++)
            entity.StepPhysics();

        Assert.Equal((6, 6), (entity.CellX, entity.CellY));
        Assert.True(entity.IsAtCellCenter);
        Assert.Equal(MoveState.Standing, entity.State);
    }

    [Fact]
    public void Partner_digimon_follows_one_cell_behind_the_tamer()
    {
        var tamer = new FieldEntity(5, 5);
        var partner = new FieldEntity(4, 4);
        tamer.SetDestination(9, 5);             // East (dir 6)

        for (int step = 0; step < 60; step++)
        {
            tamer.StepPhysics();
            partner.FollowLeader(tamer);
        }

        Assert.Equal((9, 5), (tamer.CellX, tamer.CellY));
        Assert.Equal(MoveState.Standing, tamer.State);
        Assert.Equal((8, 5), (partner.CellX, partner.CellY));       // 1 cell behind (West of destination)
        Assert.Equal(MoveState.Standing, partner.State);
    }

    [Fact]
    public void Partner_within_one_cell_turns_to_face_tamer_without_moving()
    {
        var tamer = new FieldEntity(5, 5);
        var partner = new FieldEntity(4, 5, direction: 0);
        tamer.SetDestination(6, 5);             // East (dir 6)

        tamer.StepPhysics();
        partner.FollowLeader(tamer);

        Assert.Equal((4, 5), (partner.CellX, partner.CellY));
        Assert.Equal(MoveState.Standing, partner.State);
    }

    [Fact]
    public void Partner_farther_than_five_cells_teleports_to_tamer_minus_one()
    {
        var tamer = new FieldEntity(15, 15);
        var partner = new FieldEntity(5, 5);

        partner.FollowLeader(tamer);

        Assert.Equal((14, 14), (partner.CellX, partner.CellY));
        Assert.Equal(MoveState.Standing, partner.State);
        Assert.True(partner.IsAtCellCenter);
    }

    [Fact]
    public void Move_packet_queue_sends_immediately_then_throttles_for_800_ms_keeping_latest()
    {
        var queue = new MovePacketQueue();

        queue.Enqueue(10, 20);
        Assert.True(queue.TryDequeue(1000, out short x, out short y));
        Assert.Equal((10, 20), (x, y));

        // Multiple clicks during cooldown coalesce to the latest
        queue.Enqueue(11, 21);
        queue.Enqueue(12, 22);
        Assert.False(queue.TryDequeue(1500, out _, out _));
        Assert.False(queue.TryDequeue(1800, out _, out _));     // exact 800 is still cooling (800 < elapsed)

        Assert.True(queue.TryDequeue(1801, out x, out y));
        Assert.Equal((12, 22), (x, y));
        Assert.False(queue.TryDequeue(3000, out _, out _));
    }

    [Fact]
    public void Directional_animator_uses_direction_range_and_pauses_idle_counter_while_walking()
    {
        // Build an .ani with Standing (1) and Run (5) in directions 0 and 6
        const int frames = 16;
        var data = new byte[0x2a + frames * 12 + 960];
        Encoding.ASCII.GetBytes(AniFile.Signature).CopyTo(data, 0);
        Encoding.ASCII.GetBytes("d_000.spr").CopyTo(data, 0x0e);
        BitConverter.GetBytes(frames).CopyTo(data, 0x26);
        void SetRange(int anim, int dir, int start, int count)
        {
            int at = 0x2a + frames * 12 + (anim * AniFile.DirectionCount + dir) * 6;
            BitConverter.GetBytes((ushort)start).CopyTo(data, at);
            BitConverter.GetBytes((ushort)(start + count - 1)).CopyTo(data, at + 2);
            BitConverter.GetBytes((ushort)count).CopyTo(data, at + 4);
        }
        SetRange(1, 0, 0, 4);
        SetRange(1, 6, 4, 4);
        SetRange(5, 0, 8, 4);
        SetRange(5, 6, 12, 4);

        var textures = Enumerable.Range(0, frames).Select(_ => (ITexture)new FakeTexture(8, 8)).ToList();
        var animator = new FieldAnimator(new SpriteAnimator(AniFile.Parse(data), textures, null, null), digimon: true, species: 3, initialOffset: 0);

        animator.SetDirection(6);
        animator.Switch(FieldAnimator.Run);
        Assert.Equal(12, animator.View.Frame);

        for (int i = 0; i < 250; i++)
            animator.Step();
        Assert.Equal(FieldAnimator.Run, animator.Animation);
        Assert.Equal(0, animator.IdleCounter);                  // idle counter does not advance while running
    }

    [Fact]
    public void Map_decoder_parses_0xB0_and_rejects_wrong_length()
    {
        var payload = Convert.FromHexString("2E0000000F000F00");
        var frame = new IncomingFrame(new PacketHeader((ushort)MapOpcode.TeleportCharacter, (uint)(PacketHeader.Length + payload.Length), 0, 0), payload);
        var decoded = MapDecoder.Map(frame);
        Assert.Equal(new TeleportCharacterPacket(46, 15, 15), decoded);

        var shortFrame = new IncomingFrame(new PacketHeader((ushort)MapOpcode.TeleportCharacter, (uint)(PacketHeader.Length + 7), 0, 0), new byte[7]);
        Assert.Throws<PacketFormatException>(() => MapDecoder.Map(shortFrame));
    }
}

/// <summary>End-to-end walking tests inside <see cref="GameScene"/> against fake servers.</summary>
public sealed class GameSceneWalkingTests : IDisposable
{
    private readonly SceneFixture _fixture = new();
    private readonly FakeServer _map = new();
    private readonly FakeServer _global = new();
    private WorldSession? _session;

    public GameSceneWalkingTests()
    {
        _fixture.Game.Scenes = new SceneManager(_ => throw new InvalidOperationException());
        var table = PackArchive.Open(_fixture.Game.Install.TablePath);
        var rows = new Dictionary<string, IReadOnlyList<string[]>>
        {
            ["map"] = new List<string[]> { new[] { "1", "Test Plains", "0", "0", "R001" } },
        };
        _fixture.Game.Data = new GameData(_fixture.Resource, table, rows, new Dictionary<string, byte[]>(), new TextAttach(), new ProhibitedWords(),
            new Dictionary<string, SpriteFile>());
    }

    public void Dispose()
    {
        _session?.Dispose();
        _fixture.Game.World?.Dispose();
        _map.Dispose();
        _global.Dispose();
        _fixture.Dispose();
    }

    private (GameScene Scene, ulong Entered) EnterWorld()
    {
        var settings = new ClientSettings(ClientSettings.Local.Login, ClientSettings.Local.Channel, _map.Address, _global.Address);
        _session = new WorldSession(settings, _fixture.Game.Command, _fixture.Log.Add);
        var slot = new CharacterListSlot(7, 1, "Marcus", 12, 5, 6, 3, 5, "Agumon", 0, 0, 0, 1, 0);
        _fixture.Game.World = new WorldEntry(slot, 0) { Session = _session };
        _session.Start("Marcus", firstEntry: true);
        Pump(() => _session.Step == WorldStep.Connected && _map.Received.Count >= 3);

        var scene = new GameScene(_fixture.Game, new Random(0));
        scene.Init();
        ulong t = 0;
        for (; scene.Loading.Phase == LoadingPhase.PreMap; t += WorldLoading.FrameMs)
            scene.Frame(_fixture.Renderer, t);
        Pump(() => _map.Received.Count >= 4);

        var character = ClientPacket.Parse<CharacterDataPacket>(new byte[CharacterDataPacket.Length.Min])
            with { Uid = 7, Serial = new Serial(1, 2), Name = "Marcus", TamerId = 1, Level = 12, X = 5, Y = 6, ServerUnixTime = 1_800_000_000 };
        var party = character.Party.ToArray();
        party[0] = DigimonStatus.Empty with { Uid = 9, SpeciesId = 3, Name = "Agumon", Level = 5, Hp = 50, MaxHp = 100 };
        _map.Send(character with { Party = party });
        _map.Send(ClientPacket.Parse<InventoryDataPacket>(new byte[InventoryDataPacket.Length.Min]));
        Pump(() => _session.Step == WorldStep.DataReady);
        for (; scene.Loading.Showing; t += WorldLoading.FrameMs)
            scene.Frame(_fixture.Renderer, t);
        return (scene, t - WorldLoading.FrameMs);
    }

    private void Pump(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline)
        {
            _session?.Update();
            Thread.Sleep(5);
        }
        Assert.True(done());
    }

    [Fact]
    public void Clicking_the_ground_walks_the_tamer_sends_0x20_and_the_partner_follows()
    {
        var (scene, t) = EnterWorld();
        using var owned = scene;

        // Tamer starts at cell (5, 6) -> world (352, 208), camera is (0, 0). Click cell (8, 6) -> screen (544, 208).
        _fixture.Game.Mouse.Handle(new MouseButtonEvent(8 * 64 + 32, 6 * 32 + 16, true), t + 34);
        scene.Frame(_fixture.Renderer, t + 34);
        _fixture.Game.Mouse.EndFrame();
        _fixture.Game.Mouse.Handle(new MouseButtonEvent(8 * 64 + 32, 6 * 32 + 16, false), t + 68);

        Pump(() => _map.Received.Contains((ushort)MapOpcode.MoveCharacter));

        // Advance 2 seconds (60 steps): enough for 3 cells (24 steps) plus partner catching up
        scene.Frame(_fixture.Renderer, t + 2000);

        Assert.Equal((8, 6), (scene.TamerEntity.CellX, scene.TamerEntity.CellY));
        Assert.Equal(MoveState.Standing, scene.TamerEntity.State);
        Assert.Equal((7, 6), (scene.DigimonEntity!.CellX, scene.DigimonEntity.CellY));
    }

    [Fact]
    public void Clicking_on_the_bottom_hud_bar_does_not_move_the_tamer()
    {
        var (scene, t) = EnterWorld();
        using var owned = scene;

        _fixture.Game.Mouse.Handle(new MouseButtonEvent(400, 520, true), t + 34);
        scene.Frame(_fixture.Renderer, t + 34);
        scene.Frame(_fixture.Renderer, t + 1000);

        Assert.Equal((5, 6), (scene.TamerEntity.CellX, scene.TamerEntity.CellY));
        Assert.DoesNotContain((ushort)MapOpcode.MoveCharacter, _map.Received);
    }

    [Fact]
    public void Receiving_0xB0_for_the_same_map_snaps_tamer_and_partner()
    {
        var (scene, t) = EnterWorld();
        using var owned = scene;

        _fixture.Game.Mouse.Handle(new MouseButtonEvent(9 * 64 + 32, 6 * 32 + 16, true), t + 34);
        scene.Frame(_fixture.Renderer, t + 34);
        _fixture.Game.Mouse.EndFrame();
        _fixture.Game.Mouse.Handle(new MouseButtonEvent(9 * 64 + 32, 6 * 32 + 16, false), t + 50);
        Assert.Equal(MoveState.Run, scene.TamerEntity.State);

        _map.Send(new TeleportCharacterPacket(1, 3, 4));
        Pump(() =>
        {
            scene.Frame(_fixture.Renderer, t + 100);
            return scene.TamerEntity.CellX == 3 && scene.TamerEntity.CellY == 4;
        });

        Assert.Equal(MoveState.Standing, scene.TamerEntity.State);
        Assert.Equal((2, 3), (scene.DigimonEntity!.CellX, scene.DigimonEntity.CellY));
    }
}
