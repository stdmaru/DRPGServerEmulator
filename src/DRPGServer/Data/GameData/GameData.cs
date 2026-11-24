using System.Collections.Frozen;

namespace DRPGServer.Data.GameData;

/// <summary>
/// Espécie de digimon, lida das tabelas do cliente (<c>digimon_us</c> e <c>dex_us</c>, ver docs/cliente/arquivos-de-dados.md).
/// <paramref name="Growth"/>: atributos ganhos por nível (✅ in-training e rookie nas capturas); null se a dex não tem a espécie.
/// </summary>
public sealed record DigimonSpecies(
    ushort Id, string Name, byte Stage, byte Attribute, Game.Stats.Attributes? Growth, ushort Skill1, ushort Skill2, byte Element = 0);

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
/// </summary>
public sealed record SpawnArea(
    byte MapId, short XMin, short XMax, short YMin, short YMax, int MaxCount, TimeSpan RespawnTime,
    IReadOnlyList<SpawnOption> Options, IReadOnlyList<(short X, short Y)> Points, bool Exclusive = false);

/// <summary>Tabelas estáticas do jogo, carregadas uma vez na partida. Imutáveis.</summary>
public sealed class GameData
{
    public required FrozenDictionary<ushort, DigimonSpecies> Species { get; init; }
    public required FrozenDictionary<uint, ItemInfo> Items { get; init; }
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
