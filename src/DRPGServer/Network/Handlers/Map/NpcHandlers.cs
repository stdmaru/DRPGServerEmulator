using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Handlers;

/// <summary>0x56: escolha num diálogo de NPC (<see cref="NpcScripts"/>).</summary>
[PacketHandler(MapOpcode.NpcDialogChoice)]
public sealed class NpcChoiceHandler(NpcScripts scripts) : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        uint choice = packet.ReadU32();
        if (!player.InBattle) scripts.Choose(player, choice);
    }
}
