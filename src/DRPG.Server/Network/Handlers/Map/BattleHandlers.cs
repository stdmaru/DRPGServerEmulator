using DRPG.Server.Game.Battle;
using DRPG.Server.Game.Battle.Cards;
using DRPG.Server.Game.Entities;
using DRPG.Server.Network.Packets.Map;

namespace DRPG.Server.Network.Handlers;

/// <summary>0x61: o jogador encostou num digimon selvagem.</summary>
[PacketHandler(MapOpcode.BattleStartReq)]
[RequiresState(PlayerStates.Idle)]
public sealed class BattleStartHandler(BattleSystem battles) : PlayerHandler<BattleStartRequest>
{
    protected override void Handle(Player player, BattleStartRequest request) => battles.StartPve(player, request.Wild);
}

/// <summary>0x62 (C2S): o cliente confirma o serial da batalha; o servidor manda os times.</summary>
[PacketHandler(MapOpcode.BattleStartRes)]
[RequiresState(PlayerStates.InBattle)]
public sealed class BattleConfirmHandler : PlayerHandler<BattleConfirmRequest>
{
    protected override void Handle(Player player, BattleConfirmRequest request)
    {
        if (player.Battle is { } battle && battle.Serial == request.Battle)
            battle.SendTeams(player);
    }
}

/// <summary>0x69: o cliente está pronto (no começo e depois de cada animação).</summary>
[PacketHandler(MapOpcode.BattleBegin)]
[RequiresState(PlayerStates.InBattle)]
public sealed class BattleReadyHandler : PlayerHandler<BattleReadyRequest>
{
    protected override void Handle(Player player, BattleReadyRequest request) => player.Battle!.ClientReady(player);
}

/// <summary>0x35: escolher (ou desmarcar) a ação de um digimon.</summary>
[PacketHandler(MapOpcode.BattleActionReq)]
[RequiresState(PlayerStates.InBattle)]
public sealed class BattleActionHandler : PlayerHandler<BattleActionRequest>
{
    protected override void Handle(Player player, BattleActionRequest request)
    {
        if (request.Action != BattleAction.Choose) return;
        player.Battle!.RequestAction(player, request.Icon, request.Requester, request.Target);
    }
}

/// <summary>0xA0: ✅ equipa um card dos 6 CardSlots no slot de batalha 0..2 (sem resposta S2C).</summary>
[PacketHandler(MapOpcode.BattleCardEquip)]
[RequiresState(PlayerStates.InBattle)]
public sealed class BattleCardEquipHandler : PlayerHandler<BattleCardEquipRequest>
{
    protected override void Handle(Player player, BattleCardEquipRequest request)
    {
        if (request.Slot >= CardBattleState.SlotCount) return;
        player.Battle!.EquipCard(player, (int)request.Slot, request.Uid, request.CardId);
    }
}

/// <summary>0xA1: ✅ desequipa o card do slot de batalha 0..2 (sem resposta S2C).</summary>
[PacketHandler(MapOpcode.BattleCardUnequip)]
[RequiresState(PlayerStates.InBattle)]
public sealed class BattleCardUnequipHandler : PlayerHandler<BattleCardUnequipRequest>
{
    protected override void Handle(Player player, BattleCardUnequipRequest request)
    {
        if (request.Slot >= CardBattleState.SlotCount) return;
        player.Battle!.UnequipCard(player, (int)request.Slot);
    }
}
