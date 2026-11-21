using System.Collections.Frozen;
using System.Globalization;
using System.Xml.Linq;

namespace DRPGServer.Data.GameData;

public sealed class GameDataException(string message) : Exception(message);

/// <summary>Lê as tabelas XML de Resources/ e valida as referências entre elas.</summary>
public static class GameDataLoader
{
    public static GameData Load(string root)
    {
        var species = Elements(root, "Digimon/DigimonData.xml", "DigimonData", "Digimon")
            .Select(e => new DigimonSpecies(
                U16(e, "ID"), Str(e, "Name"), I32(e, "BaseSTR"), I32(e, "BaseAGI"), I32(e, "BaseCON"), I32(e, "BaseINT"),
                I32(e, "ActionGauge"), U8(e, "Classification")))
            .Where(s => s.Id != 0)
            .ToUniqueDictionary(s => s.Id, "DigimonData");

        var items = Elements(root, "Item/ItemTable.xml", "ItemTable", "Item")
            .Select(e => new ItemInfo(U32(e, "ID"), Str(e, "Name")))
            .ToUniqueDictionary(i => i.Id, "ItemTable");

        var drops = new Dictionary<uint, IReadOnlyList<DropEntry>>();
        foreach (var group in Elements(root, "Item/ItemDrop.xml", "DropTable", "DropGroup"))
        {
            uint id = U32(group, "ID");
            var entries = group.Elements("Item").Select(i => new DropEntry(U32(i, "ID"), U8(i, "DropRate"))).ToList();
            foreach (var entry in entries)
            {
                if (!items.ContainsKey(entry.ItemId)) throw new GameDataException($"DropGroup {id}: item {entry.ItemId} não existe");
                if (entry.Rate > 100) throw new GameDataException($"DropGroup {id}: taxa {entry.Rate} fora de 0..100");
            }
            if (!drops.TryAdd(id, entries)) throw new GameDataException($"DropGroup {id} duplicado");
        }

        var digimonExp = Elements(root, "Digimon/ExpTable.xml", "DigimonExpTable", "Digimon")
            .ToUniqueDictionary(e => (int)U16(e, "Level"), "DigimonExpTable", e => I64(e, "TargetEXP"));
        var characterExp = Elements(root, "Character/ExpTable.xml", "CharacterExpTable", "Character")
            .ToUniqueDictionary(e => (int)U16(e, "Level"), "CharacterExpTable", e => I64(e, "TargetEXP"));

        var portals = Elements(root, "Map/Portals.xml", "Portals", "Portal")
            .Select(e => new PortalInfo(U8(e, "MapID"), I16(e, "PosX"), I16(e, "PosY"), U8(e, "DestMapID"), I16(e, "DestPosX"), I16(e, "DestPosY")))
            .GroupBy(p => p.MapId)
            .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<PortalInfo>)g.ToList());

        var spawns = new List<SpawnArea>();
        foreach (var e in Elements(root, "Map/DigimonSpawns.xml", "DigimonSpawns", "Spawn"))
        {
            var options = e.Elements("SpawnOption").Select(o => new SpawnOption(
                U16(o, "DigimonID"), U16(o, "Level"), I32(o, "STR"), I32(o, "AGI"), I32(o, "CON"), I32(o, "INT"),
                I64(o, "ExpReward"), F64(o, "BitReward"), U16(o, "AppearanceRate"), U32(o, "DropGroup"))).ToList();
            var area = new SpawnArea(U8(e, "MapID"), I16(e, "PosXMin"), I16(e, "PosXMax"), I16(e, "PosYMin"), I16(e, "PosYMax"),
                I32(e, "MaxCount"), TimeSpan.FromSeconds(F64(e, "RespawnTime")), options);
            if (options.Count == 0) throw new GameDataException($"spawn no mapa {area.MapId} sem SpawnOption");
            if (options[0].AppearanceRate != 100) throw new GameDataException($"spawn no mapa {area.MapId}: a primeira opção precisa de AppearanceRate 100");
            foreach (var o in options)
            {
                if (!species.ContainsKey(o.SpeciesId)) throw new GameDataException($"spawn no mapa {area.MapId}: digimon {o.SpeciesId} não existe");
                if (o.DropGroup != 0 && !drops.ContainsKey(o.DropGroup)) throw new GameDataException($"spawn no mapa {area.MapId}: DropGroup {o.DropGroup} não existe");
            }
            spawns.Add(area);
        }

        var zoneIds = Elements(root, "Map/Zones.xml", "Zones", "Zone").Select(e => U8(e, "MapID")).Where(id => id != 0).Distinct().ToList();

        return new GameData
        {
            Species = species,
            Items = items,
            DropGroups = drops.ToFrozenDictionary(),
            DigimonExp = digimonExp,
            CharacterExp = characterExp,
            Portals = portals,
            Spawns = spawns.GroupBy(s => s.MapId).ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<SpawnArea>)g.ToList()),
            ZoneIds = zoneIds,
        };
    }

    private static IEnumerable<XElement> Elements(string root, string file, string rootName, string element)
    {
        var path = Path.Combine(root, file);
        if (!File.Exists(path)) throw new GameDataException($"tabela {path} não encontrada");
        var doc = XDocument.Load(path);
        var top = doc.Element(rootName) ?? throw new GameDataException($"{path}: falta a raiz <{rootName}>");
        return top.Elements(element);
    }

    private static FrozenDictionary<TKey, TValue> ToUniqueDictionary<TSource, TKey, TValue>(
        this IEnumerable<TSource> source, Func<TSource, TKey> key, string table, Func<TSource, TValue> value) where TKey : notnull
    {
        var dict = new Dictionary<TKey, TValue>();
        foreach (var item in source)
            if (!dict.TryAdd(key(item), value(item)))
                throw new GameDataException($"{table}: chave {key(item)} duplicada");
        return dict.ToFrozenDictionary();
    }

    private static FrozenDictionary<TKey, TSource> ToUniqueDictionary<TSource, TKey>(
        this IEnumerable<TSource> source, Func<TSource, TKey> key, string table) where TKey : notnull =>
        source.ToUniqueDictionary(key, table, x => x);

    private static string Attr(XElement e, string name) =>
        e.Attribute(name)?.Value ?? throw new GameDataException($"<{e.Name}> sem o atributo {name}");

    private static string Str(XElement e, string name) => e.Attribute(name)?.Value ?? "";
    private static byte U8(XElement e, string name) => byte.Parse(Attr(e, name), CultureInfo.InvariantCulture);
    private static short I16(XElement e, string name) => short.Parse(Attr(e, name), CultureInfo.InvariantCulture);
    private static ushort U16(XElement e, string name) => ushort.Parse(Attr(e, name), CultureInfo.InvariantCulture);
    private static int I32(XElement e, string name) => int.Parse(Attr(e, name), CultureInfo.InvariantCulture);
    private static uint U32(XElement e, string name) => uint.Parse(Attr(e, name), CultureInfo.InvariantCulture);
    private static long I64(XElement e, string name) => long.Parse(Attr(e, name), CultureInfo.InvariantCulture);
    private static double F64(XElement e, string name) => double.Parse(Attr(e, name), CultureInfo.InvariantCulture);
}
