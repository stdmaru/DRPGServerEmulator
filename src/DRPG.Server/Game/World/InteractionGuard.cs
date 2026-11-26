using DRPG.Server.Configuration;
using DRPG.Server.Data.GameData;
using DRPG.Server.Game.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DRPG.Server.Game.World;

/// <summary>
/// Alcance e contexto de toda ação com alvo no mundo (docs/emulador/seguranca.md#interação-e-alcance): o alvo existe,
/// está no mapa do jogador e dentro da distância daquela interação. A distância é em casas, no maior eixo
/// (<c>max(|dx|, |dy|)</c>). Thread do mundo. Recusa vai para o log: o cliente legítimo só manda essas ações de perto.
/// </summary>
public sealed class InteractionGuard(GameData data, GameWorld world, IOptions<GameOptions> options, ILogger<InteractionGuard> log)
{
    private int NpcRange => options.Value.NpcInteractionRange;
    private int WildRange => options.Value.WildInteractionRange;
    private int DropRange => options.Value.DropPickupRange;

    /// <summary>Loja de NPC: a loja pertence a um NPC deste mapa, e ele está perto.</summary>
    public bool CanUseShop(Player player, uint shopId)
    {
        var owners = data.Npcs.OwnersOf(shopId);
        if (owners.Any(n => IsNear(player, n)))
            return true;
        Reject(player, $"loja {shopId}", owners.Count == 0 ? "sem NPC dono" : "longe do dono");
        return false;
    }

    /// <summary>Digimon Center (Digimon Depository): algum NPC dessa janela no mapa está perto.</summary>
    public bool CanUseDigimonCenter(Player player)
    {
        if (data.Npcs.On(player.Character.MapId).Any(n => n.Window == NpcWindow.DigimonCenter && IsNear(player, n)))
            return true;
        Reject(player, "Digimon Center", "nenhum por perto");
        return false;
    }

    /// <summary>Escolha de diálogo: o NPC dela está neste mapa e perto.</summary>
    public bool CanTalkTo(Player player, byte mapId, ushort npcId)
    {
        if (data.Npcs.Find(mapId, npcId) is { } npc && IsNear(player, npc))
            return true;
        Reject(player, $"NPC {mapId}:{npcId}", player.Character.MapId != mapId ? "outro mapa" : "longe");
        return false;
    }

    /// <summary>Batalha ou Taming Net num selvagem da zona do jogador: ele pode batalhar e está perto.</summary>
    public bool CanEngage(Player player, WildDigimon wild)
    {
        if (wild.CanBattle && Distance(player, wild.X, wild.Y) <= WildRange)
            return true;
        Reject(player, $"selvagem {wild.Leader.Name}", wild.CanBattle ? $"a {Distance(player, wild.X, wild.Y)} casas" : $"estado {wild.State}");
        return false;
    }

    /// <summary>Coleta de item ou card do chão da zona do jogador: perto e, na janela do dono, só ele.</summary>
    public bool CanPickUp(Player player, ItemDrop drop)
    {
        int distance = Distance(player, drop.X, drop.Y);
        bool free = drop.IsFreeFor(player.Character.Uid, world.Now);
        if (free && distance <= DropRange)
            return true;
        Reject(player, $"drop {drop.Uid} ({drop.X}, {drop.Y})", free ? $"a {distance} casas" : $"do personagem {drop.OwnerCharacterUid}");
        return false;
    }

    private bool IsNear(Player player, NpcInfo npc) =>
        npc.MapId == player.Character.MapId && Distance(player, npc.X, npc.Y) <= NpcRange;

    private static int Distance(Player player, short x, short y) =>
        Math.Max(Math.Abs(player.Character.X - x), Math.Abs(player.Character.Y - y));

    private void Reject(Player player, string target, string reason) =>
        log.LogWarning("{Player} em {Map} ({X}, {Y}): {Target} recusado, {Reason}",
            player, player.Character.MapId, player.Character.X, player.Character.Y, target, reason);
}
