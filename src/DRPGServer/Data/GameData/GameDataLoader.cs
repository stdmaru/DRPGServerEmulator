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
        var items = ApplyEquipmentEffects(ClientTables.Items(pack), LoadEquipmentEffects(Path.Combine(root, "Item/EquipmentEffects.xml")));
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
            bool hasPoints = points.Count > 0;
            short xMin = hasPoints ? (short)0 : I16(e, "PosXMin");
            short xMax = hasPoints ? (short)0 : I16(e, "PosXMax");
            short yMin = hasPoints ? (short)0 : I16(e, "PosYMin");
            short yMax = hasPoints ? (short)0 : I16(e, "PosYMax");
            int maxCount = hasPoints ? points.Count : I32(e, "MaxCount");
            byte grade = e.Attribute("Grade") is not null ? U8(e, "Grade") : Game.Stats.WildFormulas.GradeBeginner;
            var area = new SpawnArea(U8(e, "MapID"), xMin, xMax, yMin, yMax,
                maxCount, TimeSpan.FromSeconds(F64(e, "RespawnTime")), options, points,
                (bool?)e.Attribute("Exclusive") ?? false, grade);
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

        var cards = ClientTables.Cards(pack);
        var npcShops = LoadNpcShops(root);

        return new GameData
        {
            Species = species,
            Items = items,
            Cards = cards,
            NpcShops = npcShops,
            CashShopProducts = ClientTables.CashShopProducts(pack),
            DigimonTitles = ClientTables.DigimonTitles(pack),
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

    /// <summary>
    /// Carrega os efeitos estruturados de equipamentos de <c>Resources/Item/EquipmentEffects.xml</c>
    /// (fonte autoritativa server-side, sem parsear texto de descrição do cliente em runtime).
    /// </summary>
    public static FrozenDictionary<uint, EquipmentBonus> LoadEquipmentEffects(string path)
    {
        if (!File.Exists(path)) return FrozenDictionary<uint, EquipmentBonus>.Empty;
        var doc = XDocument.Load(path);
        var top = doc.Element("EquipmentEffects") ?? throw new GameDataException($"{path}: falta a raiz <EquipmentEffects>");
        var effects = new Dictionary<uint, EquipmentBonus>();
        foreach (var e in top.Elements("Equipment"))
        {
            uint id = U32(e, "ItemID");
            var bonus = new EquipmentBonus(
                Attributes: new Game.Stats.Attributes(OptI32(e, "Str"), OptI32(e, "Agi"), OptI32(e, "Con"), OptI32(e, "Int")),
                BonusHp: OptI32(e, "Hp"),
                BonusVp: OptI32(e, "Vp"),
                BonusAtk: OptI32(e, "Atk"),
                BonusDef: OptI32(e, "Def"),
                BonusBr: OptI32(e, "Br"),
                BonusAtkPercent: OptI32(e, "AtkPercent"),
                BonusDefPercent: OptI32(e, "DefPercent"),
                BonusBrPercent: OptI32(e, "BrPercent"),
                CardSlashLimit: OptI32(e, "CardSlashLimit"),
                ExtraCardSlashes: OptI32(e, "ExtraCardSlashes"),
                DigimonExpPercent: OptI32(e, "DigimonExpPercent"),
                TamerExpFlat: OptI32(e, "TamerExp"),
                HpRecovery: OptI32(e, "HpRecovery"),
                VpRecovery: OptI32(e, "VpRecovery"),
                EvpRecovery: OptI32(e, "EvpRecovery"),
                MaintainEvolution: (bool?)e.Attribute("MaintainEvolution") ?? false,
                AutoSummonPartners: OptI32(e, "AutoSummonPartners"),
                BonusDamagePercent: OptI32(e, "DamagePercent"),
                SingleSkillDamagePercent: OptI32(e, "SingleSkillDamagePercent"),
                MultiSkillDamagePercent: OptI32(e, "MultiSkillDamagePercent"),
                MeleeSkillDamagePercent: OptI32(e, "MeleeSkillDamagePercent"),
                RangedSkillDamagePercent: OptI32(e, "RangedSkillDamagePercent"),
                OnHitHealHp: OptI32(e, "OnHitHealHp"),
                OnHitHealVp: OptI32(e, "OnHitHealVp"));
            if (!effects.TryAdd(id, bonus))
                throw new GameDataException($"EquipmentEffects.xml: ItemID {id} duplicado");
        }
        return effects.ToFrozenDictionary();
    }

    private static FrozenDictionary<uint, NpcShop> LoadNpcShops(string root)
    {
        var path = Path.Combine(root, "Item/NpcShop.xml");
        if (!File.Exists(path)) return FrozenDictionary<uint, NpcShop>.Empty;

        var shops = new Dictionary<uint, NpcShop>();
        foreach (var s in Elements(root, "Item/NpcShop.xml", "NpcShops", "Shop"))
        {
            uint id = U32(s, "ID");
            string kindStr = Attr(s, "Kind");
            var kind = kindStr switch
            {
                "Card" => NpcShopKind.Card,
                "Item" => NpcShopKind.Item,
                _ => throw new GameDataException($"NpcShop {id}: Kind '{kindStr}' inválido (esperado 'Card' ou 'Item')"),
            };
            string name = (string?)s.Attribute("Name") ?? "";
            var currency = (string?)s.Attribute("Currency") switch
            {
                null or "" or "Bits" => NpcShopCurrency.Bits,
                "Medals" => NpcShopCurrency.Medals,
                "Coins" => NpcShopCurrency.Coins,
                "LadderPoints" => NpcShopCurrency.LadderPoints,
                var other => throw new GameDataException($"NpcShop {id}: Currency '{other}' inválido (esperado Bits, Medals, Coins ou LadderPoints)"),
            };
            var deliverTo = (string?)s.Attribute("DeliverTo") switch
            {
                null or "" or "Inventory" => NpcShopDelivery.Inventory,
                "ShopDepository" => NpcShopDelivery.ShopDepository,
                var other => throw new GameDataException($"NpcShop {id}: DeliverTo '{other}' inválido (esperado Inventory ou ShopDepository)"),
            };
            var entries = s.Elements("Entry")
                .Select(e => new NpcShopEntry(
                    U32(e, "ID"),
                    e.Attribute("Price") is not null ? U32(e, "Price") : null))
                .ToList();
            if (entries.Count > NpcShop.MaxSlots)
                throw new GameDataException($"NpcShop {id}: {entries.Count} itens excede o máximo de {NpcShop.MaxSlots}");
            if (!shops.TryAdd(id, new NpcShop(id, kind, name, entries, currency, deliverTo)))
                throw new GameDataException($"NpcShop {id} duplicado");
        }
        return shops.ToFrozenDictionary();
    }

    private static FrozenDictionary<uint, ItemInfo> ApplyEquipmentEffects(
        FrozenDictionary<uint, ItemInfo> items, FrozenDictionary<uint, EquipmentBonus> effects)
    {
        if (effects.Count == 0) return items;
        var merged = new Dictionary<uint, ItemInfo>(items.Count);
        foreach (var (id, info) in items)
            merged[id] = effects.TryGetValue(id, out var bonus) ? info with { EquipmentBonus = bonus } : info;
        return merged.ToFrozenDictionary();
    }

    private static int OptI32(XElement e, string name) =>
        e.Attribute(name) is { } a ? int.Parse(a.Value, CultureInfo.InvariantCulture) : 0;

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
