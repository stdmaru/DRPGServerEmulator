using System.Collections.Frozen;
using System.Globalization;
using DRPGServer.Game.Stats;

namespace DRPGServer.Data.GameData;

/// <summary>Um mapa de <c>map_us</c>, com os portais como o cliente os descreve.</summary>
/// <paramref name="FileName"/>: nome do arquivo do mapa no <c>resource.pf</c> (<c>map\&lt;nome&gt;.map</c>), coluna 4.
public sealed record ClientMap(byte Id, string Name, string FileName, short WorldX, short WorldY, IReadOnlyList<ClientPortal> Portals);

/// <summary>Portal em <c>map_us</c>: posição no mapa e nome do mapa de destino (o cliente não traz o ponto de chegada).</summary>
public sealed record ClientPortal(short X, short Y, string DestName);

/// <summary>
/// Tabelas lidas do pacote do cliente (<c>table.hf</c>/<c>table.pf</c>). Colunas em docs/cliente/arquivos-de-dados.md.
/// </summary>
public static class ClientTables
{
    public const string DigimonTable = @"table\digimon_us.dat";
    public const string DexTable = @"table\dex_us.dat";
    public const string ItemTable = @"table\item_info_us.dat";
    public const string MapTable = @"table\map_us.dat";

    /// <summary>
    /// Espécies: <c>digimon_us</c> (0 id, 1 nome, 2 estágio, 3 atributo, 5 skill 1, 9 skill 2) e o crescimento
    /// de <c>dex_us</c> (0 id, 1 nome, 15..18 STR/AGI/CON/INT).
    /// </summary>
    public static FrozenDictionary<ushort, DigimonSpecies> Species(ClientPack pack)
    {
        var dex = pack.ReadTable(DexTable).Where(f => f.Length > 18 && ushort.TryParse(f[0], out _)).ToList();
        var dexById = new Dictionary<ushort, string[]>();
        foreach (var f in dex) dexById.TryAdd(UShort(f, 0), f);
        // nome → linha, só quando todas as linhas com esse nome têm o mesmo crescimento
        var dexByName = dex.GroupBy(f => f[1])
            .Where(g => g.Select(Growth).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First());

        var species = new Dictionary<ushort, DigimonSpecies>();
        foreach (var f in pack.ReadTable(DigimonTable))
        {
            if (f.Length < 10 || !ushort.TryParse(f[0], out var id) || id == 0) continue;
            dexById.TryGetValue(id, out var byId);
            var name = f[1].Length > 0 ? f[1] : byId?[1] ?? "";
            if (name.Length == 0) continue;
            // ✅ a dex usa o mesmo id da espécie quase sempre; se o nome não bate, procura pelo nome
            var row = byId is not null && byId[1] == name ? byId : dexByName.GetValueOrDefault(name) ?? byId;
            species.TryAdd(id, new DigimonSpecies(id, name, Byte(f, 2), Byte(f, 3), row is null ? null : Growth(row),
                UShort(f, 5), UShort(f, 9)));
        }
        return species.ToFrozenDictionary();
    }

    /// <summary>Itens: <c>item_info_us</c> (0 id, 1 nome). Linhas com id 0 são cartas sem id e ficam de fora.</summary>
    public static FrozenDictionary<uint, ItemInfo> Items(ClientPack pack)
    {
        var items = new Dictionary<uint, ItemInfo>();
        foreach (var f in pack.ReadTable(ItemTable))
            if (f.Length > 1 && uint.TryParse(f[0], CultureInfo.InvariantCulture, out var id) && id != 0)
                items.TryAdd(id, new ItemInfo(id, f[1]));
        return items.ToFrozenDictionary();
    }

    /// <summary>
    /// Mapas: <c>map_us</c> (0 id, 1 nome, 4 arquivo do mapa, 12/13 posição no mapa-múndi, 16..31 quatro portais de
    /// (nome do destino, ?, x, y); "Entire" é slot vazio). Linhas com id 0 são entradas genéricas.
    /// </summary>
    public static FrozenDictionary<byte, ClientMap> Maps(ClientPack pack)
    {
        var maps = new Dictionary<byte, ClientMap>();
        foreach (var f in pack.ReadTable(MapTable))
        {
            if (f.Length < 32 || !byte.TryParse(f[0], CultureInfo.InvariantCulture, out var id) || id == 0) continue;
            var portals = new List<ClientPortal>();
            for (int k = 16; k + 3 < 32; k += 4)
                if (f[k].Length > 0 && f[k] != "Entire")
                    portals.Add(new ClientPortal(Short(f, k + 2), Short(f, k + 3), f[k]));
            maps.TryAdd(id, new ClientMap(id, f[1], f[4], Short(f, 12), Short(f, 13), portals));
        }
        return maps.ToFrozenDictionary();
    }

    private static Attributes Growth(string[] f) => new(Int(f, 15), Int(f, 16), Int(f, 17), Int(f, 18));

    private static int Int(string[] f, int i) => int.TryParse(f[i], CultureInfo.InvariantCulture, out var v) ? v : 0;
    private static short Short(string[] f, int i) => short.TryParse(f[i], CultureInfo.InvariantCulture, out var v) ? v : (short)0;
    private static byte Byte(string[] f, int i) => byte.TryParse(f[i], CultureInfo.InvariantCulture, out var v) ? v : (byte)0;
    private static ushort UShort(string[] f, int i) => ushort.TryParse(f[i], CultureInfo.InvariantCulture, out var v) ? v : (ushort)0;
}
