using DRPG.Client.Startup;
using DRPG.Client.World;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Features.Field;

/// <summary>
/// The map server's packets of the field, one method each, applied to <see cref="FieldState"/> (✅ <c>0x00abc350</c> dispatch:
/// <c>0x11</c> <c>0x00af25c0</c>, <c>0x09</c> <c>0x00ac7331</c>, <c>0xB0</c> <c>0x00acb072</c>). The server may lie: ids, cells and
/// directions are checked before they are used.
/// </summary>
public sealed class FieldHandlers(FieldState state, FieldActions actions, NpcTable npcs)
{
    /// <summary>✅ <c>0x00af25c0</c>: <c>0x11</c> is ignored on map <c>0xFA</c> (250).</summary>
    public const int WildlessMapId = 0xFA;

    /// <summary>✅ <c>0x00af25c0</c>: state 12 removes the group at once.</summary>
    public const byte WildLeavingState = 12;

    /// <summary>Applies <paramref name="packet"/>; false when it is not a field packet.</summary>
    public bool Handle(object packet)
    {
        switch (packet)
        {
            case FieldDigimonPacket wild:
                Handle(wild);
                return true;
            case FieldNpcPacket npc:
                Handle(npc);
                return true;
            case TeleportCharacterPacket teleport:
                Handle(teleport);
                return true;
            default:
                return false;
        }
    }

    /// <summary>✅ <c>0x00af25c0</c>: removes the group on state 12; otherwise finds or creates it and queues the command.</summary>
    public void Handle(FieldDigimonPacket packet)
    {
        var area = state.Area;
        if (area.MapId == WildlessMapId)
            return;
        if (packet.State == WildLeavingState)
        {
            area.Wilds.Remove(packet.Serial);
            return;
        }

        var cell = new Cell(packet.X, packet.Y);
        bool hasDirection = Directions.TryFrom(packet.Direction, out var direction);
        var wild = area.Wilds.Find(packet.Serial)
            ?? area.Wilds.Add(new WildSpawn(packet.Serial, packet.Name, packet.SpeciesId, packet.Stage, packet.Level, packet.Grade, cell,
                hasDirection ? direction : Direction.South));
        // ✅ 0x007f3b30: the command waits in the queue and is read at a cell centre (FieldWilds.Step)
        wild.Entity.EnqueueCommand(new MoveCommand((MoveState)packet.State, cell, new Cell(packet.DestX, packet.DestY),
            hasDirection ? direction : null));
    }

    /// <summary>✅ <c>0x00ac7331</c> → <c>0x00abb9e0</c> (add) / <c>0x00acfed0</c> (remove): a quest NPC appears or goes.</summary>
    public void Handle(FieldNpcPacket packet)
    {
        var area = state.Area;
        switch (packet.Action)
        {
            case FieldNpcPacket.ActionAdd:
                if (packet.MapId != area.MapId || npcs.Find(packet.NpcId) is null)
                    return;
                var cell = new Cell(packet.X, packet.Y);
                if (state.QuestNpcs.Add(new QuestNpc(packet.MapId, packet.NpcId, cell)))
                    area.Npcs.SpawnQuestNpc(packet.NpcId, cell);
                break;
            case FieldNpcPacket.ActionRemove:
                if (packet.MapId == FieldNpcPacket.AllMaps || packet.MapId == area.MapId)
                    area.Npcs.RemoveAll(packet.NpcId);
                state.QuestNpcs.Remove(packet.MapId, packet.NpcId);
                break;
        }
    }

    /// <summary>
    /// ✅ <c>0x00acb072</c>: on the same map the tamer and the partner are placed on the cell; another map starts a map change
    /// (<c>0x00af0d70</c>). Either way the queued <c>0x20</c> is dropped.
    /// </summary>
    public void Handle(TeleportCharacterPacket packet)
    {
        var cell = new Cell(packet.X, packet.Y);
        if (packet.MapId == state.Area.MapId)
            state.PlaceParty(cell);
        else
            state.Transition.Begin(new MapTarget(packet.MapId, cell));
        actions.CancelMoves();
    }
}
