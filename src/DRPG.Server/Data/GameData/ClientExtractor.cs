using DRPG.Shared.Formats.Pack;
namespace DRPG.Server.Data.GameData;

/// <summary>
/// Extrai do cliente só o que o servidor usa (docs/cliente/arquivos-de-dados.md): copia o pacote de tabelas e
/// gera o <c>grids.bin</c> com a grade de cada mapa do <c>map_us</c>. O <c>resource.pf</c> não é copiado.
/// </summary>
public static class ClientExtractor
{
    public const string GridsFile = "grids.bin";

    public sealed record Result(int Maps, int Grids, IReadOnlyList<string> Missing);

    public static Result Extract(string clientDir, string outDir)
    {
        var table = PackArchive.Open(Path.Combine(clientDir, "table.hf"));
        var resource = PackArchive.Open(Path.Combine(clientDir, "resource.hf"));
        table.CopyTo(outDir);

        var maps = ClientTables.Maps(table);
        var grids = new Dictionary<byte, MapGrid>();
        var missing = new List<string>();
        foreach (var map in maps.Values)
        {
            var name = $@"map\{map.FileName}.map";
            if (map.FileName is "" or "-" || !resource.Contains(name))
            {
                missing.Add($"{map.Id} {map.Name} ({map.FileName})");
                continue;
            }
            grids[map.Id] = MapGrid.FromClientMap(resource.ReadEntry(name));
        }
        MapGrid.Save(Path.Combine(outDir, GridsFile), grids);
        return new Result(maps.Count, grids.Count, missing);
    }
}
