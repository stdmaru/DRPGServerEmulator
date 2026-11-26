using DRPGServer.Game.Entities;
using DRPGServer.Network.Packets.Map;

namespace DRPGServer.Game.Items;

/// <summary>
/// O único ponto dos efeitos de uma troca de equipamento: equipar, desequipar, trocar de posição (0x91) ou vencer o
/// prazo (tick da zona). Sem estado nem dependências.
/// </summary>
public static class EquipmentService
{
    /// <summary>
    /// O equipamento de <paramref name="player"/> mudou. Recalcula os bônus, devolve à forma-base os Digimons evoluídos
    /// se nenhuma peça mantém a evolução (Aura), agenda a gravação e manda a sequência oficial: ✅ 0xB1 (tamer e
    /// equipamentos) → 0xB4 (um por Digimon do time) → 0x10 para a zona (captures.db conn=234 pid=115773..115778).
    /// Quem chama manda depois o pacote da própria ação (0x91 ao mover).
    /// </summary>
    public static void Changed(Player player)
    {
        var c = player.Character;
        c.SyncEquipmentBonuses();
        if (!c.TotalEquipmentBonus.MaintainEvolution)
        {
            foreach (var digimon in c.Digimons)
                if (digimon.IsEvolved)
                    digimon.Devolve();
        }
        player.RequestImmediateSave();
        player.Send(new TamerExpPacket(c));
        foreach (var digimon in c.Digimons)
            player.Send(new BattleDigimonStatusPacket(digimon));
        if (player.Zone is { } zone)
            zone.Broadcast(new RefreshCharacterInfoPacket(c));
        else
            player.Send(new RefreshCharacterInfoPacket(c));
    }

    /// <summary>
    /// Tira os itens de prazo vencido da mochila (0xFC) e do equipamento (<see cref="Changed"/>). Fora de batalha.
    /// </summary>
    public static void RemoveExpired(Player player, long unixNow)
    {
        var inventory = player.Character.Inventory;
        int expiredBag = inventory.Main.RemoveExpired(unixNow);
        int expiredEquipment = inventory.Equipment.RemoveExpired(unixNow);
        if (expiredBag == 0 && expiredEquipment == 0) return;

        player.RequestImmediateSave();
        if (expiredBag > 0)
            player.RefreshInventory();
        if (expiredEquipment > 0)
            Changed(player);
    }
}
