using DRPGServer.Game;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Handlers;

/// <summary>
/// 0x90 (C2S 186): ✅ mover, equipar, desequipar ou coletar card (<see cref="ItemMoveHeader"/> + registro de 76 B).
/// Places: 1 = chão, 2 = 6 Card Slots equipados (row 0, col 0..5), 3 = mochila de Cards (row 0..3, col 0..5).
/// Confirmado em conn=437, 442, 446: 3→2 (equipar), 2→3 (desequipar), 2→2, 3→3 e 1→3 (coletar).
/// </summary>
[PacketHandler(MapOpcode.CardMove)]
public sealed class CardMoveHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        var header = ItemMoveHeader.Read(packet);
        uint uid = packet.ReadU32();
        uint cardId = packet.ReadU32();
        if (player.InBattle) return;

        var cardBag = player.Character.Inventory.Battle;
        var cardSlots = player.Character.Inventory.CardSlots;

        switch (header.From, header.To)
        {
            case (ItemPlace.Ground, ItemPlace.Inventory):
            {
                if (player.Zone is not { } zone || zone.FindDrop(uid) is not { } drop || drop.ItemId != cardId) return;
                if (cardBag.Add(drop.ItemId, 1) is not [.., var card]) return;
                zone.RemoveDrop(drop);
                player.RequestImmediateSave();
                player.Send(new CardMovePacket(header with { ToA = card.Row, ToB = card.Column }, card));
                break;
            }
            case (ItemPlace.Inventory, ItemPlace.Inventory):
                MoveCard(player, header, uid, cardBag, ItemMoveHandler.SlotOf(header.FromA, header.FromB),
                    cardBag, ItemMoveHandler.SlotOf(header.ToA, header.ToB));
                break;
            case (ItemPlace.Inventory, ItemPlace.CardSlots):
                MoveCard(player, header, uid, cardBag, ItemMoveHandler.SlotOf(header.FromA, header.FromB),
                    cardSlots, CardSlotOf(header.ToA, header.ToB));
                break;
            case (ItemPlace.CardSlots, ItemPlace.Inventory):
                MoveCard(player, header, uid, cardSlots, CardSlotOf(header.FromA, header.FromB),
                    cardBag, ItemMoveHandler.SlotOf(header.ToA, header.ToB));
                break;
            case (ItemPlace.CardSlots, ItemPlace.CardSlots):
                MoveCard(player, header, uid, cardSlots, CardSlotOf(header.FromA, header.FromB),
                    cardSlots, CardSlotOf(header.ToA, header.ToB));
                break;
        }
    }

    private static void MoveCard(Player player, ItemMoveHeader header, uint uid,
        Bag fromBag, int fromSlot, Bag toBag, int toSlot)
    {
        if (fromBag[fromSlot] is not { } card || card.Uid != uid) return;
        if (!fromBag.MoveTo(fromSlot, toBag, toSlot, out var displaced)) return;
        player.MarkDirty();
        player.Send(new CardMovePacket(header, card, displaced));
    }

    internal static int CardSlotOf(int row, int column) =>
        row == 0 && column is >= 0 and < Inventory.CardSlotCount ? column : -1;
}

/// <summary>
/// 0x91 (C2S 250): ✅ mover item (<see cref="ItemMoveHeader"/> + registro do item). Do chão: origem (x, y) e destino
/// "qualquer lugar"; o oficial tira o item do chão (0x13 zerado com x, y) e responde com a posição usada. Dentro do
/// inventário: linha e coluna de origem e destino, trocando se o destino está ocupado.
/// </summary>
[PacketHandler(MapOpcode.ItemDropGet)]
public sealed class ItemMoveHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        var header = ItemMoveHeader.Read(packet);
        uint uid = packet.ReadU32();
        uint itemId = packet.ReadU32();
        if (player.InBattle) return;
        var bag = player.Character.Inventory.Main;

        switch (header.From, header.To)
        {
            case (ItemPlace.Ground, ItemPlace.Inventory):
            {
                if (player.Zone is not { } zone || zone.FindDrop(uid) is not { } drop || drop.ItemId != itemId) return;
                if (bag.Add(drop.ItemId, 1) is not [.., var item]) return;   // inventário cheio: fica no chão
                zone.RemoveDrop(drop);
                player.RequestImmediateSave();
                player.Send(new ItemMovePacket(header with { ToA = item.Row, ToB = item.Column }, item));
                break;
            }
            case (ItemPlace.Inventory, ItemPlace.Inventory):
            {
                int from = SlotOf(header.FromA, header.FromB), to = SlotOf(header.ToA, header.ToB);
                if (bag[from] is not { } item || item.Uid != uid) return;
                if (bag.Move(from, to, out var displaced))
                {
                    player.MarkDirty();
                    player.Send(new ItemMovePacket(header, item, displaced));
                }
                break;
            }
            // ❓ bolsas extras (0x33, 0x3D) ainda não
        }
    }

    internal static int SlotOf(int row, int column) =>
        row is >= 0 and < Inventory.Size / Inventory.Columns && column is >= 0 and < Inventory.Columns
            ? row * Inventory.Columns + column : -1;
}

/// <summary>0xBD (C2S 254): ✅ cabeçalho (inventário → chão, linha e coluna) + registro: o item é destruído.</summary>
[PacketHandler(MapOpcode.ItemDestroy)]
public sealed class ItemDestroyHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        var header = ItemMoveHeader.Read(packet);
        uint uid = packet.ReadU32();
        if (player.InBattle || header.From != ItemPlace.Inventory) return;
        var bag = player.Character.Inventory.Main;
        int slot = ItemMoveHandler.SlotOf(header.FromA, header.FromB);
        if (bag[slot] is not { } item || item.Uid != uid) return;
        bag.Destroy(slot);
        player.RequestImmediateSave();
        player.Send(new ItemDestroyPacket(header, item));
    }
}

/// <summary>
/// 0x148 (C2S 38): ✅ botão de auto-organizar inventário (DigimonRPG.bin @ 0x00B1310F / 0x00B13355 / 0x00AE3820).
/// Layout: u32 action (0), u32 0, u16 bagType (1 = inventário principal [aba 1, +0x1ED8], 2 = bolsa de batalha
/// [aba 0, +0x17B8]; 3/4 = warehouse). Ignorado em batalha; junta pilhas parciais, ordena os itens e devolve 0xFC.
/// </summary>
[PacketHandler(MapOpcode.InventorySort)]
public sealed class InventorySortHandler : PlayerHandler
{
    public const ushort MainInventoryType = 1;
    public const ushort BattleBagType = 2;

    protected override void Handle(Player player, PacketReader packet)
    {
        if (player.InBattle) return;
        uint action = packet.ReadU32();
        packet.ReadU32();
        ushort bagType = packet.ReadU16();
        if (action != 0) return;

        BagKind? bagKind = bagType switch
        {
            MainInventoryType => BagKind.Inventory,
            BattleBagType => BagKind.Battle,
            _ => null,
        };
        if (bagKind is null) return;

        player.Character.Inventory.BagOf(bagKind.Value).Sort();
        player.MarkDirty();
        player.Send(new InventoryRefreshPacket(player.Character));
    }
}

/// <summary>
/// 0x41 (C2S 822): ✅ usar item. u32 3, registro do item (uid em +0), bloco do digimon alvo (selvagem: uid 0 e o
/// serial dele em +4). Por ora só a Taming Net (<see cref="Taming"/>); o resto responde 0xC3.
/// </summary>
[PacketHandler(MapOpcode.ItemUse)]
public sealed class ItemUseHandler(Taming taming, GameWorld world) : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        packet.ReadU32();
        uint itemUid = packet.ReadU32();
        packet.Skip(ItemRecord.Length - 4);
        packet.ReadU32();                       // uid do alvo (0 no selvagem)
        var target = Serial.Read(packet);

        if (player.Character.Inventory.Main.FindByUid(itemUid) is not { } item)
        {
            player.Send(new ItemUseFailedPacket());
            return;
        }
        if (!taming.UseNet(player, item, player.Zone?.FindWild(target), world.Random))
        {
            player.SendDebug($"Item {item.ItemId} (uid={item.Uid}) has no use handler");
            player.Send(new ItemUseFailedPacket());     // ❓ outros itens (consumíveis, tickets) ainda não
        }
    }
}
