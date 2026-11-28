using DRPG.Client.Data;
using DRPG.Client.World;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Features.Field;

/// <summary>
/// The field as the client mirrors it (✅ <c>CSceneGame</c> field, <c>0x00b4b550</c> / <c>0x00b4e550</c>, docs/cliente/campo.md):
/// the loaded map with its entities (<see cref="Area"/>), the player's tamer and digimon (<see cref="Party"/>), the quest NPCs
/// that outlive a map, and the map change in progress. Only <see cref="FieldHandlers"/> and <see cref="FieldActions"/> change it;
/// the scene steps it and <see cref="FieldView"/> reads it.
/// </summary>
public sealed class FieldState
{
    public FieldState(FieldArea area, FieldParty party, IFieldSprites sprites)
    {
        Party = party;
        Warp = new WarpEffect(sprites);
        Area = area;
        EnterArea(area, party.Tamer.Cell);
    }

    public FieldArea Area { get; private set; }
    public FieldParty Party { get; }
    public QuestNpcs QuestNpcs { get; } = new();
    public MapTransition Transition { get; } = new();

    /// <summary>
    /// ✅ <c>DAT_0135b254</c>: the entity under the mouse, set each frame by <c>0x00b4dd90</c> (<see cref="Hover"/>); none
    /// (<c>default</c>) when the mouse is off the ground. Its name plate and the leader's HP bar are <see cref="EntityPlates"/>; ❓ the
    /// attack cursor the original picks for it (<c>0x00a0d340</c>, <c>+0x25c</c>) comes with its step.
    /// </summary>
    public Serial Hovered { get; private set; }

    /// <summary>
    /// ✅ <c>0x00b4dd90</c>: while the ground is the mouse's hot control (not the HUD nor a window), the occupant of the cell under the
    /// mouse in the occupancy grid (an entity takes its cell and the one above, <c>0x00b36550</c>); otherwise none.
    /// </summary>
    public void Hover(Cell? cellUnderMouse) =>
        Hovered = cellUnderMouse is { } cell ? Area.Collision.OccupantAt(cell.X, cell.Y) : default;
    public WarpEffect Warp { get; }

    /// <summary>
    /// ✅ <c>0x00ad3730</c> / <c>0x00aba410</c>: a map was loaded: its table NPCs and the quest NPCs recorded for it spawn, then the
    /// tamer and the partner are placed on <paramref name="cell"/> and occupy their cells.
    /// </summary>
    public void EnterArea(FieldArea area, Cell cell)
    {
        Area = area;
        area.Npcs.SpawnFromTable();
        foreach (var npc in QuestNpcs.All)
            if (npc.MapId == area.MapId)
                area.Npcs.SpawnQuestNpc(npc.NpcId, npc.Cell);
        Party.Tamer.PlaceAt(cell);
        Party.Partner?.PlaceAt(FieldParty.PartnerCell(cell));
        Party.Occupy(area.Collision);
    }

    /// <summary>
    /// ✅ <c>0x00b4d290</c>: local prediction of a ground click, as the original: the tamer walks at once when the first step toward
    /// <paramref name="target"/> exists and is not the blocked target itself. False when it does not walk.
    /// </summary>
    public bool TryWalkTo(Cell target)
    {
        var tamer = Party.Tamer;
        if (!Area.Contains(target) || Area.Pathfinder.ResolveStep(tamer.Cell, target) is not { BlockedAtTarget: false })
            return false;
        tamer.SetDestination(target);
        return true;
    }

    /// <summary>✅ <c>0x00acb072</c>: an <c>0xB0</c> on the same map: the tamer and the partner are placed on <paramref name="cell"/>.</summary>
    public void PlaceParty(Cell cell) => Party.PlaceAt(cell, Area.Collision);

    /// <summary>✅ <c>0x00afdba0</c> + <c>0x00afe1c0</c>: one logic step: the party, the wilds and the NPCs move, then the map change goes on.</summary>
    public void Step()
    {
        Party.Step(Area.Collision, Area.Pathfinder);
        Area.Wilds.Step(Party.Tamer);
        Area.Npcs.Step();
        Transition.Step(Party.Tamer, Warp);
    }
}
