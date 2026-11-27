using DRPG.Client.Features.Field;
using DRPG.Client.Net;
using DRPG.Client.World;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Tests;

/// <summary>
/// Wild digimon tests through <see cref="FieldHandlers"/> (<c>0x11</c>, <c>0x00af25c0</c>, <c>0x00afdba0</c>, <c>0x007f3df0</c>, <c>0x00859340</c>,
/// <c>0x007f4b10</c>): spawn, command queue at cell centres, A* pathfinding around walls, teleport &gt; 6 cells,
/// state 12 despawn, 10 × 12 cell drop range and map 0xFA ignore.
/// </summary>
public sealed class FieldWildsTests
{
    private static FieldDigimonPacket WildPacket(Serial serial, byte state, short x, short y, short destX, short destY, ushort dir = 0) =>
        new(serial, "Yuramon", 14, 1, 2, 2, state, dir, x, y, destX, destY);

    [Fact]
    public void Wild_spawns_walks_around_obstacle_via_command_queue_and_updates_occupancy()
    {
        using var field = new FieldFixture(
        [
            ".......",
            "...#...",
            "...#...",
            "...#...",
            ".......",
        ], tamer: new Cell(3, 2));
        var serial = new Serial(10, 20);

        // Spawn at (2, 2) walking (state 4 -> ×1, 16 steps/cell) to (4, 2) across the wall
        field.Handlers.Handle(WildPacket(serial, 4, 2, 2, 4, 2, dir: 6));
        Assert.Single(field.Wilds.All);
        Assert.Equal(serial, field.Collision.OccupantAt(2, 2));

        for (int step = 0; step < 120; step++)
            field.Wilds.Step(field.Tamer);

        var wild = Assert.IsType<WildEntity>(field.Wilds.Find(serial));
        Assert.Equal(new Cell(4, 2), wild.Entity.Cell);
        Assert.Equal(MoveState.Standing, wild.Entity.State);
        Assert.Equal(serial, field.Collision.OccupantAt(4, 2));
        Assert.Equal(default, field.Collision.OccupantAt(2, 2));
    }

    [Fact]
    public void Wild_command_farther_than_six_cells_teleports_and_stops()
    {
        using var field = new FieldFixture(FieldFixture.Open(20, 5), tamer: new Cell(10, 4));
        var serial = new Serial(1, 2);

        field.Handlers.Handle(WildPacket(serial, 1, 2, 2, 2, 2));
        field.Wilds.Step(field.Tamer);

        // Move command with origin 8 cells away (> 6) -> teleports to (10, 2) and stops
        field.Handlers.Handle(WildPacket(serial, 4, 10, 2, 12, 2));
        field.Wilds.Step(field.Tamer);

        var wild = Assert.IsType<WildEntity>(field.Wilds.Find(serial));
        Assert.Equal(new Cell(10, 2), wild.Entity.Cell);
        Assert.Equal(MoveState.Standing, wild.Entity.State);
    }

    [Fact]
    public void State_12_removes_wild_and_vacates_its_cell()
    {
        using var field = new FieldFixture(FieldFixture.Open(5, 3), tamer: new Cell(0, 2));
        var serial = new Serial(5, 6);

        field.Handlers.Handle(WildPacket(serial, 1, 2, 1, 2, 1));
        Assert.Single(field.Wilds.All);
        Assert.Equal(serial, field.Collision.OccupantAt(2, 1));

        field.Handlers.Handle(WildPacket(serial, FieldHandlers.WildLeavingState, 2, 1, 2, 1));
        Assert.Empty(field.Wilds.All);
        Assert.Equal(default, field.Collision.OccupantAt(2, 1));
    }

    [Fact]
    public void Wild_beyond_10_by_12_cells_of_tamer_is_dropped()
    {
        using var field = new FieldFixture(FieldFixture.Open(30, 30), tamer: new Cell(0, 0));
        var near = new Serial(1, 1);
        var farX = new Serial(2, 2);
        var farY = new Serial(3, 3);

        field.Handlers.Handle(WildPacket(near, 1, 10, 12, 10, 12));
        field.Handlers.Handle(WildPacket(farX, 1, 11, 0, 11, 0));
        field.Handlers.Handle(WildPacket(farY, 1, 0, 13, 0, 13));

        field.Wilds.Step(field.Tamer);

        Assert.Single(field.Wilds.All);
        Assert.NotNull(field.Wilds.Find(near));
        Assert.Equal(default, field.Collision.OccupantAt(11, 0));
        Assert.Equal(default, field.Collision.OccupantAt(0, 13));
    }

    [Fact]
    public void Map_0xFA_ignores_0x11_packets()
    {
        using var field = new FieldFixture(FieldFixture.Open(10, 10), tamer: new Cell(0, 0), mapId: FieldHandlers.WildlessMapId);
        field.Handlers.Handle(WildPacket(new Serial(1, 1), 1, 5, 5, 5, 5));
        Assert.Empty(field.Wilds.All);
    }

    [Fact]
    public void Standing_0x11_stops_walking_wild_and_applies_final_direction()
    {
        using var field = new FieldFixture(FieldFixture.Open(7, 5), tamer: new Cell(6, 4));
        var serial = new Serial(7, 8);

        // Wild starts walking NW from (3, 3) toward (1, 1)
        field.Handlers.Handle(WildPacket(serial, 4, 3, 3, 1, 1, dir: 3));
        // Step 16 frames (1 cell at Walk speed) to reach cell centre (2, 2) facing NW (3)
        for (int i = 0; i < 16; i++)
            field.Wilds.Step(field.Tamer);
        var wild = Assert.IsType<WildEntity>(field.Wilds.Find(serial));
        Assert.Equal(new Cell(2, 2), wild.Entity.Cell);
        Assert.Equal(Direction.NorthWest, wild.Entity.Direction);

        // Server sends Standing (state 1) at (2, 2) facing North (4): must stop immediately and face North (4), not keep walking NW
        field.Handlers.Handle(WildPacket(serial, 1, 2, 2, 2, 2, dir: 4));
        field.Wilds.Step(field.Tamer);

        Assert.Equal(MoveState.Standing, wild.Entity.State);
        Assert.Equal(new Cell(2, 2), wild.Entity.Cell);
        Assert.Equal(Direction.North, wild.Entity.Direction);
    }

    [Fact]
    public void Wild_blocked_north_by_own_occupancy_faces_north_matching_0x007f4b10()
    {
        using var field = new FieldFixture(FieldFixture.Open(5, 5), tamer: new Cell(4, 4));
        var serial = new Serial(9, 10);

        // Spawn at (2, 3): 0x00b36550 occupies both (2, 3) and (2, 2)
        field.Handlers.Handle(WildPacket(serial, 1, 2, 3, 2, 3, dir: 0));
        field.Wilds.Step(field.Tamer);
        Assert.Equal(serial, field.Collision.OccupantAt(2, 2));

        // Server moved wild to (2, 2) with Facing = NE (5) (e.g. after a dx=+1, dy=-2 walk) and now sends Standing at (2, 2)
        field.Handlers.Handle(WildPacket(serial, 1, 2, 2, 2, 2, dir: 5));
        field.Wilds.Step(field.Tamer);

        // ✅ 0x00859340 sets Dest=(2,2), State=Run, Dir=NE(5); then 0x007f4b10 finds (2,2) occupied (by self!), sets Dest=(2,3)
        // AND still overwrites Direction = FromTo((2,3) -> (2,2)) = North (4), then 0x0085e5b0 stops it facing North (4).
        var wild = Assert.IsType<WildEntity>(field.Wilds.Find(serial));
        Assert.Equal(MoveState.Standing, wild.Entity.State);
        Assert.Equal(new Cell(2, 3), wild.Entity.Cell);
        Assert.Equal(Direction.North, wild.Entity.Direction);

        // ✅ 0x007f3df0 lines 22-55: while playing IdleA/IdleB (state 2/3), a Standing 0x11 keeps IdleA and applies
        // cmd.Direction without triggering 0x007f4b10 until state returns to Standing (1).
        wild.Entity.SetIdleState(MoveState.IdleA);
        field.Handlers.Handle(WildPacket(serial, 1, 2, 2, 2, 2, dir: 5));
        field.Wilds.Step(field.Tamer);
        Assert.Equal(MoveState.IdleA, wild.Entity.State);
        Assert.Equal(Direction.NorthEast, wild.Entity.Direction);
    }

    [Fact]
    public void Hostile_direction_out_of_range_is_not_applied()
    {
        using var field = new FieldFixture(FieldFixture.Open(5, 5), tamer: new Cell(4, 4));
        var serial = new Serial(11, 12);

        field.Handlers.Handle(WildPacket(serial, 1, 2, 2, 2, 2, dir: 0xFFFF));
        field.Wilds.Step(field.Tamer);

        var wild = Assert.IsType<WildEntity>(field.Wilds.Find(serial));
        Assert.Equal(Direction.South, wild.Entity.Direction);
        Assert.Equal(new Cell(2, 2), wild.Entity.Cell);
    }

    [Fact]
    public void Map_decoder_parses_0x11_and_rejects_wrong_length()
    {
        var payload = Convert.FromHexString("2E709CEA6897AE41B514A9EAAEAC2949597572616D6F6E0000000000000000000000000000000E0001000200020102001900410019004100");
        var frame = new IncomingFrame(new PacketHeader((ushort)MapOpcode.FieldDigimonUpdate, (uint)(PacketHeader.Length + payload.Length), 0, 0), payload);
        var decoded = Assert.IsType<FieldDigimonPacket>(MapDecoder.Map(frame));
        Assert.Equal("Yuramon", decoded.Name);
        Assert.Equal(14, decoded.SpeciesId);

        var shortFrame = new IncomingFrame(new PacketHeader((ushort)MapOpcode.FieldDigimonUpdate, (uint)(PacketHeader.Length + 55), 0, 0), new byte[55]);
        Assert.Throws<PacketFormatException>(() => MapDecoder.Map(shortFrame));
    }
}
