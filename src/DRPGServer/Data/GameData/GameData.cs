using System.Collections.Frozen;

namespace DRPGServer.Data.GameData;

/// <summary>
/// Espécie de digimon, lida das tabelas do cliente (<c>digimon_us</c> e <c>dex_us</c>, ver docs/cliente/arquivos-de-dados.md).
/// <paramref name="Growth"/>: atributos ganhos por nível (✅ in-training e rookie nas capturas); null se a dex não tem a espécie.
/// </summary>
public sealed record DigimonSpecies(
    ushort Id, string Name, byte Stage, byte Attribute, Game.Stats.Attributes? Growth, ushort Skill1, ushort Skill2);

public sealed record ItemInfo(uint Id, string Name);

public sealed record DropEntry(uint ItemId, byte Rate);

public sealed record PortalInfo(byte MapId, short X, short Y, byte DestMapId, short DestX, short DestY)
{
    public const int Radius = 2;

    public bool Contains(int x, int y)
    {
        int dx = x - X, dy = y - Y;
        return dx * dx + dy * dy <= Radius * Radius;
    }
}

/// <summary>Um digimon possível num grupo selvagem. O primeiro da lista sempre aparece.</summary>
public sealed record SpawnOption(
    ushort SpeciesId, ushort Level, int Str, int Agi, int Con, int Int,
    long ExpReward, double BitReward, ushort AppearanceRate, uint DropGroup);

/// <summary>
/// Área de spawn. Cada grupo tem uma casa: um dos <paramref name="Points"/> (✅ fixos no oficial) ou, sem pontos,
/// uma célula andável sorteada no retângulo. O grupo anda só em volta da casa (<c>WildDigimon.RoamRadius</c>).
/// </summary>
public sealed record SpawnArea(
    byte MapId, short XMin, short XMax, short YMin, short YMax, int MaxCount, TimeSpan RespawnTime,
    IReadOnlyList<SpawnOption> Options, IReadOnlyList<(short X, short Y)> Points);

/// <summary>Tabelas estáticas do jogo, carregadas uma vez na partida. Imutáveis.</summary>
public sealed class GameData
{
    public required FrozenDictionary<ushort, DigimonSpecies> Species { get; init; }
    public required FrozenDictionary<uint, ItemInfo> Items { get; init; }
    public required FrozenDictionary<byte, ClientMap> Maps { get; init; }
    /// <summary>Grade de cada mapa (vazia se o grids.bin não foi extraído: aí tudo é andável).</summary>
    public required FrozenDictionary<byte, MapGrid> Grids { get; init; }
    public required FrozenDictionary<uint, IReadOnlyList<DropEntry>> DropGroups { get; init; }
    public required FrozenDictionary<byte, IReadOnlyList<PortalInfo>> Portals { get; init; }
    public required FrozenDictionary<byte, IReadOnlyList<SpawnArea>> Spawns { get; init; }
    public required IReadOnlyList<byte> ZoneIds { get; init; }

    public DigimonSpecies? FindSpecies(ushort id) => Species.GetValueOrDefault(id);

    /// <summary>EXP para passar do nível dado (fórmula do oficial, ver <see cref="Game.Stats.ExpFormulas"/>).</summary>
    public long DigimonExpToNext(int level) => Game.Stats.ExpFormulas.DigimonExpToNext(level);

    public MapGrid? GridOf(byte mapId) => Grids.GetValueOrDefault(mapId);
    public IReadOnlyList<PortalInfo> PortalsOf(byte mapId) => Portals.GetValueOrDefault(mapId) ?? [];
    public IReadOnlyList<SpawnArea> SpawnsOf(byte mapId) => Spawns.GetValueOrDefault(mapId) ?? [];
    public IReadOnlyList<DropEntry> DropGroup(uint id) => DropGroups.GetValueOrDefault(id) ?? [];
}
