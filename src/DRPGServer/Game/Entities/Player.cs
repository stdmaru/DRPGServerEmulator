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

    /// <summary>Envia o estado completo do inventário (aba Cards + aba Items + Bits, <c>0xFC</c>).</summary>
    public void RefreshInventory() => Send(new InventoryRefreshPacket(Character));

    /// <summary>
    /// Entrega um item na aba <b>Items</b> (uso fora de batalha), enviando <c>0xC9</c> (e <c>0xFC</c> se <paramref name="refresh"/>).
    /// Devolve <c>false</c> sem alterar o inventário se não houver espaço suficiente.
    /// </summary>
    public bool GiveItem(uint itemId, uint amount = 1, bool refresh = true) =>
        GiveItems([(itemId, amount)], refresh);

    /// <summary>
    /// Entrega um lote de itens na aba <b>Items</b> (uso fora de batalha), enviando um único <c>0xC9</c>
    /// (e <c>0xFC</c> se <paramref name="refresh"/>). Devolve <c>false</c> sem alterar nada se faltar espaço.
    /// </summary>
    public bool GiveItems(IReadOnlyList<(uint ItemId, uint Amount)> items, bool refresh = true)
    {
        var bag = Character.Inventory.Main;
        foreach (var (itemId, amount) in items)
            if (bag.Room(itemId) < amount)
                return false;

        var added = new List<InventoryItem>();
        foreach (var (itemId, amount) in items)
            added.AddRange(bag.Add(itemId, amount));

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

    public override string ToString() => $"{Character.Name}({Account.Username})";
}
