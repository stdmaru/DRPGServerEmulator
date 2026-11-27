using DRPG.Client.World;
using DRPG.Shared.Formats.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Tests;

/// <summary>
/// Stage 2 collision tests: bounds (<c>0x00b35f80</c>), static walkability byte <c>+0x14</c> (<c>0x00b35f40</c>),
/// occupancy grid <c>(x, y)</c> + <c>(x, y − 1)</c> (<c>0x00b36550</c> / <c>0x00b365c0</c>), free check (<c>0x00b35b30</c>)
/// and step/target rules (<c>0x00b432d0</c>, <c>0x00b4d290</c>).
/// </summary>
public sealed class FieldCollisionTests
{
    private static MapFile MakeMap(int width, int height, uint[] flags)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((uint)width);
        w.Write((uint)height);
        w.Write(64u);
        w.Write(32u);
        for (int i = 0; i < width * height; i++)
        {
            w.Write(0u);
            w.Write(-1);
            w.Write(-1);
            w.Write((short)-1);
            w.Write((short)0);
            w.Write(0u);
            w.Write(i < flags.Length ? flags[i] : 1u);
        }
        w.Write((ushort)0);
        w.Write((ushort)0);
        return MapFile.Parse(ms.ToArray());
    }

    [Fact]
    public void Bounds_and_walkability_check_byte_0x14_only()
    {
        // (0,0) blocked (0), (1,0) walkable (1), (2,0) mirrored but blocked (0x300), (0,1) non-zero low byte (2)
        var map = MakeMap(3, 2, [0u, 1u, 0x300u, 2u, 0x101u, 0u]);
        var collision = new FieldCollision(map);

        Assert.False(collision.InBounds(-1, 0));
        Assert.False(collision.InBounds(0, -1));
        Assert.False(collision.InBounds(3, 0));
        Assert.False(collision.InBounds(0, 2));
        Assert.True(collision.InBounds(2, 1));

        Assert.False(collision.IsWalkable(0, 0));
        Assert.True(collision.IsWalkable(1, 0));
        Assert.False(collision.IsWalkable(2, 0));       // flip bits in +0x15 do not make a cell walkable
        Assert.True(collision.IsWalkable(0, 1));        // any non-zero byte in +0x14 is walkable (0x00b35f40)
        Assert.True(collision.IsWalkable(1, 1));
        Assert.False(collision.IsWalkable(-1, 0));
    }

    [Fact]
    public void Occupy_marks_cell_and_upper_cell_if_empty_and_vacate_clears_matching_serial()
    {
        var map = MakeMap(4, 4, Enumerable.Repeat(1u, 16).ToArray());
        var collision = new FieldCollision(map);
        var a = new Serial(1, 10);
        var b = new Serial(2, 20);

        collision.Occupy(2, 2, a);
        Assert.Equal(a, collision.OccupantAt(2, 2));
        Assert.Equal(a, collision.OccupantAt(2, 1));    // (x, y - 1) also occupied when empty
        Assert.False(collision.IsFree(2, 2));
        Assert.False(collision.IsFree(2, 1));
        Assert.True(collision.IsWalkable(2, 2));        // static walkability is unchanged

        // Another entity occupying (2, 3) must not overwrite a's cell at (2, 2)
        collision.Occupy(2, 3, b);
        Assert.Equal(b, collision.OccupantAt(2, 3));
        Assert.Equal(a, collision.OccupantAt(2, 2));

        // Vacating (2, 3) clears (2, 3) only and leaves a at (2, 2) untouched
        collision.Vacate(2, 3);
        Assert.Equal(default, collision.OccupantAt(2, 3));
        Assert.Equal(a, collision.OccupantAt(2, 2));

        // Vacating (2, 2) clears both (2, 2) and (2, 1)
        collision.Vacate(2, 2);
        Assert.True(collision.IsFree(2, 2));
        Assert.True(collision.IsFree(2, 1));
    }

    [Fact]
    public void Pathfinder_allows_target_cell_but_step_stops_before_blocked_or_occupied_target()
    {
        // Row 0: (0,0) free, (1,0) free, (2,0) wall, (3,0) free but occupied
        var map = MakeMap(4, 1, [1u, 1u, 0u, 1u]);
        var collision = new FieldCollision(map);
        collision.Occupy(3, 0, new Serial(9, 9));

        // Target cell itself is always passable to the pathfinder (0x00b432d0)
        Assert.True(collision.CanPathThrough(2, 0, destX: 2, destY: 0));
        Assert.False(collision.CanPathThrough(2, 0, destX: 3, destY: 0));

        // Actually stepping onto the final cell requires IsFree (0x00b35b30)
        Assert.False(collision.CanStepTo(2, 0, destX: 2, destY: 0));    // wall
        Assert.False(collision.CanStepTo(3, 0, destX: 3, destY: 0));    // occupied
        Assert.True(collision.CanStepTo(1, 0, destX: 1, destY: 0));     // free
        Assert.True(collision.CanStepTo(3, 0, destX: 1, destY: 0));     // occupied intermediate cell is statically walkable
    }

    [Fact]
    public void Walking_entity_stops_before_a_wall()
    {
        // 5x1 strip: (0..2, 0) walkable, (3, 0) wall, (4, 0) walkable
        var map = MakeMap(5, 1, [1u, 1u, 1u, 0u, 1u]);
        var collision = new FieldCollision(map);
        var entity = new FieldEntity(0, 0);
        entity.SetDestination(4, 0);

        PathStep? Next(int fx, int fy, int dx, int dy)
        {
            int nx = fx + Math.Sign(dx - fx), ny = fy + Math.Sign(dy - fy);
            return collision.CanStepTo(nx, ny, dx, dy) ? new PathStep(nx, ny, BlockedAtTarget: false) : null;
        }

        for (int step = 0; step < 40; step++)
            entity.StepPhysics(Next);

        Assert.Equal((2, 0), (entity.CellX, entity.CellY));
        Assert.Equal(MoveState.Standing, entity.State);
    }
}
