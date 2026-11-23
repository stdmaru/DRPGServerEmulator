namespace DRPGServer.Game.Entities;

/// <summary>Personagem carregado para o mapa.</summary>
public sealed class Character
{
    /// <summary>✅ Cinco posições no time (cinco blocos no 0x02); pode haver buraco entre elas.</summary>
    public const int PartySize = 5;

    private readonly Digimon?[] _party = new Digimon?[PartySize];
    private readonly HashSet<ushort> _flags = [];

    public Character(Digimon leader, Inventory inventory)
    {
        _party[0] = leader;
        Inventory = inventory;
    }

    public required uint Uid { get; init; }
    public required uint AccountUid { get; init; }
    public required byte Slot { get; init; }
    public required string Name { get; init; }
    public required byte TamerId { get; init; }
    public Serial Serial { get; } = Serial.New();
    public ushort Level { get; set; } = 1;
    public byte MapId { get; set; }
    public short X { get; set; }
    public short Y { get; set; }
    public double Bits { get; private set; }
    public int TotalWins { get; set; }
    public int TotalBattles { get; set; }

    /// <summary>✅ O líder é a posição 0 do time (0xF1 troca o escolhido com ela).</summary>
    public Digimon Leader => _party[0]!;
    public Inventory Inventory { get; }

    /// <summary>Time pela posição (null = vazio).</summary>
    public IReadOnlyList<Digimon?> Party => _party;
    public IEnumerable<Digimon> Digimons => _party.OfType<Digimon>();

    /// <summary>Passos feitos com NPCs (tutorial), persistidos. Ver <c>Game/World/NpcScripts.cs</c>.</summary>
    public IReadOnlySet<ushort> Flags => _flags;
    public bool HasFlag(ushort flag) => _flags.Contains(flag);
    public bool SetFlag(ushort flag) => _flags.Add(flag);

    public void SetBits(double bits) => Bits = Math.Max(0, bits);
    public void AddBits(double amount) => SetBits(Bits + amount);

    public Digimon? FindDigimon(uint uid) => uid == 0 ? null : Digimons.FirstOrDefault(d => d.Uid == uid);
    public Digimon? FindDigimon(Serial serial) => Digimons.FirstOrDefault(d => d.Serial == serial);
    public int SlotOf(Digimon digimon) => Array.IndexOf(_party, digimon);

    /// <summary>Coloca um digimon numa posição (carga do banco). A posição 0 sempre tem alguém.</summary>
    public bool Place(int slot, Digimon digimon)
    {
        if (slot is < 0 or >= PartySize || (_party[slot] is not null && slot != 0) || _party.Contains(digimon)) return false;
        _party[slot] = digimon;
        return true;
    }

    /// <summary>✅ 0xF1: o escolhido vai para a posição 0 e o líder antigo para a posição dele.</summary>
    public bool SetLeader(Digimon digimon)
    {
        int slot = SlotOf(digimon);
        if (slot <= 0) return false;
        (_party[0], _party[slot]) = (_party[slot], _party[0]);
        return true;
    }

    /// <summary>
    /// ✅ 0x173: o botão k (0..2) troca as posições k + 1 e k + 2, mesmo com uma delas vazia (sessão 56). O líder não
    /// entra.
    /// </summary>
    public bool SwapSlots(int a, int b)
    {
        if (a is < 1 or >= PartySize || b is < 1 or >= PartySize || a == b) return false;
        if (_party[a] is null && _party[b] is null) return false;
        (_party[a], _party[b]) = (_party[b], _party[a]);
        return true;
    }
}

/// <summary>Resumo para a lista de personagens do channel.</summary>
public sealed record CharacterSummary(
    uint Uid, byte Slot, string Name, byte TamerId, ushort Level, byte MapId, short X, short Y,
    int TotalWins, int TotalBattles, ushort LeaderSpeciesId, ushort LeaderLevel, string LeaderName);
