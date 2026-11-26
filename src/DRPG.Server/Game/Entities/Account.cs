namespace DRPG.Server.Game.Entities;

public enum Authority : byte
{
    None = 0,
    PrivilegedUser = 1,
    GameMaster = 2,
    Administrator = 3,
}

/// <summary>Conta já autenticada no channel. Imutável: pode circular entre threads e servidores.</summary>
public sealed record AuthenticatedAccount(uint Uid, string Username, Authority Authority);
