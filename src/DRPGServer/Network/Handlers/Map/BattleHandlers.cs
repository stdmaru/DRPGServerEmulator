using DRPGServer.Game;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Handlers;

/// <summary>0x61: o jogador encostou num digimon selvagem.</summary>
[PacketHandler(MapOpcode.BattleStartReq)]
public sealed class BattleStartHandler(BattleSystem battles) : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        packet.ReadI32();
        battles.StartPve(player, Serial.Read(packet));
    }
}

/// <summary>0x62 (C2S): o cliente confirma o serial da batalha; o servidor manda os times.</summary>
[PacketHandler(MapOpcode.BattleStartRes)]
public sealed class BattleConfirmHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        var serial = Serial.Read(packet);
        if (player.Battle is { } battle && battle.Serial == serial)
            battle.SendTeams(player);
    }
}

/// <summary>0x69: o cliente está pronto (no começo e depois de cada animação).</summary>
[PacketHandler(MapOpcode.BattleBegin)]
public sealed class BattleReadyHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet) => player.Battle?.ClientReady(player);
}

/// <summary>0x35 (50 bytes): u8 ação (2), u16 ícone (0x0A ataque, 0x04 fuga), u8 0, quem age, alvo.</summary>
[PacketHandler(MapOpcode.BattleActionReq)]
public sealed class BattleActionHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        if (packet.ReadU8() != (byte)BattleAction.Choose) return;
        var icon = (BattleIcon)packet.ReadU16();
        packet.ReadU8();
        var requester = Serial.Read(packet);
        var target = Serial.Read(packet);
        player.Battle?.RequestAction(player, icon, requester, target);
    }
}

/// <summary>0xA0 (C2S 98 B): ✅ equipa um card dos 6 CardSlots no slot de batalha 0..2 (sem resposta S2C).</summary>
[PacketHandler(MapOpcode.BattleCardEquip)]
public sealed class BattleCardEquipHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        uint slot = packet.ReadU32();
        uint uid = packet.ReadU32();
        uint cardId = packet.ReadU32();
        player.Battle?.EquipCard(player, (int)slot, uid, cardId);
    }
}

/// <summary>0xA1 (C2S 98 B): ✅ desequipa o card do slot de batalha 0..2 (sem resposta S2C).</summary>
[PacketHandler(MapOpcode.BattleCardUnequip)]
public sealed class BattleCardUnequipHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        uint slot = packet.ReadU32();
        player.Battle?.UnequipCard(player, (int)slot);
    }
}
