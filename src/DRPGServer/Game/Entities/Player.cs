using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Game.Entities;

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

    /// <summary>Hora (do mundo) do último 0x20 aceito ou do último teleporte.</summary>
    public TimeSpan LastMoveAt { get; internal set; }

    public void Send(ServerPacket packet) => Session.Send(packet);

    public override string ToString() => $"{Character.Name}({Account.Username})";
}
