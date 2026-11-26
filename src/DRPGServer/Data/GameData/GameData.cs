using System.Collections.Frozen;
using DRPGServer.Game.Battle.Cards;
using DRPGServer.Game.Economy;
using DRPGServer.Game.Items;
using DRPGServer.Game.Stats;
using DRPGServer.Game.World;

namespace DRPGServer.Data.GameData;

/// <summary>
/// Espécie de digimon, lida das tabelas do cliente (<c>digimon_us</c> e <c>dex_us</c>, ver docs/cliente/arquivos-de-dados.md).
/// <paramref name="Growth"/>: atributos ganhos por nível (✅ in-training e rookie nas capturas); null se a dex não tem a espécie.
/// </summary>
public sealed record DigimonSpecies(
    ushort Id, string Name, byte Stage, byte Attribute, Game.Stats.Attributes? Growth, ushort Skill1, ushort Skill2, byte Element = 0);

/// <summary>Item de <c>item_info_us</c> com as colunas que o registro de 108 bytes repete (ver <see cref="ClientTables.Items"/>).</summary>
public sealed record ItemInfo(uint Id, string Name)
{
    public byte Type { get; init; }
    public byte SubType { get; init; }
    /// <summary>✅ Preço em Coins (coluna 15; <c>32000</c> = não vende por Coins). Vai em <c>ItemRecord +90</c>.</summary>
    public ushort CoinPrice { get; init; }
    /// <summary>✅ Preço em Ladder Points (coluna 16; <c>1000000</c> = não vende por Ladder). Vai em <c>ItemRecord +92</c>.</summary>
    public uint LadderPrice { get; init; }
    /// <summary>✅ Preço em Bits (coluna 17).</summary>
    public uint BuyPrice { get; init; }
    /// <summary>✅ Venda ao NPC em Bits (coluna 18).</summary>
    public uint SellPrice { get; init; }
    /// <summary>✅ Nível mínimo do Tamer (coluna 20).</summary>
    public uint Level { get; init; } = 1;
    /// <summary>❓ Coluna 23 (0 ou 1), repetida em <c>ItemRecord +96</c>.</summary>
    public byte UnknownCol23 { get; init; }
    /// <summary>❓ Coluna 24 (0 ou 1), repetida em <c>ItemRecord +97</c>.</summary>
    public byte UnknownCol24 { get; init; }
    public uint MaxStack { get; init; } = 1;
    /// <summary>✅ Duração padrão em segundos (col. 26 &gt; 999 em <c>item_info_us.dat</c>; 0 = permanente).</summary>
    public uint DurationSeconds { get; init; }
    public bool IsTimed => DurationSeconds > 0;
    /// <summary>❓ Coluna 32 (0 ou 1), repetida em <c>ItemRecord +99</c>.</summary>
    public byte UnknownCol32 { get; init; }
    public string Description { get; init; } = "";
    /// <summary>Bônus do item equipado (<c>Resources/Item/EquipmentEffects.xml</c>).</summary>
    public StatModifiers Modifiers { get; init; } = StatModifiers.None;
    /// <summary>Efeitos ao usar o item (<c>Resources/Item/ItemEffects.xml</c>). Vazio = não tem uso.</summary>
    public IReadOnlyList<IItemUseEffect> UseEffects { get; init; } = [];
}

/// <summary>Momento de ativação de um card em batalha (coluna 23 de <c>card_info_us</c>).</summary>
public enum CardTrigger : byte
{
    Attack = 1,
    Defense = 2,
}

/// <summary>Card de <c>card_info_us</c> (469 entradas, 31 colunas; ver <see cref="ClientTables.Cards"/>).</summary>
public sealed record CardInfo(uint Id, string Name)
{
    public byte Type { get; init; }
    public byte SubType { get; init; }
    public uint BuyPrice { get; init; }
    public uint SellPrice { get; init; }
    /// <summary>❓ Coluna 19, repetida em <c>CardRecord +10</c>.</summary>
    public byte UnknownCol19 { get; init; }
    public uint MaxStack { get; init; } = 999;
    public CardTrigger Trigger { get; init; } = CardTrigger.Attack;
    public byte Restriction { get; init; }
    /// <summary>❓ Coluna 26, em <c>CardRecord +66</c>. Mesmo sentinela do <see cref="ItemInfo.CoinPrice"/> (<c>32000</c>); card ainda não é vendido por Coins.</summary>
    public ushort CoinPrice { get; init; } = 32000;
    /// <summary>❓ Coluna 27, em <c>CardRecord +72</c> só quando <see cref="UnknownCol30"/> ≠ 0.</summary>
    public byte UnknownCol27 { get; init; } = 1;
    /// <summary>❓ Coluna 28, em <c>CardRecord +68</c>. Mesmo sentinela do <see cref="ItemInfo.LadderPrice"/> (<c>1000000</c>).</summary>
    public uint LadderPrice { get; init; } = 1_000_000;
    /// <summary>❓ Coluna 29, em <c>CardRecord +73</c> só quando <see cref="UnknownCol30"/> ≠ 0.</summary>
    public byte UnknownCol29 { get; init; } = 1;
    /// <summary>❓ Coluna 30, em <c>CardRecord +75</c>; liga as colunas 27 e 29 no registro.</summary>
    public byte UnknownCol30 { get; init; }
    public string Description { get; init; } = "";
    /// <summary>Efeitos ao ativar em batalha (<c>Resources/Card/CardEffects.xml</c>). Vazio = ativa e consome sem efeito.</summary>
    public IReadOnlyList<ICardEffect> Effects { get; init; } = [];

    public bool Has<T>() where T : ICardEffect => Effects.Any(e => e is T);
}

/// <summary>Título de Digimon (<c>Resources/Digimon/DigimonTitles.xml</c>, gerado de <c>digimon_title_us.dat</c>).</summary>
public sealed record DigimonTitleInfo(uint Id, string Name)
{
    /// <summary>Bônus do título equipado.</summary>
    public StatModifiers Modifiers { get; init; } = StatModifiers.None;
    /// <summary>Condições de desbloqueio automático (todas precisam valer). Vazio = só por evento, item ou GM.</summary>
    public IReadOnlyList<ITitleUnlockRule> Unlocks { get; init; } = [];
}

/// <summary>
/// Skill de <c>digimon_skill_us</c>. ✅ Custo em VP = 20 + nível exigido.
/// <paramref name="Type"/>: ✅ 1 = corpo-a-corpo, 2 = à distância (offset 36 do 0x67).
/// <paramref name="TargetCount"/>: ✅ coluna 5 da tabela (1 = alvo único, 2 = 2 alvos, 5 = área / todos).
/// </summary>
public sealed record SkillInfo(ushort Id, string Name, ushort RequiredLevel, byte Type, byte TargetCount = 1)
{
    public const int MaxLevel = 10;
    public const byte TypeMelee = 1;
    public const byte TypeRanged = 2;

    public int VpCost => 20 + RequiredLevel;
}

public sealed record DropEntry(uint ItemId, byte Rate);

/// <summary>
/// Taming Net: captura no mapa um selvagem do estágio <paramref name="Stage"/> (1 in-training … 5 mega), com
/// <paramref name="Chance"/> % (✅ as duas das amostras capturaram de primeira; são itens de cash).
/// </summary>

public sealed record PortalInfo(byte MapId, short X, short Y, byte DestMapId, short DestX, short DestY)
{
    public const int Radius = 2;

    public bool Contains(int x, int y)
    {
        int dx = x - X, dy = y - Y;
        return dx * dx + dy * dy <= Radius * Radius;
    }
}

/// <summary>
/// Um digimon possível num grupo selvagem. O primeiro da lista sempre aparece. Os status vêm do nível
/// (<see cref="Game.Stats.WildFormulas"/>); <see cref="Stats"/> substitui (XML: HP, VP, ATK, DEF, BR), como o
/// Yuramon enfraquecido do tutorial. <paramref name="BitReward"/> é o máximo: ✅ o oficial sorteia de ¾ dele até ele.
/// </summary>
public sealed record SpawnOption(
    ushort SpeciesId, ushort Level, long ExpReward, double BitReward, ushort AppearanceRate, uint DropGroup)
{
    public Game.Stats.CombatStats? Stats { get; init; }
}

/// <summary>
/// Área de spawn. Cada grupo tem uma casa: um dos <paramref name="Points"/> (✅ fixos no oficial) ou, sem pontos,
/// uma célula andável sorteada no retângulo. O grupo anda só em volta da casa (<c>WildDigimon.RoamRadius</c>).
/// <paramref name="Exclusive"/>: ✅ grupo que entra em batalha (11) e morre (8 → 12), como os Otamamon da sessão 56.
/// Sem ele o grupo é compartilhado: continua andando no mapa e a batalha usa cópias (docs/mecanicas/batalha.md).
/// <paramref name="Grade"/>: ✅ grau do grupo (2 = Beginner, 3 = Veteran — byte 44 do 0x11 e byte 300 do 0x63).
/// </summary>
public sealed record SpawnArea(
    byte MapId, short XMin, short XMax, short YMin, short YMax, int MaxCount, TimeSpan RespawnTime,
    IReadOnlyList<SpawnOption> Options, IReadOnlyList<(short X, short Y)> Points, bool Exclusive = false,
    byte Grade = Game.Stats.WildFormulas.GradeBeginner);

/// <summary>Tipo de loja genérica de NPC (<c>0x0050</c> = Card, <c>0x0052</c> = Item).</summary>
public enum NpcShopKind : byte
{
    Card = 1,
    Item = 2,
}

/// <summary>Destino dos itens comprados em uma loja genérica de NPC (<c>Inventory</c> ou <c>ShopDepository</c>).</summary>
public enum NpcShopDelivery : byte
{
    Inventory = 0,
    ShopDepository = 1,
}

/// <summary>Um item ou card vendido por uma loja de NPC (<c>Resources/Item/NpcShop.xml</c>).</summary>
public sealed record NpcShopEntry(uint ItemId, uint? PriceOverride = null);

/// <summary>Uma loja genérica de NPC (até 40 itens ou cards).</summary>
public sealed record NpcShop(
    uint ShopId,
    NpcShopKind Kind,
    string Name,
    IReadOnlyList<NpcShopEntry> Entries,
    CurrencyKind Currency = CurrencyKind.Bits,
    NpcShopDelivery DeliverTo = NpcShopDelivery.Inventory)
{
    public const int MaxSlots = 40;

    public NpcShopEntry? FindEntry(uint itemId)
    {
        foreach (var e in Entries)
            if (e.ItemId == itemId)
                return e;
        return null;
    }
}

/// <summary>Tipo de conteúdo de um produto do Cash Shop (<c>ingameshop_item_data_us.dat</c> col. 1).</summary>
public enum CashShopItemKind : byte
{
    Card = 1,
    Item = 2,
}

/// <summary>Um item ou card contido em um produto do Cash Shop (<c>ingameshop_item_data_us.dat</c>).</summary>
public sealed record CashShopEntry(CashShopItemKind Kind, uint ItemId, uint Amount);

/// <summary>Um produto vendável do Cash Shop (<c>ingameshop_itempage_us.dat</c> + <c>ingameshop_item_data_us.dat</c>).</summary>
public sealed record CashShopProduct(
    uint ProductId, string Name, uint ItemDataId, uint Price, byte Category, IReadOnlyList<CashShopEntry> Entries);

/// <summary>Tabelas estáticas do jogo, carregadas uma vez na partida. Imutáveis.</summary>
/// <summary>✅ Janela que o NPC abre (<c>npc_info_us</c> coluna 10). Só as que o servidor confere estão aqui.</summary>
public enum NpcWindow : ushort
{
    CardShop = 12,
    ItemShop = 13,
    /// <summary>Digimon Center: recuperação, Digimon Depository, Card/Item Depository (<c>npc_function_us</c>).</summary>
    DigimonCenter = 14,
}

/// <summary>
/// ✅ Um NPC do mapa (<c>npc_info_us</c>: 0 mapa, 1 id, 2 nome, 4/5 posição, 10 janela, 12 loja). A posição está nas
/// mesmas casas do movimento do jogador.
/// </summary>
public sealed record NpcInfo(byte MapId, ushort NpcId, string Name, short X, short Y, NpcWindow Window, uint ShopId);

/// <summary>NPCs por mapa, por id e por loja (o dono da loja é o NPC cuja coluna 12 tem o id dela).</summary>
public sealed class NpcDirectory
{
    public static readonly NpcDirectory Empty = new([]);

    private readonly FrozenDictionary<(byte, ushort), NpcInfo> _byId;
    private readonly FrozenDictionary<byte, NpcInfo[]> _byMap;
    private readonly FrozenDictionary<uint, NpcInfo[]> _byShop;

    public NpcDirectory(IEnumerable<NpcInfo> npcs)
    {
        var list = npcs.ToList();
        _byId = list.DistinctBy(n => (n.MapId, n.NpcId)).ToFrozenDictionary(n => (n.MapId, n.NpcId));
        _byMap = list.GroupBy(n => n.MapId).ToFrozenDictionary(g => g.Key, g => g.ToArray());
        _byShop = list.Where(n => n.ShopId != 0).GroupBy(n => n.ShopId).ToFrozenDictionary(g => g.Key, g => g.ToArray());
    }

    public int Count => _byId.Count;
    public NpcInfo? Find(byte mapId, ushort npcId) => _byId.GetValueOrDefault((mapId, npcId));
    public IReadOnlyList<NpcInfo> On(byte mapId) => _byMap.GetValueOrDefault(mapId) ?? [];
    public IReadOnlyList<NpcInfo> OwnersOf(uint shopId) => _byShop.GetValueOrDefault(shopId) ?? [];
}

public sealed class GameData
{
    public required FrozenDictionary<ushort, DigimonSpecies> Species { get; init; }
    public required FrozenDictionary<uint, ItemInfo> Items { get; init; }
    public FrozenDictionary<uint, CardInfo> Cards { get; init; } = FrozenDictionary<uint, CardInfo>.Empty;
    public FrozenDictionary<uint, NpcShop> NpcShops { get; init; } = FrozenDictionary<uint, NpcShop>.Empty;
    public NpcDirectory Npcs { get; init; } = NpcDirectory.Empty;
    public FrozenDictionary<uint, CashShopProduct> CashShopProducts { get; init; } = FrozenDictionary<uint, CashShopProduct>.Empty;
    public FrozenDictionary<uint, DigimonTitleInfo> DigimonTitles { get; init; } = FrozenDictionary<uint, DigimonTitleInfo>.Empty;
    public FrozenDictionary<ushort, SkillInfo> Skills { get; init; } = FrozenDictionary<ushort, SkillInfo>.Empty;
    /// <summary>Espécie → próxima forma da linha evolutiva (<c>tree_us</c>).</summary>
    public FrozenDictionary<ushort, ushort> Evolutions { get; init; } = FrozenDictionary<ushort, ushort>.Empty;
    public required FrozenDictionary<byte, ClientMap> Maps { get; init; }
    /// <summary>Grade de cada mapa (vazia se o grids.bin não foi extraído: aí tudo é andável).</summary>
    public required FrozenDictionary<byte, MapGrid> Grids { get; init; }
    public required FrozenDictionary<uint, IReadOnlyList<DropEntry>> DropGroups { get; init; }
    public required FrozenDictionary<byte, IReadOnlyList<PortalInfo>> Portals { get; init; }
    public required FrozenDictionary<byte, IReadOnlyList<SpawnArea>> Spawns { get; init; }
    public required IReadOnlyList<byte> ZoneIds { get; init; }

    public DigimonSpecies? FindSpecies(ushort id) => Species.GetValueOrDefault(id);
    public SkillInfo? FindSkill(ushort id) => id == 0 ? null : Skills.GetValueOrDefault(id);
    public ItemInfo? FindItem(uint id) => Items.GetValueOrDefault(id);
    public CardInfo? FindCard(uint id) => Cards.GetValueOrDefault(id);
    public NpcShop? FindNpcShop(uint shopId, NpcShopKind kind) =>
        NpcShops.TryGetValue(shopId, out var shop) && shop.Kind == kind ? shop : null;
    public CashShopProduct? FindCashShopProduct(uint productId) => CashShopProducts.GetValueOrDefault(productId);
    public DigimonTitleInfo? FindDigimonTitle(uint id) => id == 0 ? null : DigimonTitles.GetValueOrDefault(id);

    private IReadOnlyList<DigimonTitleInfo>? _autoUnlockTitles;

    /// <summary>Títulos com <see cref="DigimonTitleInfo.Unlocks"/>, por id.</summary>
    public IReadOnlyList<DigimonTitleInfo> AutoUnlockTitles =>
        _autoUnlockTitles ??= DigimonTitles.Values.Where(t => t.Unlocks.Count > 0).OrderBy(t => t.Id).ToArray();

    /// <summary>Próxima forma da linha (null no fim da linha ou se a espécie não está na árvore).</summary>
    public DigimonSpecies? NextForm(DigimonSpecies species) =>
        Evolutions.TryGetValue(species.Id, out var next) ? FindSpecies(next) : null;

    /// <summary>EXP para passar do nível dado (fórmula do oficial, ver <see cref="Game.Stats.ExpFormulas"/>).</summary>
    public long DigimonExpToNext(int level) => Game.Stats.ExpFormulas.DigimonExpToNext(level);

    public MapGrid? GridOf(byte mapId) => Grids.GetValueOrDefault(mapId);
    public IReadOnlyList<PortalInfo> PortalsOf(byte mapId) => Portals.GetValueOrDefault(mapId) ?? [];
    public IReadOnlyList<SpawnArea> SpawnsOf(byte mapId) => Spawns.GetValueOrDefault(mapId) ?? [];
    public IReadOnlyList<DropEntry> DropGroup(uint id) => DropGroups.GetValueOrDefault(id) ?? [];
}
