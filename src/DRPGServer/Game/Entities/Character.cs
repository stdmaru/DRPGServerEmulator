namespace DRPGServer.Game.Entities;

/// <summary>✅ Códigos de resultado do Map 0x89 (offset 32) confirmados em DigimonRPG.bin @ 0x00AC2498.</summary>
public enum DepositoryResult : byte
{
    OpenCheckError = 0,
    OpenOk = 1,
    OpenFailed = 2,
    DepositOk = 3,
    DepositFailed = 4,
    DepositNotEnoughBits = 5,
    WithdrawOk = 6,
    WithdrawFailed = 7,
    WithdrawNotEnoughBits = 8,
    SwapOk = 9,
    SwapFailed = 10,
    SwapNotEnoughBits = 11,
}

/// <summary>Personagem carregado para o mapa.</summary>
public sealed class Character
{
    /// <summary>✅ Cinco posições no time (cinco blocos no 0x02); pode haver buraco entre elas.</summary>
    public const int PartySize = 5;

    /// <summary>✅ Máximo de 10 Digimons no Digimon Depository (10 × 80 bytes no 0x8A).</summary>
    public const int MaxDepositorySlots = 10;

    private readonly Digimon?[] _party = new Digimon?[PartySize];
    private readonly List<Digimon> _depository = [];
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

    /// <summary>✅ Digimons guardados no Digimon Depository (0..10) e quantidade de slots liberados.</summary>
    public IReadOnlyList<Digimon> Depository => _depository;
    public byte DepositorySlots { get; set; }

    /// <summary>Passos feitos com NPCs (tutorial), persistidos. Ver <c>Game/World/NpcScripts.cs</c>.</summary>
    public IReadOnlySet<ushort> Flags => _flags;
    public bool HasFlag(ushort flag) => _flags.Contains(flag);
    public bool SetFlag(ushort flag) => _flags.Add(flag);

    public void SetBits(double bits) => Bits = Math.Max(0, bits);
    public void AddBits(double amount) => SetBits(Bits + amount);
    public bool TrySpendBits(double amount)
    {
        if (amount <= 0) return true;
        if (Bits < amount) return false;
        Bits -= amount;
        return true;
    }

    public Digimon? FindDigimon(uint uid) => uid == 0 ? null : Digimons.FirstOrDefault(d => d.Uid == uid);
    public Digimon? FindDigimon(Serial serial) => Digimons.FirstOrDefault(d => d.Serial == serial);
    public Digimon? FindInDepository(uint uid) => uid == 0 ? null : _depository.FirstOrDefault(d => d.Uid == uid);
    public int SlotOf(Digimon digimon) => Array.IndexOf(_party, digimon);

    /// <summary>Adiciona um digimon direto ao Depository (carga do banco).</summary>
    public bool AddToDepository(Digimon digimon)
    {
        if (_depository.Count >= MaxDepositorySlots || _party.Contains(digimon) || _depository.Contains(digimon))
            return false;
        _depository.Add(digimon);
        return true;
    }

    /// <summary>Libera +1 slot no Depository (até <see cref="MaxDepositorySlots"/>).</summary>
    public bool ExpandDepository()
    {
        if (DepositorySlots >= MaxDepositorySlots) return false;
        DepositorySlots++;
        return true;
    }

    /// <summary>Move um digimon do time (não-líder) para o Depository.</summary>
    public DepositoryResult Deposit(uint partyDigimonUid, double feeBits)
    {
        if (FindDigimon(partyDigimonUid) is not { } digimon)
            return DepositoryResult.DepositFailed;
        int slot = SlotOf(digimon);
        if (slot <= 0 || _depository.Count >= DepositorySlots)
            return DepositoryResult.DepositFailed;
        if (!TrySpendBits(feeBits))
            return DepositoryResult.DepositNotEnoughBits;

        _party[slot] = null;
        _depository.Add(digimon);
        return DepositoryResult.DepositOk;
    }

    /// <summary>Retira um digimon do Depository para a primeira posição livre do time.</summary>
    public (DepositoryResult Result, Digimon? Digimon) Withdraw(uint depositoryDigimonUid, double feeBits)
    {
        if (FindInDepository(depositoryDigimonUid) is not { } digimon)
            return (DepositoryResult.WithdrawFailed, null);
        int freeSlot = Array.IndexOf(_party, null);
        if (freeSlot < 0)
            return (DepositoryResult.WithdrawFailed, null);
        if (!TrySpendBits(feeBits))
            return (DepositoryResult.WithdrawNotEnoughBits, null);

        _depository.Remove(digimon);
        _party[freeSlot] = digimon;
        return (DepositoryResult.WithdrawOk, digimon);
    }

    /// <summary>Troca um digimon do time com um digimon do Depository.</summary>
    public (DepositoryResult Result, Digimon? Withdrawn) Swap(uint partyDigimonUid, uint depositoryDigimonUid, double feeBits)
    {
        if (FindDigimon(partyDigimonUid) is not { } partyDigimon)
            return (DepositoryResult.SwapFailed, null);
        if (FindInDepository(depositoryDigimonUid) is not { } storedDigimon)
            return (DepositoryResult.SwapFailed, null);
        int partySlot = SlotOf(partyDigimon);
        int depIndex = _depository.IndexOf(storedDigimon);
        if (partySlot < 0 || depIndex < 0)
            return (DepositoryResult.SwapFailed, null);
        if (!TrySpendBits(feeBits))
            return (DepositoryResult.SwapNotEnoughBits, null);

        _party[partySlot] = storedDigimon;
        _depository[depIndex] = partyDigimon;
        return (DepositoryResult.SwapOk, storedDigimon);
    }

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
