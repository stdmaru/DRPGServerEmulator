using DRPGServer.Game;
using DRPGServer.Game.Economy;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;

namespace DRPGServer.Network.Handlers;

/// <summary>
/// 0x90 (C2S 186): ✅ mover, equipar, desequipar ou coletar card (<see cref="ItemMoveHeader"/> + registro de 76 B).
/// Places: 1 = chão, 2 = 6 Card Slots equipados (row 0, col 0..5), 3 = mochila de Cards (row 0..3, col 0..5).
/// Confirmado em conn=437, 442, 446: 3→2 (equipar), 2→3 (desequipar), 2→2, 3→3 e 1→3 (coletar).
/// </summary>
[PacketHandler(MapOpcode.CardMove)]
[RequiresState(PlayerStates.Idle)]
public sealed class CardMoveHandler(InteractionGuard guard) : PlayerHandler<CardMoveRequest>
{
    protected override void Handle(Player player, CardMoveRequest request)
    {
        var (header, uid, cardId) = request;
        var cardBag = player.Character.Inventory.Battle;
        var cardSlots = player.Character.Inventory.CardSlots;

        switch (header.From, header.To)
        {
            case (ItemPlace.Ground, ItemPlace.Inventory):
            {
                if (player.Zone is not { } zone || zone.FindDrop(uid) is not { IsCard: true } drop || drop.ItemId != cardId
                    || !guard.CanPickUp(player, drop)) return;
                if (cardBag.Add(drop.ItemId, 1) is not [.., var card]) return;
                zone.RemoveDrop(drop);
                player.Character.Economy.RecordItems(Pickup(drop), [(drop.ItemId, 1)], cards: true);
                player.RequestImmediateSave();
                player.Send(new CardMovePacket(header with { ToA = card.Row, ToB = card.Column }, card));
                break;
            }
            case (ItemPlace.Inventory, ItemPlace.Inventory):
                MoveCard(player, header, uid, cardId, cardBag, ItemMoveHandler.SlotOf(header.FromA, header.FromB),
                    cardBag, ItemMoveHandler.SlotOf(header.ToA, header.ToB));
                break;
            case (ItemPlace.Inventory, ItemPlace.CardSlots):
                MoveCard(player, header, uid, cardId, cardBag, ItemMoveHandler.SlotOf(header.FromA, header.FromB),
                    cardSlots, CardSlotOf(header.ToA, header.ToB));
                break;
            case (ItemPlace.CardSlots, ItemPlace.Inventory):
                MoveCard(player, header, uid, cardId, cardSlots, CardSlotOf(header.FromA, header.FromB),
                    cardBag, ItemMoveHandler.SlotOf(header.ToA, header.ToB));
                break;
            case (ItemPlace.CardSlots, ItemPlace.CardSlots):
                MoveCard(player, header, uid, cardId, cardSlots, CardSlotOf(header.FromA, header.FromB),
                    cardSlots, CardSlotOf(header.ToA, header.ToB));
                break;
        }
    }

    private static void MoveCard(Player player, ItemMoveHeader header, uint uid, uint cardId,
        Bag fromBag, int fromSlot, Bag toBag, int toSlot)
    {
        if (fromBag[fromSlot] is not { } card || card.Uid != uid || card.ItemId != cardId) return;
        if (!fromBag.MoveTo(fromSlot, toBag, toSlot, out var displaced)) return;
        player.MarkDirty();
        player.Send(new CardMovePacket(header, card, displaced));
    }

    internal static EconomyReason Pickup(ItemDrop drop) =>
        new(EconomySource.DropPickup, "pickup", $"drop {drop.Uid} dono {drop.OwnerCharacterUid}");

    internal static int CardSlotOf(int row, int column) =>
        row == 0 && column is >= 0 and < Inventory.CardSlotCount ? column : -1;
}

/// <summary>
/// 0x91 (C2S 250): ✅ mover item (<see cref="ItemMoveHeader"/> + registro do item). Do chão: origem (y, x) e destino
/// "qualquer lugar"; o oficial tira o item do chão (0x13 zerado com x, y) e responde com a posição usada. Dentro do
/// inventário: linha e coluna de origem e destino, trocando se o destino está ocupado. A coleta passa pelo
/// <see cref="InteractionGuard.CanPickUp"/> (distância e janela do dono).
/// </summary>
[PacketHandler(MapOpcode.ItemDropGet)]
[RequiresState(PlayerStates.Idle)]
public sealed class ItemMoveHandler(InteractionGuard guard) : PlayerHandler<ItemMoveRequest>
{
    protected override void Handle(Player player, ItemMoveRequest request)
    {
        var (header, uid, itemId) = request;
        var bag = player.Character.Inventory.Main;
        long now = player.Character.Inventory.Clock.UnixNow;
        var equipment = player.Character.Inventory.Equipment;

        if (header.From == ItemPlace.Ground && header.To == ItemPlace.Inventory)
        {
            if (player.Zone is not { } zone || zone.FindDrop(uid) is not { IsCard: false } drop || drop.ItemId != itemId
                || !guard.CanPickUp(player, drop)) return;
            if (bag.Add(drop.ItemId, 1) is not [.., var item]) return;   // inventário cheio: fica no chão
            zone.RemoveDrop(drop);
            player.Character.Economy.RecordItems(CardMoveHandler.Pickup(drop), [(drop.ItemId, 1)], cards: false);
            player.RequestImmediateSave();
            player.Send(new ItemMovePacket(header with { ToA = item.Row, ToB = item.Column }, item));
            return;
        }

        if (header.From == ItemPlace.Inventory && header.To == ItemPlace.Inventory)
        {
            int from = SlotOf(header.FromA, header.FromB), to = SlotOf(header.ToA, header.ToB);
            if (bag[from] is not { } item || item.Uid != uid || item.ItemId != itemId || item.IsExpired(now)) return;
            if (bag.Move(from, to, out var displaced))
            {
                player.MarkDirty();
                player.Send(new ItemMovePacket(header, item, displaced));
            }
            return;
        }

        // ✅ Equipar item: Inventário (3) → Slot de equipamento (4..9, 11, 12, 14..28, 81..86)
        if (header.From == ItemPlace.Inventory && EquipmentSlots.IsEquipmentPlace(header.To))
        {
            if (header.ToA != 0 || header.ToB != 0) return;
            int fromSlot = SlotOf(header.FromA, header.FromB);
            if (bag[fromSlot] is not { } item || item.Uid != uid || item.ItemId != itemId) return;
            if (!EquipmentSlots.CanEquip(header.To, item, player.Character.Level, now)) return;

            if (!bag.MoveTo(fromSlot, equipment, (int)header.To, out var displaced)) return;
            ApplyEquipmentChange(player, header, item, displaced);
            return;
        }

        // ✅ Desequipar item: Slot de equipamento → Inventário (3)
        if (EquipmentSlots.IsEquipmentPlace(header.From) && header.To == ItemPlace.Inventory)
        {
            if (header.FromA != 0 || header.FromB != 0) return;
            int toSlot = SlotOf(header.ToA, header.ToB);
            if (toSlot < 0) return;
            if (equipment[(int)header.From] is not { } item || item.Uid != uid || item.ItemId != itemId) return;

            // Se o slot de destino no inventário já está ocupado, só permite troca se o item deslocado puder ir para o slot de equipamento
            if (bag[toSlot] is { } existing && !EquipmentSlots.CanEquip(header.From, existing, player.Character.Level, now))
                return;

            if (!equipment.MoveTo((int)header.From, bag, toSlot, out var displaced)) return;
            ApplyEquipmentChange(player, header, item, displaced);
            return;
        }

        // ✅ Mover/trocar entre dois slots de equipamento compatíveis (ex.: Crest 1 ↔ Crest 2, Digimental 1 ↔ 3)
        if (EquipmentSlots.IsEquipmentPlace(header.From) && EquipmentSlots.IsEquipmentPlace(header.To))
        {
            if (header.From == header.To || header.FromA != 0 || header.FromB != 0 || header.ToA != 0 || header.ToB != 0) return;
            if (equipment[(int)header.From] is not { } item || item.Uid != uid || item.ItemId != itemId) return;
            if (!EquipmentSlots.CanEquip(header.To, item, player.Character.Level, now)) return;
            if (equipment[(int)header.To] is { } existing && !EquipmentSlots.CanEquip(header.From, existing, player.Character.Level, now))
                return;

            if (!equipment.Move((int)header.From, (int)header.To, out var displaced)) return;
            ApplyEquipmentChange(player, header, item, displaced);
        }
    }

    /// <summary>
    /// ✅ Sequência oficial ao equipar/desequipar (captures.db conn=234 pid=115773..115778):
    /// 0xB1 (Tamer + equipamentos) → 0xB4 (um por Digimon do time com status recalculados) → 0x10 → 0x91.
    /// </summary>
    private static void ApplyEquipmentChange(Player player, ItemMoveHeader header, InventoryItem moved, InventoryItem? displaced)
    {
        var c = player.Character;
        c.SyncEquipmentBonuses();
        // Ao desequipar uma Aura que mantém evolução, Digimons evoluídos no mapa voltam para a forma-base
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
        player.Send(new ItemMovePacket(header, moved, displaced));
    }

    internal static int SlotOf(int row, int column) =>
        row is >= 0 and < Inventory.Size / Inventory.Columns && column is >= 0 and < Inventory.Columns
            ? row * Inventory.Columns + column : -1;
}

/// <summary>0xBD (C2S 254): ✅ cabeçalho (inventário → chão, linha e coluna) + registro: o item é destruído.</summary>
[PacketHandler(MapOpcode.ItemDestroy)]
[RequiresState(PlayerStates.Idle)]
public sealed class ItemDestroyHandler : PlayerHandler<ItemDestroyRequest>
{
    protected override void Handle(Player player, ItemDestroyRequest request)
    {
        var (header, uid) = request;
        if (header.From != ItemPlace.Inventory) return;
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
[RequiresState(PlayerStates.Idle)]
public sealed class InventorySortHandler : PlayerHandler<InventorySortRequest>
{
    public const ushort MainInventoryType = 1;
    public const ushort BattleBagType = 2;

    protected override void Handle(Player player, InventorySortRequest request)
    {
        if (request.Action != 0) return;

        BagKind? bagKind = request.BagType switch
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
/// serial dele em +4). Por ora só a Taming Net (<see cref="Taming"/>); o resto responde 0xC3. Em batalha também 0xC3.
/// </summary>
[PacketHandler(MapOpcode.ItemUse)]
[RequiresState(PlayerStates.Idle)]
public sealed class ItemUseHandler(Taming taming, GameWorld world) : PlayerHandler<ItemUseRequest>
{
    protected override void Handle(Player player, ItemUseRequest request)
    {
        var (itemUid, _, target) = request;
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

    protected override void OnRejected(Player player, ItemUseRequest request) => player.Send(new ItemUseFailedPacket());
}

/// <summary>
/// 0x85 (C2S 138): ✅ teleporte imediato no mesmo mapa usando item 900 (Teleport) ou 907 (Teleport X).
/// Layout: i32 X, i32 Y, u32 place (3), ItemRecord (108 B), 18 zeros (conn=125/234/469).
/// Resposta oficial: 0x10 (broadcast na zona) → 0x85 (124 B) → 0xC9 (604 B com quantidade atualizada).
/// </summary>
[PacketHandler(MapOpcode.ItemTeleport)]
[RequiresState(PlayerStates.Idle)]
public sealed class ItemTeleportHandler(GameWorld world) : PlayerHandler<ItemTeleportRequest>
{
    public const uint TeleportItemId = 900;
    public const uint TeleportXItemId = 907;

    protected override void Handle(Player player, ItemTeleportRequest request)
    {
        var (targetX, targetY, place, uid, itemId) = request;
        if (player.Zone is not { } zone) return;
        if (place != (uint)ItemPlace.Inventory) return;
        if (itemId is not (TeleportItemId or TeleportXItemId)) return;
        if (targetX < short.MinValue || targetX > short.MaxValue || targetY < short.MinValue || targetY > short.MaxValue) return;
        if (zone.Grid is { } grid && !grid.IsWalkable(targetX, targetY)) return;

        var bag = player.Character.Inventory.Main;
        if (bag.FindByUid(uid) is not { } item || item.ItemId != itemId || item.Amount == 0) return;

        player.Character.X = (short)targetX;
        player.Character.Y = (short)targetY;
        player.LastMoveAt = world.Now;

        zone.Broadcast(new RefreshCharacterInfoPacket(player.Character));
        player.Send(new ItemTeleportPacket(targetX, targetY, item));
        bag.Consume(item, 1);
        player.RequestImmediateSave();
        player.Send(new ItemReceivePacket(item));
    }
}
