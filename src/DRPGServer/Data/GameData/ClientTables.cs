using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
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
    public const string CardTable = @"table\card_info_us.dat";
    public const string DigimonTitleTable = @"table\digimon_title_us.dat";
    public const string MapTable = @"table\map_us.dat";
    public const string SkillTable = @"table\digimon_skill_us.dat";
    public const string TreeTable = @"table\tree_us.dat";
    public const string CashShopPageTable = @"table\ingameshop_itempage_us.dat";
    public const string CashShopDataTable = @"table\ingameshop_item_data_us.dat";
    public const string NpcTable = @"table\npc_info_us.dat";

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
                UShort(f, 5), UShort(f, 9), Byte(f, 22)));
        }
        return species.ToFrozenDictionary();
    }

    /// <summary>
    /// Itens: <c>item_info_us</c> (33 colunas, índices 0..32). Linhas com id 0 são cartas sem id e ficam de fora.
    /// ✅ Colunas que o registro de 108 bytes repete (sessões 52–61): 2 tipo, 3 subtipo, 15, 16, 17 preço, 18 venda,
    /// 20 nível, 23, 24, 26 pilha máxima (1..999; &gt; 999 é duração em segundos de item temporário), 32 (ver docs/protocolo/structs/inventario.md).
    /// </summary>
    public static FrozenDictionary<uint, ItemInfo> Items(ClientPack pack)
    {
        var items = new Dictionary<uint, ItemInfo>();
        foreach (var f in pack.ReadTable(ItemTable))
            if (f.Length > 1 && uint.TryParse(f[0], CultureInfo.InvariantCulture, out var id) && id != 0)
            {
                uint col26 = UInt(f, 26);
                items.TryAdd(id, new ItemInfo(id, f[1])
                {
                    Type = Byte(f, 2), SubType = Byte(f, 3),
                    Col16 = UShort(f, 15), Col17 = UInt(f, 16), BuyPrice = UInt(f, 17), SellPrice = UInt(f, 18),
                    Level = UInt(f, 20), Flag24 = Byte(f, 23), Flag25 = Byte(f, 24),
                    MaxStack = col26 is >= 1 and <= 999 ? col26 : 1u,
                    DurationSeconds = col26 > 999 ? col26 : 0u,
                    Flag33 = Byte(f, 32),
                });
            }
        return items.ToFrozenDictionary();
    }

    /// <summary>
    /// Cards: <c>card_info_us</c> (31 colunas, índices 0..30). ✅ Colunas mapeadas contra capturas v322 (0x02/0x03/0x90):
    /// 0 id, 1 nome, 2 tipo, 3 subtipo, 12 preço, 13 venda, 19 flag, 21 pilha máxima (999), 23 momento de ativação
    /// (1 ataque / 2 defesa), 24 restrição (0 normal, 1 PvE/evento, 3 Battle Zone), 25 descrição, 26..30 flags finais.
    /// </summary>
    public static FrozenDictionary<uint, CardInfo> Cards(ClientPack pack)
    {
        var cards = new Dictionary<uint, CardInfo>();
        if (!pack.Contains(CardTable)) return cards.ToFrozenDictionary();
        foreach (var f in pack.ReadTable(CardTable))
            if (f.Length > 24 && uint.TryParse(f[0], CultureInfo.InvariantCulture, out var id) && id != 0)
            {
                uint col21 = UInt(f, 21);
                byte trigger = Byte(f, 23);
                string description = f.Length > 25 && f[25].Length > 0 ? f[25] : (f.Length > 4 ? f[4] : "");
                cards.TryAdd(id, new CardInfo(id, f[1])
                {
                    Type = Byte(f, 2),
                    SubType = Byte(f, 3),
                    BuyPrice = UInt(f, 12),
                    SellPrice = UInt(f, 13),
                    Flag19 = Byte(f, 19),
                    MaxStack = col21 is >= 1 and <= 999 ? col21 : 999u,
                    Trigger = trigger == (byte)CardTrigger.Defense ? CardTrigger.Defense : CardTrigger.Attack,
                    Restriction = Byte(f, 24),
                    Col26 = UShort(f, 26),
                    Col27 = Byte(f, 27),
                    Col28 = UInt(f, 28),
                    Col29 = Byte(f, 29),
                    Col30 = Byte(f, 30),
                    Description = description,
                });
            }
        return cards.ToFrozenDictionary();
    }

    /// <summary>
    /// Títulos de Digimon: <c>digimon_title_us</c> (0 id, 1 nome, 3..13 descrição e linhas de bônus "※ ...").
    /// ✅ Extrai bônus de atributos (STR/AGI/CON/INT/All stats) e combate (HP/VP/ATK/DEF/BR flat e %).
    /// </summary>
    public static FrozenDictionary<uint, DigimonTitleInfo> DigimonTitles(ClientPack pack)
    {
        var titles = new Dictionary<uint, DigimonTitleInfo>();
        if (!pack.Contains(DigimonTitleTable)) return titles.ToFrozenDictionary();
        foreach (var f in pack.ReadTable(DigimonTitleTable))
        {
            if (f.Length < 4 || !uint.TryParse(f[0], CultureInfo.InvariantCulture, out var id) || id == 0) continue;
            int str = 0, agi = 0, con = 0, intl = 0;
            int hp = 0, vp = 0, atk = 0, def = 0, br = 0;
            int atkPct = 0, defPct = 0, brPct = 0;

            int end = Math.Min(f.Length, 14);
            for (int col = 3; col < end; col++)
            {
                string line = f[col];
                if (string.IsNullOrWhiteSpace(line)) continue;
                foreach (Match m in TitleStatRegex.Matches(line))
                {
                    string stat = m.Groups[1].Value.Trim().ToUpperInvariant();
                    int val = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    bool isPercent = m.Groups[3].Value == "%";
                    switch (stat)
                    {
                        case "ALL STATS":
                            str += val; agi += val; con += val; intl += val;
                            break;
                        case "STR": str += val; break;
                        case "AGI": agi += val; break;
                        case "CON": con += val; break;
                        case "INT": intl += val; break;
                        case "HP" or "MAX HP": hp += val; break;
                        case "VP" or "MAX VP": vp += val; break;
                        case "ATK":
                            if (isPercent) atkPct += val; else atk += val;
                            break;
                        case "DEF":
                            if (isPercent) defPct += val; else def += val;
                            break;
                        case "BR" or "BATTLE RATING":
                            if (isPercent) brPct += val; else br += val;
                            break;
                    }
                }
            }

            titles.TryAdd(id, new DigimonTitleInfo(id, f[1])
            {
                AttributeBonus = new Attributes(str, agi, con, intl),
                BonusHp = hp,
                BonusVp = vp,
                BonusAtk = atk,
                BonusDef = def,
                BonusBr = br,
                BonusAtkPercent = atkPct,
                BonusDefPercent = defPct,
                BonusBrPercent = brPct,
            });
        }
        return titles.ToFrozenDictionary();
    }

    private static readonly Regex TitleStatRegex = new(
        @"(All stats|Max HP|Max VP|Battle Rating|STR|AGI|CON|INT|HP|VP|ATK|DEF|BR)\s*\+?\s*(\d+)\s*(%?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Skills: <c>digimon_skill_us</c> (0 id, 1 nível para aprender, 3 nome, 4 tipo [1 melee / 2 ranged], 5 alvos [1 / 2 / 5]).
    /// ✅ O nível da coluna 1 é o u16 depois do id no bloco de status, a skill passa a nível 1 quando o digimon chega nele,
    /// e o custo é 20 + esse nível em VP (sessão 56). Sem a tabela (pacote de testes antigo), nenhuma skill.
    /// </summary>
    public static FrozenDictionary<ushort, SkillInfo> Skills(ClientPack pack)
    {
        var skills = new Dictionary<ushort, SkillInfo>();
        if (!pack.Contains(SkillTable)) return skills.ToFrozenDictionary();
        foreach (var f in pack.ReadTable(SkillTable))
            if (f.Length > 4 && ushort.TryParse(f[0], CultureInfo.InvariantCulture, out var id) && id != 0)
            {
                byte type = Byte(f, 4);
                byte targetCount = Byte(f, 5);
                skills.TryAdd(id, new SkillInfo(
                    id,
                    f.Length > 3 ? f[3] : "",
                    UShort(f, 1),
                    type > 0 ? type : SkillInfo.TypeMelee,
                    targetCount > 0 ? targetCount : (byte)1));
            }
        return skills.ToFrozenDictionary();
    }

    /// <summary>
    /// Linhas evolutivas: <c>tree_us</c>, uma linha por ovo e variante com as espécies de in-training a mega nas
    /// colunas 6, 10, 14, 18 e 22 (✅ ovo 6: Hopmon → Monodramon → Strikedramon → Cyberdramon → Justimon; ovo 48:
    /// V-mon → XV-mon). Devolve espécie → próxima forma. ✅ 309 de 319 espécies têm um só sucessor; nas outras vale o
    /// mais frequente.
    /// </summary>
    public static FrozenDictionary<ushort, ushort> Evolutions(ClientPack pack)
    {
        var counts = new Dictionary<ushort, Dictionary<ushort, int>>();
        if (pack.Contains(TreeTable))
            foreach (var f in pack.ReadTable(TreeTable))
            {
                if (f.Length < 23) continue;
                ushort[] chain = [UShort(f, 6), UShort(f, 10), UShort(f, 14), UShort(f, 18), UShort(f, 22)];
                for (int i = 0; i + 1 < chain.Length; i++)
                {
                    if (chain[i] == 0 || chain[i + 1] == 0) continue;
                    var next = counts.TryGetValue(chain[i], out var c) ? c : counts[chain[i]] = [];
                    next[chain[i + 1]] = next.GetValueOrDefault(chain[i + 1]) + 1;
                }
            }
        return counts.ToFrozenDictionary(kv => kv.Key, kv => kv.Value.MaxBy(n => n.Value).Key);
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

    /// <summary>✅ NPCs: <c>npc_info_us</c> (0 mapa, 1 id, 2 nome, 4/5 posição, 10 janela, 12 loja). Ver <see cref="NpcInfo"/>.</summary>
    public static NpcDirectory Npcs(ClientPack pack)
    {
        var npcs = new List<NpcInfo>();
        foreach (var f in pack.ReadTable(NpcTable))
        {
            if (f.Length < 13 || !byte.TryParse(f[0], CultureInfo.InvariantCulture, out var map)
                || !ushort.TryParse(f[1], CultureInfo.InvariantCulture, out var id))
                continue;
            ushort.TryParse(f[10], CultureInfo.InvariantCulture, out var window);
            uint.TryParse(f[12], CultureInfo.InvariantCulture, out var shop);
            npcs.Add(new NpcInfo(map, id, f[2], Short(f, 4), Short(f, 5), (NpcWindow)window, shop));
        }
        return new NpcDirectory(npcs);
    }

    /// <summary>
    /// ✅ Catálogo do Cash Shop: <c>ingameshop_itempage_us.dat</c> (0 productId, 1 nome, 2 itemDataId, 5 preço, 8 categoria)
    /// combinado com <c>ingameshop_item_data_us.dat</c> (0 itemDataId, 1 tipo [1=Card, 2=Item], 3 itemId, 4 quantidade).
    /// </summary>
    public static FrozenDictionary<uint, CashShopProduct> CashShopProducts(ClientPack pack)
    {
        if (!pack.Contains(CashShopPageTable) || !pack.Contains(CashShopDataTable))
            return FrozenDictionary<uint, CashShopProduct>.Empty;

        var entriesByDataId = new Dictionary<uint, List<CashShopEntry>>();
        foreach (var f in pack.ReadTable(CashShopDataTable))
        {
            if (f.Length < 5 || !uint.TryParse(f[0], CultureInfo.InvariantCulture, out var dataId) || dataId == 0) continue;
            byte kindByte = Byte(f, 1);
            if (kindByte is not (byte)CashShopItemKind.Card and not (byte)CashShopItemKind.Item) continue;
            uint itemId = UInt(f, 3);
            uint amount = Math.Max(1u, UInt(f, 4));
            if (itemId == 0) continue;
            if (!entriesByDataId.TryGetValue(dataId, out var list))
                entriesByDataId[dataId] = list = [];
            list.Add(new CashShopEntry((CashShopItemKind)kindByte, itemId, amount));
        }

        var products = new Dictionary<uint, CashShopProduct>();
        foreach (var f in pack.ReadTable(CashShopPageTable))
        {
            if (f.Length < 9 || !uint.TryParse(f[0], CultureInfo.InvariantCulture, out var productId) || productId == 0) continue;
            uint dataId = UInt(f, 2);
            uint price = UInt(f, 5);
            byte category = Byte(f, 8);
            if (!entriesByDataId.TryGetValue(dataId, out var entries) || entries.Count == 0) continue;
            // ✅ O cliente envia ItemDataId (col. 2 de itempage = col. 0 de item_data) em C2S 0x0160 (+0x18; DigimonRPG.bin @ 0x0096C386 / 0x0096C424)
            products.TryAdd(dataId, new CashShopProduct(productId, f[1], dataId, price, category, entries));
        }
        return products.ToFrozenDictionary();
    }

    private static Attributes Growth(string[] f) => new(Int(f, 15), Int(f, 16), Int(f, 17), Int(f, 18));

    private static int Int(string[] f, int i) => int.TryParse(f[i], CultureInfo.InvariantCulture, out var v) ? v : 0;
    private static short Short(string[] f, int i) => short.TryParse(f[i], CultureInfo.InvariantCulture, out var v) ? v : (short)0;
    private static byte Byte(string[] f, int i) => i < f.Length && byte.TryParse(f[i], CultureInfo.InvariantCulture, out var v) ? v : (byte)0;
    private static uint UInt(string[] f, int i) => i < f.Length && uint.TryParse(f[i], CultureInfo.InvariantCulture, out var v) ? v : 0;
    private static ushort UShort(string[] f, int i) => i < f.Length && ushort.TryParse(f[i], CultureInfo.InvariantCulture, out var v) ? v : (ushort)0;
}
