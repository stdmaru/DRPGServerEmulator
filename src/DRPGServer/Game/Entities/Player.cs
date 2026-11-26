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

/// <summary>Estado do jogador para as pré-condições dos handlers (<c>[RequiresState]</c>).</summary>
[Flags]
public enum PlayerStates
{
    /// <summary>No mapa, livre para agir.</summary>
    Idle = 1,
    /// <summary>Numa batalha.</summary>
    InBattle = 2,
    /// <summary>
    /// Esperando uma transferência conta ↔ personagem gravar (<see cref="World.CharacterTransfers"/>). Nenhuma outra ação
    /// de economia é aceita até ela terminar.
    /// </summary>
    Transacting = 4,
    Any = Idle | InBattle | Transacting,
}

/// <summary>Personagem online no mapa. Vive na thread do mundo.</summary>
public sealed class Player(
    Session session,
    AuthenticatedAccount account,
    Character character)
{
    public Session Session { get; } = session;
    public AuthenticatedAccount Account { get; } = account;
    public Character Character { get; } = character;
    public Digimon Leader => Character.Leader;

    public Zone? Zone { get; internal set; }
    public Battle.Battle? Battle { get; internal set; }
    public bool InBattle => Battle is not null;
    public PlayerStates State => _transfer is not null ? PlayerStates.Transacting
        : InBattle ? PlayerStates.InBattle
        : PlayerStates.Idle;

    private TaskCompletionSource? _transfer;

    /// <summary>
    /// Transferência em andamento (null se nenhuma). Completa na thread do mundo, depois de o commit ou o rollback ter
    /// sido aplicado ao personagem.
    /// </summary>
    public Task? PendingTransfer => _transfer?.Task;

    internal void BeginTransfer()
    {
        if (_transfer is not null) throw new InvalidOperationException($"{this}: transferência já em andamento");
        _transfer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal void EndTransfer()
    {
        var transfer = _transfer;
        _transfer = null;
        transfer?.TrySetResult();
    }
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

    public override string ToString() => $"{Character.Name}({Account.Username})";
}
