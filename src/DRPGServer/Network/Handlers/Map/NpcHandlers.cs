using DRPGServer.Game.Entities;
using DRPGServer.Game.Npcs;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Handlers;

/// <summary>0x56: escolha num diálogo de NPC (<see cref="NpcScriptRegistry"/>).</summary>
[PacketHandler(MapOpcode.NpcDialogChoice)]
public sealed class NpcChoiceHandler(NpcScriptRegistry scripts) : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        uint choice = packet.ReadU32();
        if (!player.InBattle && scripts.Choose(player, choice))
            player.RequestImmediateSave();
    }
}
