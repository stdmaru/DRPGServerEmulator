using System.Collections.Frozen;
using System.Globalization;
using System.Xml.Linq;

namespace DRPGServer.Data.GameData;

public sealed class GameDataException(string message) : Exception(message);

/// <summary>
/// Junta as tabelas do cliente (<see cref="ClientTables"/>) com as do servidor em Resources/ (o que o cliente
/// não tem: spawns, drops e pontos de chegada dos portais) e valida as referências entre elas.
/// </summary>
public static class GameDataLoader
{
    /// <summary>Distância máxima, por eixo, entre o portal de volta e o ponto de chegada estimado.</summary>
    private const int ArrivalNudge = 5;

    /// <param name="root">Pasta Resources/ do servidor.</param>
    /// <param name="pack">Pacote de tabelas do cliente.</param>
    /// <param name="grids">Grades dos mapas (<see cref="MapGrid.Load"/>); null = sem validação de posição.</param>
    public static GameData Load(string root, ClientPack pack, FrozenDictionary<byte, MapGrid>? grids = null)
    {
        grids ??= FrozenDictionary<byte, MapGrid>.Empty;
        var species = ClientTables.Species(pack);
        var items = ClientTables.Items(pack);
        var maps = ClientTables.Maps(pack);

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

        var nets = new Dictionary<uint, TamingNet>();
        foreach (var e in Elements(root, "Item/TamingNets.xml", "TamingNets", "Net"))
        {
            var net = new TamingNet(U32(e, "ItemID"), U8(e, "Stage"), U8(e, "Chance"));
            if (!items.ContainsKey(net.ItemId)) throw new GameDataException($"TamingNets.xml: item {net.ItemId} não existe");
            if (net.Stage is < 1 or > 5) throw new GameDataException($"TamingNets.xml: item {net.ItemId} com estágio {net.Stage} fora de 1..5");
            if (net.Chance > 100) throw new GameDataException($"TamingNets.xml: item {net.ItemId} com chance {net.Chance} fora de 0..100");
            if (!nets.TryAdd(net.ItemId, net)) throw new GameDataException($"TamingNets.xml: item {net.ItemId} duplicado");
        }

        var arrivals = new Dictionary<(byte, short, short), PortalInfo>();
        foreach (var e in Elements(root, "Map/Portals.xml", "Portals", "Portal"))
        {
            var p = new PortalInfo(U8(e, "MapID"), I16(e, "PosX"), I16(e, "PosY"), U8(e, "DestMapID"), I16(e, "DestPosX"), I16(e, "DestPosY"));
            if (!maps.TryGetValue(p.MapId, out var map) || !map.Portals.Any(c => c.X == p.X && c.Y == p.Y))
                throw new GameDataException($"Portals.xml: mapa {p.MapId} não tem portal em ({p.X}, {p.Y}) no map_us");
            if (!maps.ContainsKey(p.DestMapId)) throw new GameDataException($"Portals.xml: mapa {p.DestMapId} não existe");
            if (!arrivals.TryAdd((p.MapId, p.X, p.Y), p)) throw new GameDataException($"Portals.xml: portal ({p.MapId}, {p.X}, {p.Y}) duplicado");
        }
        var portals = ResolvePortals(maps, arrivals);

        var spawns = new List<SpawnArea>();
        foreach (var e in Elements(root, "Map/DigimonSpawns.xml", "DigimonSpawns", "Spawn"))
        {
            var options = e.Elements("SpawnOption").Select(o => new SpawnOption(
                U16(o, "DigimonID"), U16(o, "Level"), I64(o, "ExpReward"), F64(o, "BitReward"), U16(o, "AppearanceRate"),
                U32(o, "DropGroup")) { Stats = FixedStats(o, U16(o, "Level")) }).ToList();
            var points = e.Elements("Point").Select(p => (I16(p, "X"), I16(p, "Y"))).ToList();
            var area = new SpawnArea(U8(e, "MapID"), I16(e, "PosXMin"), I16(e, "PosXMax"), I16(e, "PosYMin"), I16(e, "PosYMax"),
                points.Count > 0 ? points.Count : I32(e, "MaxCount"), TimeSpan.FromSeconds(F64(e, "RespawnTime")), options, points,
                (bool?)e.Attribute("Exclusive") ?? false);
            if (grids.GetValueOrDefault(area.MapId) is { } grid)
                foreach (var (x, y) in points)
                    if (!grid.IsWalkable(x, y)) throw new GameDataException($"spawn no mapa {area.MapId}: ponto ({x}, {y}) não é andável");
            if (options.Count == 0) throw new GameDataException($"spawn no mapa {area.MapId} sem SpawnOption");
            if (options[0].AppearanceRate != 100) throw new GameDataException($"spawn no mapa {area.MapId}: a primeira opção precisa de AppearanceRate 100");
            foreach (var o in options)
            {
                if (!species.ContainsKey(o.SpeciesId)) throw new GameDataException($"spawn no mapa {area.MapId}: digimon {o.SpeciesId} não existe");
                if (o.DropGroup != 0 && !drops.ContainsKey(o.DropGroup)) throw new GameDataException($"spawn no mapa {area.MapId}: DropGroup {o.DropGroup} não existe");
            }
            spawns.Add(area);
        }

        var zoneIds = maps.Keys.Order().ToList();

        return new GameData
        {
            Species = species,
            Items = items,
            Skills = ClientTables.Skills(pack),
            Evolutions = ClientTables.Evolutions(pack),
            DropGroups = drops.ToFrozenDictionary(),
            TamingNets = nets.ToFrozenDictionary(),
            Maps = maps,
            Grids = grids,
            Portals = portals,
            Spawns = spawns.GroupBy(s => s.MapId).ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<SpawnArea>)g.ToList()),
            ZoneIds = zoneIds,
        };
    }

    /// <summary>
    /// Portais do <c>map_us</c>. O destino vem do Portals.xml (✅ capturado) quando existe. Senão, o mapa de destino
    /// é o do nome (desempata pelo mapa que tem portal de volta) e a chegada é ❓ estimada: o portal de volta,
    /// deslocado até <see cref="ArrivalNudge"/> casas por eixo para o centro dos portais do mapa de destino
    /// (nas capturas a chegada fica 4 a 9 casas para dentro). Portal sem destino resolvível fica de fora.
    /// </summary>
    private static FrozenDictionary<byte, IReadOnlyList<PortalInfo>> ResolvePortals(
        FrozenDictionary<byte, ClientMap> maps, Dictionary<(byte, short, short), PortalInfo> arrivals)
    {
        var byName = maps.Values.ToLookup(m => m.Name);
        var result = new Dictionary<byte, IReadOnlyList<PortalInfo>>();
        foreach (var map in maps.Values)
        {
            var list = new List<PortalInfo>();
            foreach (var c in map.Portals)
            {
                if (arrivals.TryGetValue((map.Id, c.X, c.Y), out var known))
                {
                    list.Add(known);
                    continue;
                }
                var candidates = byName[c.DestName].ToList();
                var dest = candidates.FirstOrDefault(m => m.Portals.Any(b => b.DestName == map.Name))
                           ?? (candidates.Count == 1 ? candidates[0] : null);
                var back = dest?.Portals.FirstOrDefault(b => b.DestName == map.Name);
                if (dest is null || back is null) continue;
                int cx = (int)Math.Round(dest.Portals.Average(b => b.X)), cy = (int)Math.Round(dest.Portals.Average(b => b.Y));
                list.Add(new PortalInfo(map.Id, c.X, c.Y, dest.Id,
                    (short)(back.X + Math.Clamp(cx - back.X, -ArrivalNudge, ArrivalNudge)),
                    (short)(back.Y + Math.Clamp(cy - back.Y, -ArrivalNudge, ArrivalNudge))));
            }
            if (list.Count > 0) result[map.Id] = list;
        }
        return result.ToFrozenDictionary();
    }

    /// <summary>Status fixos de um selvagem (HP, VP, ATK, DEF, BR, todos ou nenhum).</summary>
    private static Game.Stats.CombatStats? FixedStats(XElement o, int level)
    {
        if (o.Attribute("HP") is null) return null;
        int hp = I32(o, "HP"), vp = I32(o, "VP");
        return new Game.Stats.CombatStats(hp, vp, Game.Stats.StatFormulas.MaxEvp(level), I32(o, "ATK"), I32(o, "DEF"), I32(o, "BR"),
            Game.Stats.StatFormulas.MaxActionGauge);
    }

    private static IEnumerable<XElement> Elements(string root, string file, string rootName, string element)
    {
        var path = Path.Combine(root, file);
        if (!File.Exists(path)) throw new GameDataException($"tabela {path} não encontrada");
        var doc = XDocument.Load(path);
        var top = doc.Element(rootName) ?? throw new GameDataException($"{path}: falta a raiz <{rootName}>");
        return top.Elements(element);
    }

    private static string Attr(XElement e, string name) =>
        e.Attribute(name)?.Value ?? throw new GameDataException($"<{e.Name}> sem o atributo {name}");

    private static byte U8(XElement e, string name) => byte.Parse(Attr(e, name), CultureInfo.InvariantCulture);
    private static short I16(XElement e, string name) => short.Parse(Attr(e, name), CultureInfo.InvariantCulture);
    private static ushort U16(XElement e, string name) => ushort.Parse(Attr(e, name), CultureInfo.InvariantCulture);
    private static int I32(XElement e, string name) => int.Parse(Attr(e, name), CultureInfo.InvariantCulture);
    private static uint U32(XElement e, string name) => uint.Parse(Attr(e, name), CultureInfo.InvariantCulture);
    private static long I64(XElement e, string name) => long.Parse(Attr(e, name), CultureInfo.InvariantCulture);
    private static double F64(XElement e, string name) => double.Parse(Attr(e, name), CultureInfo.InvariantCulture);
}
