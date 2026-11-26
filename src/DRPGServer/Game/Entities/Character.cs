using DRPGServer.Game.Economy;
using DRPGServer.Game.Stats;

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

    /// <summary>✅ 10 slots por página e até 21 páginas (210 slots) no Digimon Depository (DigimonRPG.bin @ 0x00B7B94B).</summary>
    public const int DepositoryPageSize = 10;
    public const int MaxDepositoryPages = 21;
    public const int MaxDepositorySlots = DepositoryPageSize * MaxDepositoryPages;

    private readonly Digimon?[] _party = new Digimon?[PartySize];
    private readonly Digimon?[] _depository = new Digimon?[MaxDepositorySlots];
    private readonly HashSet<ushort> _flags = [];

    public Character(Digimon leader, Inventory inventory)
    {
        _party[0] = leader;
        Inventory = inventory;
        Economy = new EconomyLedger(this);
        Wallet = new Wallet(Economy);
        SyncEquipmentModifiers();
    }

    /// <summary>Bônus juntos de todos os equipamentos do Tamer.</summary>
    public StatModifiers EquipmentModifiers => Inventory.EquipmentModifiers();

    /// <summary>
    /// Recalcula o bônus total de equipamentos do Tamer e propaga para todos os Digimons do time
    /// (✅ confirmado em <c>captures.db conn=234</c>: Crests e Digimentals aplicam a todos os Digimons da party).
    /// </summary>
    public void SyncEquipmentModifiers()
    {
        var modifiers = Inventory.EquipmentModifiers();
        foreach (var d in Digimons)
            d.SetEquipmentModifiers(modifiers);
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
    public int TotalWins { get; set; }
    public int TotalBattles { get; set; }

    /// <summary>✅ O líder é a posição 0 do time (0xF1 troca o escolhido com ela).</summary>
    public Digimon Leader => _party[0]!;
    public Inventory Inventory { get; }

    /// <summary>Time pela posição (null = vazio).</summary>
    public IReadOnlyList<Digimon?> Party => _party;
    public IEnumerable<Digimon> Digimons => _party.OfType<Digimon>();

    /// <summary>✅ Digimons guardados no Digimon Depository (até 210 slots em 21 páginas) e slots liberados.</summary>
    public IReadOnlyList<Digimon?> DepositoryGrid => _depository;
    public IEnumerable<Digimon> Depository => _depository.OfType<Digimon>();
    public byte DepositorySlots { get; set; }

    /// <summary>Passos feitos com NPCs (escolhas 0x56), persistidos. Ver <c>Game/Npcs/</c>.</summary>
    public IReadOnlySet<ushort> Flags => _flags;
    public bool HasFlag(ushort flag) => _flags.Contains(flag);
    public bool SetFlag(ushort flag) => _flags.Add(flag);

    /// <summary>Movimentações de moeda e item que ainda não chegaram ao banco (<see cref="EconomyLedger"/>).</summary>
    public EconomyLedger Economy { get; }

    /// <summary>Moedas, auditadas em <see cref="Economy"/>.</summary>
    public Wallet Wallet { get; }

    public Digimon? FindDigimon(uint uid) => uid == 0 ? null : Digimons.FirstOrDefault(d => d.Uid == uid);
    public Digimon? FindDigimon(Serial serial) => Digimons.FirstOrDefault(d => d.Serial == serial);
    public Digimon? FindInDepository(uint uid) => uid == 0 ? null : Depository.FirstOrDefault(d => d.Uid == uid);
    public int SlotOf(Digimon digimon) => Array.IndexOf(_party, digimon);

    /// <summary>Adiciona um digimon direto ao Depository na posição salva (carga do banco).</summary>
    public bool AddToDepository(Digimon digimon, int slot = -1)
    {
        if (_party.Contains(digimon) || _depository.Contains(digimon)) return false;
        if (slot is >= 0 and < MaxDepositorySlots && _depository[slot] is null)
        {
            _depository[slot] = digimon;
            return true;
        }
        int freeSlot = Array.IndexOf(_depository, null);
        if (freeSlot < 0) return false;
        _depository[freeSlot] = digimon;
        return true;
    }

    /// <summary>Digimons guardados na página <paramref name="page"/> (1-based).</summary>
    public IReadOnlyList<Digimon> DepositoryPage(ushort page)
    {
        int start = (Math.Max((int)page, 1) - 1) * DepositoryPageSize;
        int end = Math.Min(start + DepositoryPageSize, MaxDepositorySlots);
        var list = new List<Digimon>(DepositoryPageSize);
        for (int i = start; i < end; i++)
        {
            if (_depository[i] is { } digimon)
                list.Add(digimon);
        }
        return list;
    }

    /// <summary>Libera +1 slot no Depository (até <see cref="MaxDepositorySlots"/>).</summary>
    public bool ExpandDepository()
    {
        if (DepositorySlots >= MaxDepositorySlots) return false;
        DepositorySlots++;
        return true;
    }

    /// <summary>✅ Quantidade de páginas visíveis no Depository (1..21; cada página tem até 10 slots).</summary>
    public ushort DepositoryTotalPages => (ushort)Math.Max(1, (DepositorySlots + DepositoryPageSize - 1) / DepositoryPageSize);

    /// <summary>✅ Quantidade de slots liberados na página <paramref name="page"/> (1-based, 0..10).</summary>
    public byte DepositorySlotsOnPage(ushort page)
    {
        int start = (Math.Max((int)page, 1) - 1) * DepositoryPageSize;
        return (byte)Math.Clamp(DepositorySlots - start, 0, DepositoryPageSize);
    }

    /// <summary>✅ Taxa oficial em Bits por nível do Digimon (DigimonRPG.bin @ 0x00BDFF10).</summary>
    public static long DepositoryFeeForLevel(ushort level) => level switch
    {
        0 => 0,
        <= 10 => 1_000,
        <= 20 => 5_000,
        <= 30 => 10_000,
        <= 40 => 20_000,
        <= 50 => 30_000,
        <= 60 => 40_000,
        <= 70 => 50_000,
        <= 80 => 60_000,
        _ => 70_000,
    };

    /// <summary>
    /// Resolve a taxa em Bits de um Digimon no Depository: <c>&lt; 0</c> usa a tabela oficial por nível;
    /// <c>0</c> é gratuito; <c>&gt; 0</c> usa o valor fixo configurado.
    /// </summary>
    public static long ResolveDepositoryFee(ushort level, long configuredFee) =>
        configuredFee < 0 ? DepositoryFeeForLevel(level) : configuredFee;

    /// <summary>Move um digimon do time (nunca o líder na posição 0) para a página escolhida do Depository.</summary>
    public DepositoryResult Deposit(uint partyDigimonUid, ushort page, long configuredFee)
    {
        if (page < 1 || page > DepositoryTotalPages)
            return DepositoryResult.DepositFailed;
        if (FindDigimon(partyDigimonUid) is not { } digimon)
            return DepositoryResult.DepositFailed;
        int partySlot = SlotOf(digimon);
        if (partySlot <= 0)
            return DepositoryResult.DepositFailed;

        int pageStart = (page - 1) * DepositoryPageSize;
        int slotsOnPage = DepositorySlotsOnPage(page);
        int freeDepSlot = -1;
        for (int i = pageStart; i < pageStart + slotsOnPage; i++)
        {
            if (_depository[i] is null)
            {
                freeDepSlot = i;
                break;
            }
        }
        if (freeDepSlot < 0)
            return DepositoryResult.DepositFailed;

        long fee = ResolveDepositoryFee(digimon.Level, configuredFee);
        if (!Wallet.TrySpend(CurrencyKind.Bits, fee, new(EconomySource.DigimonDepository, "deposit-fee", $"digimon {digimon.Uid}")))
            return DepositoryResult.DepositNotEnoughBits;

        _party[partySlot] = null;
        _depository[freeDepSlot] = digimon;
        digimon.SetEquipmentModifiers(StatModifiers.None);
        return DepositoryResult.DepositOk;
    }

    /// <summary>Retira um digimon do Depository para a primeira posição livre do time (recusa se o grupo estiver cheio).</summary>
    public (DepositoryResult Result, Digimon? Digimon) Withdraw(uint depositoryDigimonUid, long configuredFee)
    {
        if (FindInDepository(depositoryDigimonUid) is not { } digimon)
            return (DepositoryResult.WithdrawFailed, null);
        int depSlot = Array.IndexOf(_depository, digimon);
        int freePartySlot = Array.IndexOf(_party, null);
        if (depSlot < 0 || freePartySlot < 0)
            return (DepositoryResult.WithdrawFailed, null);
        long fee = ResolveDepositoryFee(digimon.Level, configuredFee);
        if (!Wallet.TrySpend(CurrencyKind.Bits, fee, new(EconomySource.DigimonDepository, "withdraw-fee", $"digimon {digimon.Uid}")))
            return (DepositoryResult.WithdrawNotEnoughBits, null);

        _depository[depSlot] = null;
        _party[freePartySlot] = digimon;
        digimon.SetEquipmentModifiers(EquipmentModifiers);
        return (DepositoryResult.WithdrawOk, digimon);
    }

    /// <summary>Troca um digimon do time (nunca o líder na posição 0) com um digimon do Depository.</summary>
    public (DepositoryResult Result, Digimon? Withdrawn) Swap(uint partyDigimonUid, uint depositoryDigimonUid, long configuredFee)
    {
        if (FindDigimon(partyDigimonUid) is not { } partyDigimon)
            return (DepositoryResult.SwapFailed, null);
        if (FindInDepository(depositoryDigimonUid) is not { } storedDigimon)
            return (DepositoryResult.SwapFailed, null);
        int partySlot = SlotOf(partyDigimon);
        int depSlot = Array.IndexOf(_depository, storedDigimon);
        if (partySlot <= 0 || depSlot < 0)
            return (DepositoryResult.SwapFailed, null);
        long fee = ResolveDepositoryFee(partyDigimon.Level, configuredFee)
                   + ResolveDepositoryFee(storedDigimon.Level, configuredFee);
        if (!Wallet.TrySpend(CurrencyKind.Bits, fee, new(EconomySource.DigimonDepository, "swap-fee", $"digimon {partyDigimon.Uid} ↔ {storedDigimon.Uid}")))
            return (DepositoryResult.SwapNotEnoughBits, null);

        _party[partySlot] = storedDigimon;
        _depository[depSlot] = partyDigimon;
        storedDigimon.SetEquipmentModifiers(EquipmentModifiers);
        return (DepositoryResult.SwapOk, storedDigimon);
    }

    /// <summary>Coloca um digimon numa posição (carga do banco). A posição 0 sempre tem alguém.</summary>
    public bool Place(int slot, Digimon digimon)
    {
        if (slot is < 0 or >= PartySize || (_party[slot] is not null && slot != 0) || _party.Contains(digimon)) return false;
        _party[slot] = digimon;
        digimon.SetEquipmentModifiers(EquipmentModifiers);
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
