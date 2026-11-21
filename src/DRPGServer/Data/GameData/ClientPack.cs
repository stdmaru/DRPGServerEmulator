using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace DRPGServer.Data.GameData;

/// <summary>
/// Leitura do pacote de tabelas do cliente (<c>table.hf</c> + <c>table.pf</c>, cópia v322 em Resources/Client, fora do git).
/// Só leitura. Formato e codecs em
/// docs/cliente/arquivos-de-dados.md; implementação de referência em work/tools/drpg_pack.py.
/// </summary>
public sealed class ClientPack
{
    private readonly string _pfPath;
    private readonly Dictionary<string, (long Offset, int Size)> _entries;

    private ClientPack(string pfPath, Dictionary<string, (long, int)> entries)
    {
        _pfPath = pfPath;
        _entries = entries;
    }

    public IReadOnlyCollection<string> Names => _entries.Keys;

    /// <summary>Abre o par .hf/.pf. <paramref name="hfPath"/> pode ser o .hf ou o .pf.</summary>
    public static ClientPack Open(string hfPath)
    {
        var basePath = Path.ChangeExtension(hfPath, null);
        string hf = basePath + ".hf", pf = basePath + ".pf";
        if (!File.Exists(hf) || !File.Exists(pf))
            throw new GameDataException($"pacote do cliente não encontrado: {hf} / {pf}");

        var d = File.ReadAllBytes(hf);
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(d);
        int size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(12)));
        var index = Inflate(DecodeIndex(version, d.AsSpan(16, size).ToArray()));

        var entries = new Dictionary<string, (long, int)>(StringComparer.OrdinalIgnoreCase);
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(index);
        int p = 4;
        for (int i = 0; i < count; i++)
        {
            int n = (int)BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(p));
            p += 4;
            var name = Encoding.Unicode.GetString(index, p, 2 * n);
            p += 2 * n;
            long offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(index.AsSpan(p));
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(p + 8));
            p += 12;
            entries[Normalize(name)] = (offset, length);
        }
        return new ClientPack(pf, entries);
    }

    /// <summary>
    /// Lê uma tabela <c>.dat</c> (ex.: <c>table\digimon_us.dat</c>) como linhas de campos separados por <c>|</c>.
    /// </summary>
    public IReadOnlyList<string[]> ReadTable(string name)
    {
        if (!_entries.TryGetValue(Normalize(name), out var entry))
            throw new GameDataException($"tabela {name} não está no pacote do cliente");

        var raw = new byte[entry.Size];
        using (var f = File.OpenRead(_pfPath))
        {
            f.Position = entry.Offset;
            f.ReadExactly(raw);
        }
        var plain = Inflate(Codec.OldDecode(Inflate(raw)));

        var rows = new List<string[]>();
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(plain);
        int p = 4;
        for (int i = 0; i < count; i++)
        {
            int n = (int)BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(p));
            p += 4;
            rows.Add(Encoding.Unicode.GetString(plain, p, 2 * n).Split('|'));
            p += 2 * n;
        }
        return rows;
    }

    private static string Normalize(string name) => name.Replace('/', '\\').TrimStart('\\');

    private static byte[] DecodeIndex(uint version, byte[] payload) => version switch
    {
        0x30 => Codec.Xmask(Codec.Revnot(payload)),
        0x20 => DecodeKeyedIndex(payload),
        0x11 => Codec.OldDecode(payload, flags: 5),
        _ => throw new GameDataException($"versão de .hf não suportada: 0x{version:X}"),
    };

    private static byte[] DecodeKeyedIndex(byte[] payload)
    {
        var (key, data, _) = Codec.ReadKeyed(Codec.Xmask(payload));
        return Codec.Revnot(Codec.XorKey(key, data));
    }

    private static byte[] Inflate(byte[] data)
    {
        using var input = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Estágios de codificação do cliente (nomes e funções como em drpg_pack.py).</summary>
    internal static class Codec
    {
        private static uint[] Words(byte[] b, out int n)
        {
            n = b.Length / 4;
            var w = new uint[n];
            for (int i = 0; i < n; i++) w[i] = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4 * i));
            return w;
        }

        private static byte[] Pack(uint[] w, byte[] source)
        {
            var out_ = new byte[source.Length];
            for (int i = 0; i < w.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(out_.AsSpan(4 * i), w[i]);
            source.AsSpan(w.Length * 4).CopyTo(out_.AsSpan(w.Length * 4));
            return out_;
        }

        /// <summary>Inverte a ordem dos dwords e aplica NOT.</summary>
        public static byte[] Revnot(byte[] b)
        {
            var w = Words(b, out int n);
            var r = new uint[n];
            for (int i = 0; i < n; i++) r[i] = ~w[n - 1 - i];
            return Pack(r, b);
        }

        /// <summary>XOR alternado 0xFFFF0000 / 0x0000FFFF.</summary>
        public static byte[] Xmask(byte[] b)
        {
            var w = Words(b, out int n);
            for (int i = 0; i < n; i++) w[i] ^= (i & 1) == 0 ? 0xFFFF0000u : 0x0000FFFFu;
            return Pack(w, b);
        }

        /// <summary>Rotação à direita do dword i por 1..31 (cíclico).</summary>
        public static byte[] RotRight(byte[] b)
        {
            var w = Words(b, out int n);
            int s = 1;
            for (int i = 0; i < n; i++)
            {
                if (s >= 32) s = 1;
                w[i] = (w[i] >> s) | (w[i] << (32 - s));
                s++;
            }
            return Pack(w, b);
        }

        /// <summary>XOR por dword com key[i % 16]; o resto parcial vai nos bytes altos de mais um dword.</summary>
        public static byte[] XorKey(uint[] key, byte[] data)
        {
            var w = Words(data, out int n);
            for (int i = 0; i < n; i++) w[i] ^= key[i % 16];
            var out_ = new byte[data.Length];
            for (int i = 0; i < n; i++) BinaryPrimitives.WriteUInt32LittleEndian(out_.AsSpan(4 * i), w[i]);
            int r = data.Length % 4;
            if (r != 0)
            {
                var tmp = new byte[4];
                data.AsSpan(n * 4).CopyTo(tmp.AsSpan(4 - r));
                uint t = BinaryPrimitives.ReadUInt32LittleEndian(tmp) ^ key[n % 16];
                BinaryPrimitives.WriteUInt32LittleEndian(tmp, t);
                tmp.AsSpan(4 - r).CopyTo(out_.AsSpan(n * 4));
            }
            return out_;
        }

        /// <summary>Por bloco de 16 bytes, troca blk[k &amp; 15] com blk[k &gt;&gt; 4] para cada byte da chave (decodificação).</summary>
        public static byte[] NibSwap(uint[] key, byte[] data)
        {
            var kb = new byte[key.Length * 4];
            for (int i = 0; i < key.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(kb.AsSpan(4 * i), key[i]);
            var out_ = (byte[])data.Clone();
            for (int o = 0; o + 16 <= data.Length; o += 16)
                foreach (var k in kb)
                {
                    int i = o + (k & 15), j = o + (k >> 4);
                    (out_[i], out_[j]) = (out_[j], out_[i]);
                }
            return out_;
        }

        /// <summary>u32 n, n × u32 chave, u32 tamanho, dados.</summary>
        public static (uint[] Key, byte[] Data, int Used) ReadKeyed(byte[] b)
        {
            int nk = (int)BinaryPrimitives.ReadUInt32LittleEndian(b);
            var key = new uint[nk];
            for (int i = 0; i < nk; i++) key[i] = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4 + 4 * i));
            int p = 4 + 4 * nk;
            int nd = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p));
            p += 4;
            if (nk == 0 || p + nd > b.Length) throw new GameDataException("pacote do cliente: bloco com chave inválido");
            return (key, b.AsSpan(p, nd).ToArray(), p + nd);
        }

        /// <summary>Codec antigo das tabelas .dat (flags 0xFF): nibble-swap, xmask, revnot, rotação, xor com chave.</summary>
        public static byte[] OldDecode(byte[] b, int flags = 0xFF)
        {
            if ((flags & 0x10) != 0)
            {
                var (key, data, used) = ReadKeyed(b);
                if (used != b.Length) throw new GameDataException("pacote do cliente: dados sobrando");
                b = NibSwap(key, data);
            }
            if ((flags & 0x08) != 0) b = Xmask(b);
            if ((flags & 0x04) != 0) b = Revnot(b);
            if ((flags & 0x02) != 0) b = RotRight(b);
            if ((flags & 0x01) != 0)
            {
                var (key, data, _) = ReadKeyed(b);
                b = XorKey(key, data);
            }
            return b;
        }
    }
}
