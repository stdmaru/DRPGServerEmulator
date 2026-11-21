using System.Collections.Frozen;

namespace DRPGServer.Data.GameData;

/// <summary>Espécie de digimon (Resources/Digimon/DigimonData.xml). Os "Base*" são palpite do protótipo.</summary>
public sealed record DigimonSpecies(
    ushort Id, string Name, int BaseStr, int BaseAgi, int BaseCon, int BaseInt, int ActionGauge, byte Classification);

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

public sealed record SpawnArea(
    byte MapId, short XMin, short XMax, short YMin, short YMax, int MaxCount, TimeSpan RespawnTime,
    IReadOnlyList<SpawnOption> Options);

/// <summary>Tabelas estáticas do jogo, carregadas uma vez na partida. Imutáveis.</summary>
public sealed class GameData
{
    public required FrozenDictionary<ushort, DigimonSpecies> Species { get; init; }
    public required FrozenDictionary<uint, ItemInfo> Items { get; init; }
    public required FrozenDictionary<uint, IReadOnlyList<DropEntry>> DropGroups { get; init; }
    public required FrozenDictionary<int, long> DigimonExp { get; init; }
    public required FrozenDictionary<int, long> CharacterExp { get; init; }
    public required FrozenDictionary<byte, IReadOnlyList<PortalInfo>> Portals { get; init; }
    public required FrozenDictionary<byte, IReadOnlyList<SpawnArea>> Spawns { get; init; }
    public required IReadOnlyList<byte> ZoneIds { get; init; }

    public DigimonSpecies? FindSpecies(ushort id) => Species.GetValueOrDefault(id);

    /// <summary>EXP para passar do nível dado. Sem entrada na tabela = não sobe mais.</summary>
    public long DigimonExpToNext(int level) =>
        DigimonExp.TryGetValue(level, out var exp) && exp > 0 ? exp : long.MaxValue;

    public IReadOnlyList<PortalInfo> PortalsOf(byte mapId) => Portals.GetValueOrDefault(mapId) ?? [];
    public IReadOnlyList<SpawnArea> SpawnsOf(byte mapId) => Spawns.GetValueOrDefault(mapId) ?? [];
    public IReadOnlyList<DropEntry> DropGroup(uint id) => DropGroups.GetValueOrDefault(id) ?? [];
}
