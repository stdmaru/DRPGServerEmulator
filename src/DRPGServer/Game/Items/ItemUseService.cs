using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;

namespace DRPGServer.Game.Items;

/// <summary>
/// Uso de item da mochila pelos efeitos tipados de <see cref="Data.GameData.ItemInfo.UseEffects"/>. Todos os efeitos
/// são validados antes de o item ser consumido; cada pacote de entrada mantém a sequência oficial de resposta.
/// </summary>
public sealed class ItemUseService(Taming taming, InteractionGuard guard, GameWorld world)
{
    /// <summary>
    /// ✅ <c>0x41</c> com alvo selvagem. Recusa: <c>0xC3</c> sem consumir. Validado: consome, aplica e, se o efeito
    /// falhar depois disso (rede que não pega), <c>0xC3</c>.
    /// </summary>
    public void UseOnTarget(Player player, uint itemUid, Serial target)
    {
        if (player.Character.Inventory.Main.FindByUid(itemUid) is not { } item || item.Amount == 0)
        {
            player.Send(new ItemUseFailedPacket());
            return;
        }
        var effects = item.Info?.UseEffects ?? [];
        if (effects.Count == 0 || effects.Any(e => e.Entry != ItemUseEntry.OnTarget))
        {
            player.SendDebug($"Item {item.ItemId} (uid={item.Uid}) has no use handler");
            player.Send(new ItemUseFailedPacket());     // ❓ outros itens (consumíveis, tickets) ainda não
            return;
        }

        var ctx = Context(player, item, player.Zone?.FindWild(target), 0, 0);
        if (!effects.All(e => e.CanApply(ctx)))
        {
            player.Send(new ItemUseFailedPacket());
            return;
        }

        player.Character.Inventory.Main.Consume(item, 1);
        player.RequestImmediateSave();
        foreach (var effect in effects)
        {
            if (!effect.Apply(ctx))
            {
                player.Send(new ItemUseFailedPacket());
                return;
            }
        }
    }

    /// <summary>
    /// ✅ <c>0x85</c> numa posição (conn=125/234/469). Pedido inválido é ignorado em silêncio. Validado: aplica,
    /// ecoa <c>0x85</c> com a quantidade de antes, consome e manda <c>0xC9</c> com a quantidade nova.
    /// </summary>
    public void UseAtPosition(Player player, uint place, uint itemUid, uint itemId, int x, int y)
    {
        if (place != (uint)ItemPlace.Inventory) return;
        var bag = player.Character.Inventory.Main;
        if (bag.FindByUid(itemUid) is not { } item || item.ItemId != itemId || item.Amount == 0) return;
        var effects = item.Info?.UseEffects ?? [];
        if (effects.Count == 0 || effects.Any(e => e.Entry != ItemUseEntry.AtPosition)) return;

        var ctx = Context(player, item, null, x, y);
        if (!effects.All(e => e.CanApply(ctx))) return;

        foreach (var effect in effects)
            effect.Apply(ctx);
        player.Send(new ItemTeleportPacket(x, y, item));
        bag.Consume(item, 1);
        player.RequestImmediateSave();
        player.Send(new ItemReceivePacket(item));
    }

    private ItemUseContext Context(Player player, InventoryItem item, WildDigimon? wild, int x, int y) =>
        new(player, item, wild, x, y, taming, guard, world.Random, world.Now);
}
