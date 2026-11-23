using System.Buffers.Binary;
using System.Collections.Frozen;

namespace DRPGServer.Data.GameData;

/// <summary>
/// Grade de células de um mapa: onde dá para pisar. Coordenadas = as do protocolo (x, y), uma por célula.
/// Formato do <c>.map</c> do cliente em docs/cliente/mapas.md.
/// </summary>
public sealed class MapGrid
{
    private readonly byte[] _walk;   // 1 bit por célula, linha a linha (y·Width + x), bit menos significativo primeiro

    public MapGrid(int width, int height, byte[] walk)
    {
        if (walk.Length != (width * height + 7) / 8) throw new GameDataException("grade: tamanho do bitmap não bate");
        Width = width;
        Height = height;
        _walk = walk;
    }

    public int Width { get; }
    public int Height { get; }

    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public bool IsWalkable(int x, int y)
    {
        if (!InBounds(x, y)) return false;
        int i = y * Width + x;
        return (_walk[i >> 3] & (1 << (i & 7))) != 0;
    }

    /// <summary>
    /// ✅ A célula ou uma vizinha (8 direções) é andável. Todas as 1696 posições capturadas (jogador e selvagens)
    /// passam nesse teste; ~5 % caem numa célula bloqueada encostada em área livre.
    /// </summary>
    public bool IsNearWalkable(int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (IsWalkable(x + dx, y + dy)) return true;
        return false;
    }

    /// <summary>
    /// Lê o <c>.map</c> do cliente: u32 largura, u32 altura, u32 64, u32 32 (pixels do tile), depois
    /// largura × altura células de 24 bytes (6 × u32). ✅ Bit 0 do 6º u32 = andável.
    /// </summary>
    public static MapGrid FromClientMap(ReadOnlySpan<byte> data)
    {
        int w = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
        int h = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        if (w <= 0 || h <= 0 || w > 4096 || h > 4096 || 16 + 24L * w * h > data.Length)
            throw new GameDataException($"mapa do cliente com tamanho inválido ({w} × {h})");
        var walk = new byte[(w * h + 7) / 8];
        for (int i = 0; i < w * h; i++)
            if ((data[16 + 24 * i + 20] & 1) != 0)
                walk[i >> 3] |= (byte)(1 << (i & 7));
        return new MapGrid(w, h, walk);
    }

    // ---------- grids.bin: "DRGD", u32 versão, u32 n, n × (u8 mapa, u16 largura, u16 altura, bitmap) ----------

    private const uint Magic = 0x44475244; // "DRGD"
    private const uint Version = 1;

    public static void Save(string path, IReadOnlyDictionary<byte, MapGrid> grids)
    {
        using var f = File.Create(path);
        using var w = new BinaryWriter(f);
        w.Write(Magic);
        w.Write(Version);
        w.Write(grids.Count);
        foreach (var (id, g) in grids.OrderBy(p => p.Key))
        {
            w.Write(id);
            w.Write((ushort)g.Width);
            w.Write((ushort)g.Height);
            w.Write(g._walk);
        }
    }

    public static FrozenDictionary<byte, MapGrid> Load(string path)
    {
        using var f = File.OpenRead(path);
        using var r = new BinaryReader(f);
        if (r.ReadUInt32() != Magic || r.ReadUInt32() != Version) throw new GameDataException($"{path}: não é um grids.bin v{Version}");
        int n = r.ReadInt32();
        var grids = new Dictionary<byte, MapGrid>();
        for (int i = 0; i < n; i++)
        {
            byte id = r.ReadByte();
            int w = r.ReadUInt16(), h = r.ReadUInt16();
            grids[id] = new MapGrid(w, h, r.ReadBytes((w * h + 7) / 8));
        }
        return grids.ToFrozenDictionary();
    }
}
