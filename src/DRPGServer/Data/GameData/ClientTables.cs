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
                byte type = Byte(f, 2);
                string desc = f.Length > 28 ? f[28] : "";
                string extraDesc = string.Join(' ', f.Skip(4).Take(Math.Min(9, Math.Max(0, f.Length - 4))).Where(s => !string.IsNullOrWhiteSpace(s)));
                items.TryAdd(id, new ItemInfo(id, f[1])
                {
                    Type = type, SubType = Byte(f, 3),
                    Col16 = UShort(f, 15), Col17 = UInt(f, 16), BuyPrice = UInt(f, 17), SellPrice = UInt(f, 18),
                    Level = UInt(f, 20), Flag24 = Byte(f, 23), Flag25 = Byte(f, 24),
                    MaxStack = col26 is >= 1 and <= 999 ? col26 : 1u, Flag33 = Byte(f, 32),
                    Description = desc,
                    EquipmentBonus = IsEquipmentItemType(type) ? ParseEquipmentBonus(desc, extraDesc, id) : EquipmentBonus.Empty,
                });
            }
        return items.ToFrozenDictionary();
    }

    private static bool IsEquipmentItemType(byte type) => type switch
    {
        1 or 2 or 8 or 9 or 27 => true,
        >= 15 and <= 21 => true,
        23 or 24 or 25 or 28 => true,
        37 or 38 => true,
        >= 40 and <= 45 => true,
        _ => false,
    };

    /// <summary>
    /// Extrai os bônus de equipamento da coluna 28 (e colunas 4..12 para Auras como <c>Aura of High-Velocity</c>)
    /// de <c>item_info_us.dat</c>.
    /// </summary>
    public static EquipmentBonus ParseEquipmentBonus(string description, string extraDescription = "", uint itemId = 0)
    {
        int str = 0, agi = 0, con = 0, intl = 0;
        int hp = 0, vp = 0, atk = 0, def = 0, br = 0;
        int atkPct = 0, defPct = 0, brPct = 0;
        int cardSlash = 0, extraCardSlashes = 0, digimonExpPct = 0, tamerExp = 0;
        int hpRec = 0, vpRec = 0, evpRec = 0;
        bool maintainEvolution = itemId == 555; // Aura of Evolution (col 28 vazia na tabela)
        int autoSummonPartners = 0;
        int bonusDamagePct = 0, singleSkillPct = 0, multiSkillPct = 0, meleeSkillPct = 0, rangedSkillPct = 0;
        int onHitHealHp = 0, onHitHealVp = 0;

        if (!string.IsNullOrWhiteSpace(extraDescription)
            && extraDescription.Contains("summons the leader and Partner Digimon", StringComparison.OrdinalIgnoreCase))
        {
            autoSummonPartners += 1;
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            foreach (string rawPart in description.Split('/'))
            {
                string part = rawPart.Trim();
                if (part.Length == 0) continue;

                if (part.Contains("maintains the Digimon's current Evolution Stage", StringComparison.OrdinalIgnoreCase))
                {
                    maintainEvolution = true;
                    continue;
                }
                if (CardSlashRegex.Match(part) is { Success: true } cs)
                {
                    cardSlash = Math.Max(cardSlash, int.Parse(cs.Groups[1].Value, CultureInfo.InvariantCulture));
                    continue;
                }
                if (ExtraCardSlashRegex.Match(part) is { Success: true } ecs)
                {
                    extraCardSlashes += int.Parse(ecs.Groups[1].Value, CultureInfo.InvariantCulture);
                    continue;
                }
                if (SummonMoreRegex.Match(part) is { Success: true } sm)
                {
                    autoSummonPartners += int.Parse(sm.Groups[1].Value, CultureInfo.InvariantCulture);
                    continue;
                }
                if (DigimonExpRegex.Match(part) is { Success: true } de)
                {
                    digimonExpPct += int.Parse(de.Groups[1].Value, CultureInfo.InvariantCulture);
                    continue;
                }
                if (TamerExpRegex.Match(part) is { Success: true } te)
                {
                    tamerExp += int.Parse(te.Groups[1].Value, CultureInfo.InvariantCulture);
                    continue;
                }
                if (RecoveryRegex.Match(part) is { Success: true } rec)
                {
                    int val = int.Parse(rec.Groups[2].Value, CultureInfo.InvariantCulture);
                    switch (rec.Groups[1].Value.ToUpperInvariant())
                    {
                        case "HP": hpRec += val; break;
                        case "VP": vpRec += val; break;
                        case "EVP": evpRec += val; break;
                    }
                    continue;
                }
                if (SkillDamageRegex.Match(part) is { Success: true } sd)
                {
                    int val = int.Parse(sd.Groups[2].Value, CultureInfo.InvariantCulture);
                    switch (sd.Groups[1].Value.ToUpperInvariant())
                    {
                        case "SINGLE": singleSkillPct += val; break;
                        case "MULTI": multiSkillPct += val; break;
                        case "MELEE": meleeSkillPct += val; break;
                        case "RANGED": rangedSkillPct += val; break;
                    }
                    continue;
                }
                if (GeneralDamageRegex.Match(part) is { Success: true } gd)
                {
                    bonusDamagePct += int.Parse(gd.Groups[1].Value, CultureInfo.InvariantCulture);
                    continue;
                }
                if (OnHitHealRegex.Match(part) is { Success: true } oh)
                {
                    int val = int.Parse(oh.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (oh.Groups[2].Value.Equals("HP", StringComparison.OrdinalIgnoreCase)) onHitHealHp += val;
                    else onHitHealVp += val;
                    continue;
                }

                foreach (Match m in TitleStatRegex.Matches(part))
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
        }

        return new EquipmentBonus(
            Attributes: new Attributes(str, agi, con, intl),
            BonusHp: hp,
            BonusVp: vp,
            BonusAtk: atk,
            BonusDef: def,
            BonusBr: br,
            BonusAtkPercent: atkPct,
            BonusDefPercent: defPct,
            BonusBrPercent: brPct,
            CardSlashLimit: cardSlash,
            ExtraCardSlashes: extraCardSlashes,
            DigimonExpPercent: digimonExpPct,
            TamerExpFlat: tamerExp,
            HpRecovery: hpRec,
            VpRecovery: vpRec,
            EvpRecovery: evpRec,
            MaintainEvolution: maintainEvolution,
            AutoSummonPartners: autoSummonPartners,
            BonusDamagePercent: bonusDamagePct,
            SingleSkillDamagePercent: singleSkillPct,
            MultiSkillDamagePercent: multiSkillPct,
            MeleeSkillDamagePercent: meleeSkillPct,
            RangedSkillDamagePercent: rangedSkillPct,
            OnHitHealHp: onHitHealHp,
            OnHitHealVp: onHitHealVp);
    }

    private static readonly Regex CardSlashRegex = new(
        @"^Card Slash\s+(\d+)\s+times?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExtraCardSlashRegex = new(
        @"^Extra Card Slash\s+(\d+)\s+times?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SummonMoreRegex = new(
        @"^Summon\s+(\d+)\s+more Digimon$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DigimonExpRegex = new(
        @"^Digimon EXP\s*\+?\s*(\d+)\s*%$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TamerExpRegex = new(
        @"^Tamer EXP\s*\+?\s*(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RecoveryRegex = new(
        @"^(HP|VP|EVP)\s+Recovery Amount\s+(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SkillDamageRegex = new(
        @"^(Single|Multi|Melee|Ranged)\s+Skill Damage\s*\+?\s*(\d+)\s*%$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GeneralDamageRegex = new(
        @"^Damag?e\s*\+?\s*(\d+)\s*%$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OnHitHealRegex = new(
        @"^Recover\s+(\d+)\s+(HP|VP),?\s*when attacking\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    private static Attributes Growth(string[] f) => new(Int(f, 15), Int(f, 16), Int(f, 17), Int(f, 18));

    private static int Int(string[] f, int i) => int.TryParse(f[i], CultureInfo.InvariantCulture, out var v) ? v : 0;
    private static short Short(string[] f, int i) => short.TryParse(f[i], CultureInfo.InvariantCulture, out var v) ? v : (short)0;
    private static byte Byte(string[] f, int i) => i < f.Length && byte.TryParse(f[i], CultureInfo.InvariantCulture, out var v) ? v : (byte)0;
    private static uint UInt(string[] f, int i) => i < f.Length && uint.TryParse(f[i], CultureInfo.InvariantCulture, out var v) ? v : 0;
    private static ushort UShort(string[] f, int i) => i < f.Length && ushort.TryParse(f[i], CultureInfo.InvariantCulture, out var v) ? v : (ushort)0;
}
