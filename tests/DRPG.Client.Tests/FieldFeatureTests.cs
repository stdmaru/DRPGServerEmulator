using DRPG.Client.Features.Field;
using DRPG.Client.World;
using DRPG.Shared.Packets.Map;

namespace DRPG.Client.Tests;

/// <summary>
/// The field feature slice without a scene: the <c>0xB0</c> handler (<c>0x00acb072</c>), the ground click actions
/// (<c>0x00b4c1a0</c> / <c>0x00b4d290</c>) and the map change phases (<c>0x00af0d70</c> → <c>0x00aba410</c>).
/// </summary>
public sealed class FieldFeatureTests
{
    [Fact]
    public void Same_map_0xB0_places_the_party_and_drops_the_queued_move()
    {
        using var field = new FieldFixture(FieldFixture.Open(20, 20), tamer: new Cell(5, 5));
        field.Actions.PressGround(new Cell(9, 5));
        Assert.True(field.Actions.HasPendingMove);
        Assert.True(field.Tamer.IsMoving);

        Assert.True(field.Handlers.Handle((object)new TeleportCharacterPacket(79, 12, 3)));

        Assert.Equal(new Cell(12, 3), field.Tamer.Cell);
        Assert.False(field.Tamer.IsMoving);
        Assert.False(field.Actions.HasPendingMove);
        Assert.Equal(FieldFixture.TamerSerial, field.Collision.OccupantAt(12, 3));
        Assert.Equal(default, field.Collision.OccupantAt(5, 5));
        Assert.Equal(MapTransitionPhase.None, field.State.Transition.Phase);
    }

    [Fact]
    public void Other_map_0xB0_locks_input_waits_for_the_stop_then_plays_the_warp()
    {
        using var field = new FieldFixture(FieldFixture.Open(20, 20), tamer: new Cell(5, 5));
        field.Actions.PressGround(new Cell(7, 5));

        field.Handlers.Handle(new TeleportCharacterPacket(46, 15, 15));

        var transition = field.State.Transition;
        Assert.Equal(MapTransitionPhase.WaitingForStop, transition.Phase);
        Assert.True(transition.LocksInput);
        Assert.Equal(new MapTarget(46, new Cell(15, 15)), transition.Target);
        Assert.False(field.Actions.HasPendingMove);

        // the tamer finishes its walk first (✅ 0x00af0d70 +0x771 = 1)
        field.State.Step();
        Assert.Equal(MapTransitionPhase.WaitingForStop, transition.Phase);
        for (int i = 0; i < 100 && transition.Phase == MapTransitionPhase.WaitingForStop; i++)
            field.State.Step();

        Assert.False(field.Tamer.IsMoving);
        Assert.Equal(new Cell(7, 5), field.Tamer.Cell);
        // without effect_map.spr the warp ends at once and the map is ready to load
        Assert.Equal(MapTransitionPhase.ReadyToLoad, transition.Phase);
        transition.Complete();
        Assert.False(transition.LocksInput);
    }

    [Fact]
    public void Holding_the_mouse_on_one_cell_asks_once_and_a_blocked_target_is_not_sent()
    {
        using var field = new FieldFixture(
        [
            "..........",
            "......#...",
            "..........",
        ], tamer: new Cell(2, 1));

        field.Actions.PressGround(new Cell(4, 1));
        Assert.True(field.Actions.HasPendingMove);
        Assert.Equal(new Cell(4, 1), field.Tamer.Destination);

        // the 0x20 goes out (the session is not in the world here, so nothing is written); holding the same cell asks nothing more
        field.Actions.Update(nowMs: 1000);
        Assert.False(field.Actions.HasPendingMove);
        field.Actions.PressGround(new Cell(4, 1));
        Assert.False(field.Actions.HasPendingMove);

        // released and pressed again: asked again
        field.Actions.ReleaseGround();
        field.Actions.PressGround(new Cell(4, 1));
        Assert.True(field.Actions.HasPendingMove);

        // ✅ 0x00b4d290: a click on the wall next to the tamer is blocked at the target: no walk, no 0x20
        field.Actions.CancelMoves();
        field.State.PlaceParty(new Cell(5, 1));
        field.Actions.PressGround(new Cell(6, 1));
        Assert.False(field.Actions.HasPendingMove);
        Assert.False(field.Tamer.IsMoving);

        // a cell outside the map is never asked
        field.Actions.ReleaseGround();
        field.Actions.PressGround(new Cell(50, 1));
        Assert.False(field.Actions.HasPendingMove);
    }

    [Fact]
    public void Packets_outside_the_field_are_not_handled()
    {
        using var field = new FieldFixture(FieldFixture.Open(3, 3), tamer: new Cell(0, 0));
        Assert.False(field.Handlers.Handle(new MoveCharacterRequest(1, 1)));
    }
}

public sealed class FieldHoverTests
{
    [Fact]
    public void The_entity_under_the_mouse_is_the_occupant_of_its_cell_or_the_one_below()
    {
        using var field = new FieldFixture(FieldFixture.Open(10, 10), new DRPG.Client.World.Cell(4, 4));
        field.State.Hover(new DRPG.Client.World.Cell(4, 4));
        Assert.Equal(FieldFixture.TamerSerial, field.State.Hovered);
        field.State.Hover(new DRPG.Client.World.Cell(4, 3));                  // ✅ 0x00b36550: the cell above is the tamer's too
        Assert.Equal(FieldFixture.TamerSerial, field.State.Hovered);
        field.State.Hover(new DRPG.Client.World.Cell(7, 7));
        Assert.Equal(default, field.State.Hovered);
        field.State.Hover(null);                                               // the mouse is on the HUD or a window
        Assert.Equal(default, field.State.Hovered);
    }
}
