using DRPG.Client.Net;
using DRPG.Client.World;
using DRPG.Shared.Formats.Map;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Tests;

/// <summary>
/// Stage 4 wild digimon tests (<c>0x11</c>, <c>0x00af25c0</c>, <c>0x00afdba0</c>, <c>0x007f3df0</c>, <c>0x00859340</c>,
/// <c>0x007f4b10</c>): spawn, command queue at cell centres, A* pathfinding around walls, teleport &gt; 6 cells,
/// state 12 despawn, 10 × 12 cell drop range and map 0xFA ignore.
/// </summary>
public sealed class FieldWildsTests
{
    private static FieldCollision Grid(params string[] rows)
    {
        int height = rows.Length, width = rows[0].Length;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((uint)width);
        w.Write((uint)height);
        w.Write(64u);
        w.Write(32u);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                w.Write(0u);
                w.Write(-1);
                w.Write(-1);
                w.Write((short)-1);
                w.Write((short)0);
                w.Write(0u);
                w.Write(rows[y][x] == '#' ? 0u : 1u);
            }
        w.Write((ushort)0);
        w.Write((ushort)0);
        return new FieldCollision(MapFile.Parse(ms.ToArray()));
    }

    private static FieldDigimonPacket WildPacket(Serial serial, byte state, short x, short y, short destX, short destY, ushort dir = 0) =>
        new(serial, "Yuramon", 14, 1, 2, 2, state, dir, x, y, destX, destY);

    [Fact]
    public void Wild_spawns_walks_around_obstacle_via_command_queue_and_updates_occupancy()
    {
        var collision = Grid(
            ".......",
            "...#...",
            "...#...",
            "...#...",
            ".......");
        var pathfinder = new FieldPathfinder(collision);
        var wilds = new FieldWilds();
        var tamer = new FieldEntity(3, 2);
        var serial = new Serial(10, 20);
        var rng = new Random(0);

        // Spawn at (2, 2) walking (state 4 -> ×1, 16 steps/cell) to (4, 2) across the wall
        wilds.Apply(WildPacket(serial, 4, 2, 2, 4, 2, dir: 6), mapId: 79, collision, _ => null, rng);
        Assert.Single(wilds.All);
        Assert.Equal(serial, collision.OccupantAt(2, 2));

        for (int step = 0; step < 120; step++)
            wilds.Step(tamer, collision, pathfinder, rng);

        Assert.True(wilds.TryGet(serial, out var wild));
        Assert.Equal((4, 2), (wild.Entity.CellX, wild.Entity.CellY));
        Assert.Equal(MoveState.Standing, wild.Entity.State);
        Assert.Equal(serial, collision.OccupantAt(4, 2));
        Assert.Equal(default, collision.OccupantAt(2, 2));
    }

    [Fact]
    public void Wild_command_farther_than_six_cells_teleports_and_stops()
    {
        var collision = Grid(Enumerable.Repeat(new string('.', 20), 5).ToArray());
        var pathfinder = new FieldPathfinder(collision);
        var wilds = new FieldWilds();
        var tamer = new FieldEntity(10, 2);
        var serial = new Serial(1, 2);
        var rng = new Random(0);

        wilds.Apply(WildPacket(serial, 1, 2, 2, 2, 2), mapId: 79, collision, _ => null, rng);
        wilds.Step(tamer, collision, pathfinder, rng);

        // Move command with origin 8 cells away (> 6) -> teleports to (10, 2) and stops
        wilds.Apply(WildPacket(serial, 4, 10, 2, 12, 2), mapId: 79, collision, _ => null, rng);
        wilds.Step(tamer, collision, pathfinder, rng);

        Assert.True(wilds.TryGet(serial, out var wild));
        Assert.Equal((10, 2), (wild.Entity.CellX, wild.Entity.CellY));
        Assert.Equal(MoveState.Standing, wild.Entity.State);
    }

    [Fact]
    public void State_12_removes_wild_and_vacates_its_cell()
    {
        var collision = Grid(".....", ".....", ".....");
        var wilds = new FieldWilds();
        var serial = new Serial(5, 6);
        var rng = new Random(0);

        wilds.Apply(WildPacket(serial, 1, 2, 1, 2, 1), mapId: 79, collision, _ => null, rng);
        Assert.Single(wilds.All);
        Assert.Equal(serial, collision.OccupantAt(2, 1));

        wilds.Apply(WildPacket(serial, FieldWilds.LeavingState, 2, 1, 2, 1), mapId: 79, collision, _ => null, rng);
        Assert.Empty(wilds.All);
        Assert.Equal(default, collision.OccupantAt(2, 1));
    }

    [Fact]
    public void Wild_beyond_10_by_12_cells_of_tamer_is_dropped()
    {
        var collision = Grid(Enumerable.Repeat(new string('.', 30), 30).ToArray());
        var pathfinder = new FieldPathfinder(collision);
        var wilds = new FieldWilds();
        var tamer = new FieldEntity(0, 0);
        var near = new Serial(1, 1);
        var farX = new Serial(2, 2);
        var farY = new Serial(3, 3);
        var rng = new Random(0);

        wilds.Apply(WildPacket(near, 1, 10, 12, 10, 12), mapId: 79, collision, _ => null, rng);
        wilds.Apply(WildPacket(farX, 1, 11, 0, 11, 0), mapId: 79, collision, _ => null, rng);
        wilds.Apply(WildPacket(farY, 1, 0, 13, 0, 13), mapId: 79, collision, _ => null, rng);

        wilds.Step(tamer, collision, pathfinder, rng);

        Assert.Single(wilds.All);
        Assert.True(wilds.TryGet(near, out _));
        Assert.Equal(default, collision.OccupantAt(11, 0));
        Assert.Equal(default, collision.OccupantAt(0, 13));
    }

    [Fact]
    public void Map_0xFA_ignores_0x11_packets()
    {
        var wilds = new FieldWilds();
        wilds.Apply(WildPacket(new Serial(1, 1), 1, 5, 5, 5, 5), mapId: FieldWilds.IgnoredMapId, null, _ => null, new Random(0));
        Assert.Empty(wilds.All);
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
