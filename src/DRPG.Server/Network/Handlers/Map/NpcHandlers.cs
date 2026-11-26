using DRPG.Server.Game.Entities;
using DRPG.Server.Game.Npcs;
using DRPG.Server.Network.Packets.Map;

namespace DRPG.Server.Network.Handlers;

/// <summary>0x56: escolha num diálogo de NPC (<see cref="NpcScriptRegistry"/>).</summary>
[PacketHandler(MapOpcode.NpcDialogChoice)]
[RequiresState(PlayerStates.Idle)]
public sealed class NpcChoiceHandler(NpcScriptRegistry scripts) : PlayerHandler<NpcChoiceRequest>
{
    protected override void Handle(Player player, NpcChoiceRequest request)
    {
        if (scripts.Choose(player, request.Choice))
            player.RequestImmediateSave();
    }
}
