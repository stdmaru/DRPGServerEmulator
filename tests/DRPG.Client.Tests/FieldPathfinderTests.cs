using DRPG.Client.World;
using DRPG.Shared.Formats.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Tests;

/// <summary>
/// Stage 3 pathfinding tests: v322 A* (<c>0x00b43200</c> / <c>0x00b430f0</c> / <c>0x00b433d0</c>):
/// routing around obstacles, walking up to a blocked/occupied target and stopping adjacent, ignoring adjacent blocked clicks,
/// and enforcing the depth cap of 25 steps.
/// </summary>
public sealed class FieldPathfinderTests
{
    /// <summary>Builds a map from ASCII rows: '.' = walkable, '#' = blocked.</summary>
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

    [Fact]
    public void Routes_around_a_vertical_wall_to_reach_the_other_side()
    {
        var collision = Grid(
            ".....",
            "..#..",
            "..#..",
            "..#..",
            ".....");
        var pathfinder = new FieldPathfinder(collision);
        var entity = new FieldEntity(1, 2);
        entity.SetDestination(3, 2);

        for (int step = 0; step < 100 && entity.IsMoving; step++)
        {
            entity.StepPhysics(pathfinder.ResolveStep);
            Assert.True(collision.IsWalkable(entity.CellX, entity.CellY));
        }

        Assert.Equal((3, 2), (entity.CellX, entity.CellY));
        Assert.Equal(MoveState.Standing, entity.State);
    }

    [Fact]
    public void Clicking_a_blocked_cell_far_away_walks_up_next_to_it_and_stops()
    {
        var collision = Grid(
            ".....",
            "....#",
            ".....");
        var pathfinder = new FieldPathfinder(collision);
        var entity = new FieldEntity(0, 1);
        entity.SetDestination(4, 1);            // (4, 1) is blocked

        for (int step = 0; step < 80 && entity.IsMoving; step++)
            entity.StepPhysics(pathfinder.ResolveStep);

        Assert.Equal((3, 1), (entity.CellX, entity.CellY));
        Assert.Equal(MoveState.Standing, entity.State);
        Assert.Equal(6, entity.Direction);      // ✅ 0x008671c0: still faces the blocked target cell (E = 6)
    }

    [Fact]
    public void Clicking_an_adjacent_blocked_or_occupied_cell_returns_no_step()
    {
        var collision = Grid(
            "...",
            ".#.",
            "...");
        collision.Occupy(2, 1, new Serial(1, 2));
        var pathfinder = new FieldPathfinder(collision);

        Assert.Null(pathfinder.NextStep(0, 1, 1, 1));       // adjacent wall
        Assert.Null(pathfinder.NextStep(2, 2, 2, 1));       // adjacent occupied cell
    }

    [Fact]
    public void Unreachable_target_walled_off_returns_null()
    {
        var collision = Grid(
            "..#..",
            "..#..",
            "..#..");
        var pathfinder = new FieldPathfinder(collision);

        Assert.Null(pathfinder.NextStep(0, 1, 4, 1));
    }

    [Fact]
    public void Target_beyond_max_depth_25_returns_null()
    {
        var row = new string('.', 35);
        var collision = Grid(row);
        var pathfinder = new FieldPathfinder(collision);

        Assert.NotNull(pathfinder.NextStep(0, 0, 24, 0));   // depth 24 < 25 -> found
        Assert.Null(pathfinder.NextStep(0, 0, 26, 0));      // depth 26 >= 25 -> cut off (0x00b430f0)
    }
}
