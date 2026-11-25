using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Game.Entities;

/// <summary>✅ Estado do tamer no offset 42 do 0x10 (mesmos códigos do 0x11 dos selvagens).</summary>
public enum TamerState : byte
{
    Idle = 1,
    Moving = 5,
    InBattle = 11,
}

/// <summary>Personagem online no mapa. Vive na thread do mundo.</summary>
public sealed class Player(
    Session session,
    AuthenticatedAccount account,
    Character character,
    ICashStore? cashStore = null,
    GameWorld? world = null)
{
    public Session Session { get; } = session;
    public AuthenticatedAccount Account { get; } = account;
    public Character Character { get; } = character;
    public Digimon Leader => Character.Leader;

    public Zone? Zone { get; internal set; }
    public Battle.Battle? Battle { get; internal set; }
    public bool InBattle => Battle is not null;
    public bool IsOnline => !Session.IsClosed;
    public bool IsAdmin => Account.Authority >= Authority.Administrator;

    /// <summary>
    /// Próxima recuperação automática (hora do mundo). ✅ Parada na batalha; o oficial cura logo no fim
    /// (0,04–0,34 s depois do 0x6D, ainda na tela de resultado) e segue de 5,2 em 5,2 s.
    /// </summary>
    public TimeSpan NextRecoveryAt { get; internal set; }

    /// <summary>Hora (do mundo) do último 0x20 aceito ou do último teleporte.</summary>
    public TimeSpan LastMoveAt { get; internal set; }

    /// <summary>Persistência híbrida: true se houve alteração desde o último snapshot enviado ao banco.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Persistência híbrida: true em eventos críticos (itens, captura, batalha, depository) para salvar já no próximo tick.</summary>
    public bool NeedsImmediateSave { get; private set; }

    /// <summary>Hora (do mundo) em que o último snapshot deste jogador foi enfileirado.</summary>
    public TimeSpan LastSavedAt { get; internal set; }

    /// <summary>Marca alteração de baixo risco (movimento, slots internos, pontos) para o próximo ciclo de autosave.</summary>
    public void MarkDirty() => IsDirty = true;

    /// <summary>Agenda gravação imediata na fila de persistência (eventos críticos / anti-duplicação).</summary>
    public void RequestImmediateSave()
    {
        IsDirty = true;
        NeedsImmediateSave = true;
    }

    internal void MarkSaved(TimeSpan now)
    {
        IsDirty = false;
        NeedsImmediateSave = false;
        LastSavedAt = now;
    }

    public void Send(ServerPacket packet) => Session.Send(packet);

    /// <summary>Envia mensagem de chat normal (<c>0x21</c>) somente para este jogador.</summary>
    public void SendMessage(string sender, string message) => Send(new ChatNormalPacket(sender, message));

    /// <summary>Envia mensagem colorida (<c>0x22</c>) somente para este jogador.</summary>
    public void SendNotice(string sender, string message) => Send(new ChatNoticePacket(sender, message));

    /// <summary>Envia echo de debug colorido (<c>0x22</c>) somente se a conta tiver autoridade de administrador.</summary>
    public void SendDebug(string message)
    {
        if (!IsAdmin) return;
        SendNotice("[DEBUG]", message);
    }

    /// <summary>Envia o estado completo do inventário (aba Cards + aba Items + Bits, <c>0xFC</c>).</summary>
    public void RefreshInventory() => Send(new InventoryRefreshPacket(Character));

    /// <summary>
    /// Entrega um item na aba <b>Items</b> (uso fora de batalha), enviando <c>0xC9</c> (e <c>0xFC</c> se <paramref name="refresh"/>).
    /// Para itens não empilháveis (<c>MaxStack == 1</c>), <paramref name="amountOrDuration"/> aceita:
    /// <c>-1</c> (permanente), <c>0</c>/<c>1</c> (duração padrão do <c>ItemInfo</c>) ou <c>&gt; 1</c> (duração arbitrária em segundos, ex.: <c>86400</c> = 1 dia).
    /// Para itens empilháveis, representa a quantidade. Devolve <c>false</c> sem alterar o inventário se não houver espaço ou valor inválido.
    /// </summary>
    public bool GiveItem(uint itemId, int amountOrDuration = 1, bool refresh = true) =>
        GiveItems([(itemId, amountOrDuration)], refresh);

    /// <summary>
    /// Entrega um lote de itens na aba <b>Items</b> (uso fora de batalha), enviando um único <c>0xC9</c>
    /// (e <c>0xFC</c> se <paramref name="refresh"/>). Suporta períodos arbitrários em segundos ou permanentes (<c>-1</c>)
    /// para itens não empilháveis (<c>MaxStack == 1</c>): ex. <c>player.GiveItems([(718, 86400), (718, -1)])</c>.
    /// </summary>
    public bool GiveItems(IReadOnlyList<(uint ItemId, int AmountOrDuration)> items, bool refresh = true)
    {
        var bag = Character.Inventory.Main;
        if (!TryResolveItems(items, out var resolved)) return false;
        if (!HasRoomForBatch(bag, resolved.Select(r => (r.ItemId, r.Amount)), isCard: false)) return false;

        var added = new List<InventoryItem>();
        foreach (var (itemId, amount, duration) in resolved)
            added.AddRange(bag.Add(itemId, amount, duration));

        if (added.Count > 0) Send(new ItemReceivePacket(added));
        if (refresh) RefreshInventory();
        return true;
    }

    /// <summary>
    /// Entrega um card na aba <b>Cards</b> (uso em batalha), enviando <c>0xC8</c> (e <c>0xFC</c> se <paramref name="refresh"/>).
    /// Devolve <c>false</c> sem alterar o inventário se não houver espaço suficiente.
    /// </summary>
    public bool GiveCard(uint cardId, uint amount = 1, bool refresh = true) =>
        GiveCards([(cardId, amount)], refresh);

    /// <summary>
    /// Entrega um lote de cards na aba <b>Cards</b> (uso em batalha), enviando um único <c>0xC8</c>
    /// (e <c>0xFC</c> se <paramref name="refresh"/>). Devolve <c>false</c> sem alterar nada se faltar espaço.
    /// </summary>
    public bool GiveCards(IReadOnlyList<(uint CardId, uint Amount)> cards, bool refresh = true)
    {
        var bag = Character.Inventory.Battle;
        if (cards.Any(c => c.Amount == 0)) return false;
        if (!HasRoomForBatch(bag, cards, isCard: true)) return false;

        var added = new List<InventoryItem>();
        foreach (var (cardId, amount) in cards)
            added.AddRange(bag.Add(cardId, amount));

        if (added.Count > 0) Send(new BattleItemReceivePacket(added));
        if (refresh) RefreshInventory();
        return true;
    }

    /// <summary>
    /// Deposita um item diretamente no <b>Shop Depository</b> (<c>cash_depository_item</c>) da conta do jogador,
    /// fora da thread do mundo, invocando <paramref name="onComplete"/> de volta na thread do mundo com <c>true</c>
    /// em caso de sucesso ou <c>false</c> se o depósito estiver cheio / indisponível.
    /// Reutilizável por lojas NPC (ex.: loja 5050 de Coins), eventos, missões e comandos GM.
    /// </summary>
    public void SendToShopDepository(uint itemId, uint quantity = 1, uint expiresAt = 0, Action<bool>? onComplete = null)
    {
        if (quantity == 0)
        {
            onComplete?.Invoke(false);
            return;
        }

        uint maxStack = Math.Max(1u, Character.Inventory.MaxStackOf(itemId, isCard: false));
        var entries = new List<(CashShopItemKind Kind, uint ItemId, uint Amount, uint ExpiresAt)>();
        uint remaining = quantity;
        while (remaining > 0)
        {
            uint chunk = Math.Min(remaining, maxStack);
            entries.Add((CashShopItemKind.Item, itemId, chunk, expiresAt));
            remaining -= chunk;
        }

        SendBatchToShopDepository(entries, onComplete);
    }

    /// <summary>
    /// Deposita um card diretamente no <b>Shop Depository</b> (<c>cash_depository_item</c>) da conta do jogador.
    /// </summary>
    public void SendCardToShopDepository(uint cardId, uint quantity = 1, Action<bool>? onComplete = null)
    {
        if (quantity == 0)
        {
            onComplete?.Invoke(false);
            return;
        }

        uint maxStack = Math.Max(1u, Character.Inventory.MaxStackOf(cardId, isCard: true));
        var entries = new List<(CashShopItemKind Kind, uint ItemId, uint Amount, uint ExpiresAt)>();
        uint remaining = quantity;
        while (remaining > 0)
        {
            uint chunk = Math.Min(remaining, maxStack);
            entries.Add((CashShopItemKind.Card, cardId, chunk, 0u));
            remaining -= chunk;
        }

        SendBatchToShopDepository(entries, onComplete);
    }

    /// <summary>
    /// Deposita um lote de itens/cards no <b>Shop Depository</b> (<c>cash_depository_item</c>) da conta do jogador,
    /// executando I/O fora da thread do mundo e devolvendo o resultado via <paramref name="onComplete"/> na thread do mundo.
    /// </summary>
    public void SendBatchToShopDepository(
        IReadOnlyList<(CashShopItemKind Kind, uint ItemId, uint Amount, uint ExpiresAt)> entries,
        Action<bool>? onComplete = null)
    {
        if (cashStore is null || entries.Count == 0)
        {
            onComplete?.Invoke(false);
            return;
        }

        _ = DepositAsync(cashStore, world, Account.Uid, entries, onComplete);
    }

    private static async Task DepositAsync(
        ICashStore store,
        GameWorld? world,
        uint accountUid,
        IReadOnlyList<(CashShopItemKind Kind, uint ItemId, uint Amount, uint ExpiresAt)> entries,
        Action<bool>? onComplete)
    {
        bool deposited = false;
        try
        {
            deposited = await store.DepositAsync(accountUid, entries, CancellationToken.None);
        }
        catch
        {
            deposited = false;
        }

        if (onComplete is null) return;
        if (world is not null)
            world.Post(() => onComplete(deposited));
        else
            onComplete(deposited);
    }

    /// <summary>
    /// Troca atômica na aba <b>Items</b>: remove <paramref name="takeAmount"/> de <paramref name="takeItemId"/> e
    /// entrega <paramref name="giveItems"/> (aceitando o mesmo formato de <see cref="GiveItems"/>, incluindo
    /// <c>-1</c> para permanente e duração arbitrária em segundos para não empilháveis). Faz rollback se faltar espaço.
    /// Envia <c>0xC9</c> (posições alteradas) e <c>0xFC</c>. Opcionalmente executa <paramref name="beforeNotify"/>
    /// após a troca ser confirmada em memória e antes de enviar <c>0xC9</c>/<c>0xFC</c>.
    /// </summary>
    public bool ExchangeItems(
        uint takeItemId,
        uint takeAmount,
        IReadOnlyList<(uint ItemId, int AmountOrDuration)> giveItems,
        bool refresh = true,
        Action? beforeNotify = null)
    {
        var bag = Character.Inventory.Main;
        if (bag.Count(takeItemId) < takeAmount) return false;
        if (!TryResolveItems(giveItems, out var resolved)) return false;

        var removed = bag.Remove(takeItemId, takeAmount);
        if (!HasRoomForBatch(bag, resolved.Select(r => (r.ItemId, r.Amount)), isCard: false))
        {
            bag.Add(takeItemId, takeAmount);
            return false;
        }

        var added = new List<InventoryItem>();
        foreach (var (itemId, amount, duration) in resolved)
            added.AddRange(bag.Add(itemId, amount, duration));

        beforeNotify?.Invoke();

        // posição esvaziada e ocupada de novo na mesma troca: vale o último estado
        var changed = removed.Concat(added).GroupBy(i => i.Slot).Select(g => g.Last()).ToList();
        if (changed.Count > 0) Send(new ItemReceivePacket(changed));
        if (refresh) RefreshInventory();
        return true;
    }

    private bool TryResolveItems(
        IReadOnlyList<(uint ItemId, int AmountOrDuration)> items,
        out List<(uint ItemId, uint Amount, long? DurationSeconds)> resolved)
    {
        var inv = Character.Inventory;
        resolved = new List<(uint ItemId, uint Amount, long? DurationSeconds)>(items.Count);

        foreach (var (itemId, value) in items)
        {
            bool nonStackable = inv.FindInfo(itemId) is { MaxStack: <= 1 };
            if (nonStackable)
            {
                if (value < Inventory.PermanentDuration) return false;
                long? duration = value is 0 or 1 ? null : value;
                resolved.Add((itemId, 1u, duration));
            }
            else
            {
                if (value <= 0) return false;
                resolved.Add((itemId, (uint)value, null));
            }
        }
        return true;
    }

    private bool HasRoomForBatch(Bag bag, IEnumerable<(uint Id, uint Amount)> entries, bool isCard)
    {
        var inv = Character.Inventory;
        int freeSlots = bag.Capacity - bag.Items.Count();
        int newSlotsNeeded = 0;

        foreach (var group in entries.GroupBy(e => e.Id))
        {
            uint max = inv.MaxStackOf(group.Key, isCard);
            long totalAmt = group.Sum(x => (long)x.Amount);
            if (max <= 1)
            {
                newSlotsNeeded += (int)totalAmt;
                continue;
            }

            long existingRoom = bag.Items
                .Where(i => i.ItemId == group.Key)
                .Sum(i => (long)(max - Math.Min(max, i.Amount)));
            long overflow = Math.Max(0, totalAmt - existingRoom);
            newSlotsNeeded += (int)((overflow + max - 1) / max);
        }

        return newSlotsNeeded <= freeSlots;
    }

    public override string ToString() => $"{Character.Name}({Account.Username})";
}
