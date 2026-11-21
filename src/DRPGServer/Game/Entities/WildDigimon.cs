using DRPGServer.Data.GameData;
using DRPGServer.Game.Stats;

namespace DRPGServer.Game.Entities;

public enum WildState : byte
{
    Roaming,
    InBattle,
    Dead,
}

/// <summary>Um membro do grupo selvagem com as recompensas dele.</summary>
public sealed record WildMember(Digimon Digimon, SpawnOption Option);

/// <summary>Grupo de digimons selvagens andando no mapa. O primeiro membro é o que aparece.</summary>
public sealed class WildDigimon
{
    public WildDigimon(GameData data, SpawnArea spawn, Random random)
    {
        Spawn = spawn;
        Members = spawn.Options
            .Select(o => new WildMember(
                new Digimon(data, data.Species[o.SpeciesId], o.Level, new Attributes(o.Str, o.Agi, o.Con, o.Int)), o))
            .ToList();
        MoveTo((short)random.Next(spawn.XMin, spawn.XMax + 1), (short)random.Next(spawn.YMin, spawn.YMax + 1));
    }

    public Serial Serial { get; } = Serial.New();
    public SpawnArea Spawn { get; }
    public IReadOnlyList<WildMember> Members { get; }
    public Digimon Leader => Members[0].Digimon;
    public short X { get; private set; }
    public short Y { get; private set; }
    public Direction Facing { get; private set; } = Direction.NorthWest;
    public WildState State { get; set; } = WildState.Roaming;
    public TimeSpan RespawnAt { get; set; }

    public void MoveTo(short x, short y)
    {
        // ❓ o cliente só parece usar duas direções para selvagens (protótipo)
        int dx = Math.Sign(x - X), dy = Math.Sign(y - Y);
        Facing = (dx, dy) is (1, _) or (0, 1) ? Direction.SouthEast : Direction.NorthWest;
        X = x;
        Y = y;
    }

    /// <summary>Volta ao mapa com tudo cheio (fim de batalha sem morrer).</summary>
    public void Reset()
    {
        State = WildState.Roaming;
        foreach (var m in Members) m.Digimon.RestoreAll();
    }
}
