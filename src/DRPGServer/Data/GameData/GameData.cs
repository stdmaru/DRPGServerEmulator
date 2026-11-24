using System.Collections.Frozen;

namespace DRPGServer.Data.GameData;

/// <summary>
/// Espécie de digimon, lida das tabelas do cliente (<c>digimon_us</c> e <c>dex_us</c>, ver docs/cliente/arquivos-de-dados.md).
/// <paramref name="Growth"/>: atributos ganhos por nível (✅ in-training e rookie nas capturas); null se a dex não tem a espécie.
/// </summary>
public sealed record DigimonSpecies(
    ushort Id, string Name, byte Stage, byte Attribute, Game.Stats.Attributes? Growth, ushort Skill1, ushort Skill2, byte Element = 0);

/// <summary>
/// Bônus de status extraídos da coluna 28 de <c>item_info_us.dat</c> para equipamentos do Tamer.
/// ✅ Confirmado em <c>captures.db</c> (<c>conn=234</c>): atributos (STR/AGI/CON/INT) entram antes de
/// <see cref="Game.Stats.StatFormulas.Combat"/> e bônus flat (ATK/DEF/BR/HP/VP) somam depois.
/// </summary>
public readonly record struct EquipmentBonus(
    Game.Stats.Attributes Attributes = default,
    int BonusHp = 0,
    int BonusVp = 0,
    int BonusAtk = 0,
    int BonusDef = 0,
    int BonusBr = 0,
    int BonusAtkPercent = 0,
    int BonusDefPercent = 0,
    int BonusBrPercent = 0,
    int CardSlashLimit = 0,
    int DigimonExpPercent = 0,
    int TamerExpFlat = 0,
    int HpRecovery = 0,
    int VpRecovery = 0,
    int EvpRecovery = 0)
{
    public static EquipmentBonus Empty => default;

    public bool IsEmpty =>
        Attributes == default
        && BonusHp == 0 && BonusVp == 0
        && BonusAtk == 0 && BonusDef == 0 && BonusBr == 0
        && BonusAtkPercent == 0 && BonusDefPercent == 0 && BonusBrPercent == 0
        && CardSlashLimit == 0 && DigimonExpPercent == 0 && TamerExpFlat == 0
        && HpRecovery == 0 && VpRecovery == 0 && EvpRecovery == 0;

    public static EquipmentBonus operator +(EquipmentBonus a, EquipmentBonus b) => new(
        Attributes: a.Attributes + b.Attributes,
        BonusHp: a.BonusHp + b.BonusHp,
        BonusVp: a.BonusVp + b.BonusVp,
        BonusAtk: a.BonusAtk + b.BonusAtk,
        BonusDef: a.BonusDef + b.BonusDef,
        BonusBr: a.BonusBr + b.BonusBr,
        BonusAtkPercent: a.BonusAtkPercent + b.BonusAtkPercent,
        BonusDefPercent: a.BonusDefPercent + b.BonusDefPercent,
        BonusBrPercent: a.BonusBrPercent + b.BonusBrPercent,
        CardSlashLimit: Math.Max(a.CardSlashLimit, b.CardSlashLimit),
        DigimonExpPercent: a.DigimonExpPercent + b.DigimonExpPercent,
        TamerExpFlat: a.TamerExpFlat + b.TamerExpFlat,
        HpRecovery: a.HpRecovery + b.HpRecovery,
        VpRecovery: a.VpRecovery + b.VpRecovery,
        EvpRecovery: a.EvpRecovery + b.EvpRecovery);
}

/// <summary>Item de <c>item_info_us</c> com as colunas que o registro de 108 bytes repete (ver <see cref="ClientTables.Items"/>).</summary>
public sealed record ItemInfo(uint Id, string Name)
{
    public byte Type { get; init; }
    public byte SubType { get; init; }
    public ushort Col16 { get; init; }
    public uint Col17 { get; init; }
    public uint BuyPrice { get; init; }
    public uint SellPrice { get; init; }
    public uint Level { get; init; } = 1;
    public byte Flag24 { get; init; }
    public byte Flag25 { get; init; }
    public uint MaxStack { get; init; } = 1;
    public byte Flag33 { get; init; }
    public string Description { get; init; } = "";
    public EquipmentBonus EquipmentBonus { get; init; }
}

/// <summary>Momento de ativação de um card em batalha (coluna 23 de <c>card_info_us</c>).</summary>
public enum CardTrigger : byte
{
    Attack = 1,
    Defense = 2,
}

/// <summary>Card de <c>card_info_us</c> (469 entradas, 31 colunas; ver <see cref="ClientTables.Cards"/>).</summary>
public sealed record CardInfo(uint Id, string Name)
{
    public byte Type { get; init; }
    public byte SubType { get; init; }
    public uint BuyPrice { get; init; }
    public uint SellPrice { get; init; }
    public byte Flag19 { get; init; }
    public uint MaxStack { get; init; } = 999;
    public CardTrigger Trigger { get; init; } = CardTrigger.Attack;
    public byte Restriction { get; init; }
    public ushort Col26 { get; init; } = 32000;
    public byte Col27 { get; init; } = 1;
    public uint Col28 { get; init; } = 1_000_000;
    public byte Col29 { get; init; } = 1;
    public byte Col30 { get; init; }
    public string Description { get; init; } = "";
}

/// <summary>Título de Digimon de <c>digimon_title_us.dat</c> (236 entradas; ver <see cref="ClientTables.DigimonTitles"/>).</summary>
public sealed record DigimonTitleInfo(uint Id, string Name)
{
    public Game.Stats.Attributes AttributeBonus { get; init; }
    public int BonusHp { get; init; }
    public int BonusVp { get; init; }
    public int BonusAtk { get; init; }
    public int BonusDef { get; init; }
    public int BonusBr { get; init; }
    public int BonusAtkPercent { get; init; }
    public int BonusDefPercent { get; init; }
    public int BonusBrPercent { get; init; }
}

/// <summary>
/// Skill de <c>digimon_skill_us</c>. ✅ Custo em VP = 20 + nível exigido.
/// <paramref name="Type"/>: ✅ 1 = corpo-a-corpo, 2 = à distância (offset 36 do 0x67).
/// <paramref name="TargetCount"/>: ✅ coluna 5 da tabela (1 = alvo único, 2 = 2 alvos, 5 = área / todos).
/// </summary>
public sealed record SkillInfo(ushort Id, string Name, ushort RequiredLevel, byte Type, byte TargetCount = 1)
{
    public const int MaxLevel = 10;
    public const byte TypeMelee = 1;
    public const byte TypeRanged = 2;

    public int VpCost => 20 + RequiredLevel;
}

public sealed record DropEntry(uint ItemId, byte Rate);

/// <summary>
/// Taming Net: captura no mapa um selvagem do estágio <paramref name="Stage"/> (1 in-training … 5 mega), com
/// <paramref name="Chance"/> % (✅ as duas das amostras capturaram de primeira; são itens de cash).
/// </summary>
public sealed record TamingNet(uint ItemId, byte Stage, byte Chance);

public sealed record PortalInfo(byte MapId, short X, short Y, byte DestMapId, short DestX, short DestY)
{
    public const int Radius = 2;

    public bool Contains(int x, int y)
    {
        int dx = x - X, dy = y - Y;
        return dx * dx + dy * dy <= Radius * Radius;
    }
}

/// <summary>
/// Um digimon possível num grupo selvagem. O primeiro da lista sempre aparece. Os status vêm do nível
/// (<see cref="Game.Stats.WildFormulas"/>); <see cref="Stats"/> substitui (XML: HP, VP, ATK, DEF, BR), como o
/// Yuramon enfraquecido do tutorial. <paramref name="BitReward"/> é o máximo: ✅ o oficial sorteia de ¾ dele até ele.
/// </summary>
public sealed record SpawnOption(
    ushort SpeciesId, ushort Level, long ExpReward, double BitReward, ushort AppearanceRate, uint DropGroup)
{
    public Game.Stats.CombatStats? Stats { get; init; }
}

/// <summary>
/// Área de spawn. Cada grupo tem uma casa: um dos <paramref name="Points"/> (✅ fixos no oficial) ou, sem pontos,
/// uma célula andável sorteada no retângulo. O grupo anda só em volta da casa (<c>WildDigimon.RoamRadius</c>).
/// <paramref name="Exclusive"/>: ✅ grupo que entra em batalha (11) e morre (8 → 12), como os Otamamon da sessão 56.
/// Sem ele o grupo é compartilhado: continua andando no mapa e a batalha usa cópias (docs/mecanicas/batalha.md).
/// <paramref name="Grade"/>: ✅ grau do grupo (2 = Beginner, 3 = Veteran — byte 44 do 0x11 e byte 300 do 0x63).
/// </summary>
public sealed record SpawnArea(
    byte MapId, short XMin, short XMax, short YMin, short YMax, int MaxCount, TimeSpan RespawnTime,
    IReadOnlyList<SpawnOption> Options, IReadOnlyList<(short X, short Y)> Points, bool Exclusive = false,
    byte Grade = Game.Stats.WildFormulas.GradeBeginner);

/// <summary>Tabelas estáticas do jogo, carregadas uma vez na partida. Imutáveis.</summary>
public sealed class GameData
{
    public required FrozenDictionary<ushort, DigimonSpecies> Species { get; init; }
    public required FrozenDictionary<uint, ItemInfo> Items { get; init; }
    public FrozenDictionary<uint, CardInfo> Cards { get; init; } = FrozenDictionary<uint, CardInfo>.Empty;
    public FrozenDictionary<uint, DigimonTitleInfo> DigimonTitles { get; init; } = FrozenDictionary<uint, DigimonTitleInfo>.Empty;
    public FrozenDictionary<ushort, SkillInfo> Skills { get; init; } = FrozenDictionary<ushort, SkillInfo>.Empty;
    /// <summary>Espécie → próxima forma da linha evolutiva (<c>tree_us</c>).</summary>
    public FrozenDictionary<ushort, ushort> Evolutions { get; init; } = FrozenDictionary<ushort, ushort>.Empty;
    public required FrozenDictionary<byte, ClientMap> Maps { get; init; }
    /// <summary>Grade de cada mapa (vazia se o grids.bin não foi extraído: aí tudo é andável).</summary>
    public required FrozenDictionary<byte, MapGrid> Grids { get; init; }
    public required FrozenDictionary<uint, IReadOnlyList<DropEntry>> DropGroups { get; init; }
    public FrozenDictionary<uint, TamingNet> TamingNets { get; init; } = FrozenDictionary<uint, TamingNet>.Empty;
    public required FrozenDictionary<byte, IReadOnlyList<PortalInfo>> Portals { get; init; }
    public required FrozenDictionary<byte, IReadOnlyList<SpawnArea>> Spawns { get; init; }
    public required IReadOnlyList<byte> ZoneIds { get; init; }

    public DigimonSpecies? FindSpecies(ushort id) => Species.GetValueOrDefault(id);
    public SkillInfo? FindSkill(ushort id) => id == 0 ? null : Skills.GetValueOrDefault(id);
    public ItemInfo? FindItem(uint id) => Items.GetValueOrDefault(id);
    public CardInfo? FindCard(uint id) => Cards.GetValueOrDefault(id);
    public DigimonTitleInfo? FindDigimonTitle(uint id) => id == 0 ? null : DigimonTitles.GetValueOrDefault(id);

    /// <summary>Próxima forma da linha (null no fim da linha ou se a espécie não está na árvore).</summary>
    public DigimonSpecies? NextForm(DigimonSpecies species) =>
        Evolutions.TryGetValue(species.Id, out var next) ? FindSpecies(next) : null;

    /// <summary>EXP para passar do nível dado (fórmula do oficial, ver <see cref="Game.Stats.ExpFormulas"/>).</summary>
    public long DigimonExpToNext(int level) => Game.Stats.ExpFormulas.DigimonExpToNext(level);

    public MapGrid? GridOf(byte mapId) => Grids.GetValueOrDefault(mapId);
    public IReadOnlyList<PortalInfo> PortalsOf(byte mapId) => Portals.GetValueOrDefault(mapId) ?? [];
    public IReadOnlyList<SpawnArea> SpawnsOf(byte mapId) => Spawns.GetValueOrDefault(mapId) ?? [];
    public IReadOnlyList<DropEntry> DropGroup(uint id) => DropGroups.GetValueOrDefault(id) ?? [];
    public TamingNet? FindTamingNet(uint itemId) => TamingNets.GetValueOrDefault(itemId);
}
