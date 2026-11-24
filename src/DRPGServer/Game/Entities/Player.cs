using DRPGServer.Game.World;
using DRPGServer.Network;
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

    public override string ToString() => $"{Character.Name}({Account.Username})";
}
