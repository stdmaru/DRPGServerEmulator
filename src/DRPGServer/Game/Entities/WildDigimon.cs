using DRPGServer.Data.GameData;
using DRPGServer.Game.Stats;

namespace DRPGServer.Game.Entities;

/// <summary>Estado do grupo no mapa. O valor no <c>0x11</c> está em <see cref="WildDigimon.StateCode"/>.</summary>
public enum WildState : byte
{
    Idle,
    Moving,
    InBattle,
    Dead,
    Gone,
}

/// <summary>Um membro do grupo selvagem com as recompensas dele.</summary>
public sealed record WildMember(Digimon Digimon, SpawnOption Option);

/// <summary>
/// Grupo de digimons selvagens no mapa. O primeiro membro é o que aparece. Anda só em volta da casa
/// (docs/mecanicas/selvagens.md).
/// </summary>
public sealed class WildDigimon
{
    /// <summary>✅ Cada selvagem fica numa caixa de 7 × 7 centrada na casa (192 grupos, sessões 53–58).</summary>
    public const int RoamRadius = 3;

    public WildDigimon(GameData data, SpawnArea spawn, short homeX, short homeY)
    {
        Spawn = spawn;
        Members = spawn.Options.Select(o => new WildMember(CreateMember(data, o), o)).ToList();
        HomeX = X = DestX = homeX;
        HomeY = Y = DestY = homeY;
    }

    /// <summary>Digimon novo com os dados de uma opção de spawn (membro do grupo ou cópia para uma batalha).</summary>
    public static Digimon CreateMember(GameData data, SpawnOption o) =>
        new(data, data.Species[o.SpeciesId], o.Level, o.Stats ?? WildFormulas.Combat(o.Level));

    public Serial Serial { get; } = Serial.New();
    public SpawnArea Spawn { get; }
    public IReadOnlyList<WildMember> Members { get; }
    public Digimon Leader => Members[0].Digimon;
    public short HomeX { get; }
    public short HomeY { get; }
    public short X { get; private set; }
    public short Y { get; private set; }
    /// <summary>Destino do movimento em curso (igual à posição quando parado).</summary>
    public short DestX { get; private set; }
    public short DestY { get; private set; }
    public Direction Facing { get; private set; } = Direction.South;
    public WildState State { get; set; } = WildState.Idle;
    public bool CanBattle => State is WildState.Idle or WildState.Moving;
    public TimeSpan DiedAt { get; set; }
    /// <summary>Índice do grupo na área (a casa volta para o mesmo slot no respawn).</summary>
    public int Slot { get; init; }

    /// <summary>✅ Estado no 0x11 (v322): 1 parado, 4 andando, 11 em batalha, 8 derrotado, 12 saindo do mapa.</summary>
    public byte StateCode => State switch
    {
        WildState.Moving => 4,
        WildState.InBattle => 11,
        WildState.Dead => 8,
        WildState.Gone => 12,
        _ => 1,
    };

    /// <summary>Começa a andar até (x, y): o 0x11 sai com a posição atual e o destino.</summary>
    public void StartMove(short x, short y)
    {
        DestX = x;
        DestY = y;
        State = WildState.Moving;
    }

    /// <summary>
    /// Chega ao destino. ✅ A direção nova só aparece no 0x11 da chegada (96 % de 2896 movimentos).
    /// Continua <see cref="WildState.Moving"/> por mais um tick, como no oficial.
    /// </summary>
    public void Arrive()
    {
        Facing = FacingOf(DestX - X, DestY - Y, Facing);
        X = DestX;
        Y = DestY;
    }

    public void Stop() => State = WildState.Idle;

    /// <summary>✅ 0 = +y, 1 = (−x, +y), 2 = −x, 3 = (−x, −y), 4 = −y, 5 = (+x, −y), 6 = +x, 7 = (+x, +y).</summary>
    public static Direction FacingOf(int dx, int dy, Direction current) => (Math.Sign(dx), Math.Sign(dy)) switch
    {
        (0, 1) => Direction.South,
        (-1, 1) => Direction.SouthWest,
        (-1, 0) => Direction.West,
        (-1, -1) => Direction.NorthWest,
        (0, -1) => Direction.North,
        (1, -1) => Direction.NorthEast,
        (1, 0) => Direction.East,
        (1, 1) => Direction.SouthEast,
        _ => current,
    };

    /// <summary>Volta ao mapa com tudo cheio (fim de batalha sem morrer).</summary>
    public void Reset()
    {
        State = WildState.Idle;
        DestX = X;
        DestY = Y;
        foreach (var m in Members) m.Digimon.RestoreAll();
    }
}
