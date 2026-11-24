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
public sealed class Player(Session session, AuthenticatedAccount account, Character character)
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
        var inv = Character.Inventory;
        var bag = inv.Main;
        var resolved = new List<(uint ItemId, uint Amount, long? DurationSeconds)>(items.Count);

        foreach (var (itemId, value) in items)
        {
            var info = inv.FindInfo(itemId);
            bool nonStackable = info is { MaxStack: <= 1 };
            if (nonStackable)
            {
                if (value < Inventory.PermanentDuration) return false;
                long? duration = value switch
                {
                    Inventory.PermanentDuration => Inventory.PermanentDuration,
                    0 or 1 => null,
                    _ => value,
                };
                resolved.Add((itemId, 1u, duration));
            }
            else
            {
                if (value <= 0) return false;
                resolved.Add((itemId, (uint)value, null));
            }
        }

        // Verifica espaço total necessário antes de mutar o inventário
        int freeSlotsNeeded = 0;
        foreach (var group in resolved.GroupBy(r => r.ItemId))
        {
            uint totalAmt = (uint)group.Sum(x => (long)x.Amount);
            if (bag.Room(group.Key) < totalAmt) return false;
            if (inv.FindInfo(group.Key) is { MaxStack: <= 1 })
                freeSlotsNeeded += (int)totalAmt;
        }
        if (freeSlotsNeeded > bag.Capacity - bag.Items.Count()) return false;

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
    public bool GiveCard(uint itemId, uint amount = 1, bool refresh = true)
    {
        var bag = Character.Inventory.Battle;
        if (bag.Room(itemId) < amount) return false;

        var added = bag.Add(itemId, amount);
        if (added.Count > 0) Send(new BattleItemReceivePacket(added));
        if (refresh) RefreshInventory();
        return true;
    }

    /// <summary>
    /// Troca atômica na aba <b>Items</b>: remove <paramref name="takeAmount"/> de <paramref name="takeItemId"/> e
    /// entrega <paramref name="giveItems"/>, fazendo rollback se faltar espaço. Envia <c>0xC9</c> (posições alteradas)
    /// e <c>0xFC</c>. Opcionalmente executa <paramref name="beforeNotify"/> após a troca ser confirmada em memória
    /// e antes de enviar <c>0xC9</c>/<c>0xFC</c>.
    /// </summary>
    public bool ExchangeItems(
        uint takeItemId,
        uint takeAmount,
        IReadOnlyList<(uint ItemId, uint Amount)> giveItems,
        bool refresh = true,
        Action? beforeNotify = null)
    {
        var bag = Character.Inventory.Main;
        if (bag.Count(takeItemId) < takeAmount) return false;

        var removed = bag.Remove(takeItemId, takeAmount);
        var added = new List<InventoryItem>();
        foreach (var (itemId, amount) in giveItems)
        {
            if (bag.Room(itemId) < amount)
            {
                foreach (var item in added)
                    bag.Remove(item.ItemId, item.Amount);
                bag.Add(takeItemId, takeAmount);
                return false;
            }
            added.AddRange(bag.Add(itemId, amount));
        }

        beforeNotify?.Invoke();

        // posição esvaziada e ocupada de novo na mesma troca: vale o último estado
        var changed = removed.Concat(added).GroupBy(i => i.Slot).Select(g => g.Last()).ToList();
        if (changed.Count > 0) Send(new ItemReceivePacket(changed));
        if (refresh) RefreshInventory();
        return true;
    }

    public override string ToString() => $"{Character.Name}({Account.Username})";
}
