using DRPGServer.Data.GameData;

namespace DRPGServer.Game.Entities;

/// <summary>
/// Regras e mapeamento oficial dos 29 slots de equipamento do Tamer (✅ engenharia reversa em
/// <c>DigimonRPG.bin @ 0x00AF030C / 0x00B0E740..0x00B0FEFD / 0x00B5BB50 / 0x00B5C170</c> e capturas v322
/// <c>conn=234/238/428</c> para Crests e Digimentals).
/// </summary>
public static class EquipmentSlots
{
    /// <summary>Capacidade da bolsa <see cref="BagKind.Equipment"/> (indexada diretamente pelo código <see cref="ItemPlace"/> 4..86).</summary>
    public const int BagCapacity = (int)ItemPlace.Watch + 1;

    /// <summary>✅ Todos os 29 slots de equipamento reconhecidos pelo cliente v322.</summary>
    public static readonly ItemPlace[] All =
    [
        ItemPlace.Crest1,
        ItemPlace.Crest2,
        ItemPlace.Crest3,
        ItemPlace.Digimental1,
        ItemPlace.Digimental2,
        ItemPlace.Digimental3,
        ItemPlace.Aura,
        ItemPlace.Digivice,
        ItemPlace.Socks,
        ItemPlace.Shoes,
        ItemPlace.Pants,
        ItemPlace.Gloves,
        ItemPlace.Shirt,
        ItemPlace.Jacket,
        ItemPlace.Hat,
        ItemPlace.CostumeSet,
        ItemPlace.Earring,
        ItemPlace.Necklace,
        ItemPlace.Ring,
        ItemPlace.DarkCard,
        ItemPlace.Spirit1,
        ItemPlace.Spirit2,
        ItemPlace.Spirit3,
        ItemPlace.Wings,
        ItemPlace.FloatingEffect,
        ItemPlace.Emblem,
        ItemPlace.Font,
        ItemPlace.Gem,
        ItemPlace.Watch,
    ];

    /// <summary>✅ Os 6 slots de Crest (4..6) e Digimental (7..9) gravados em <c>0x02</c> offsets 100..747.</summary>
    public static readonly ItemPlace[] PrimarySlots =
    [
        ItemPlace.Crest1,
        ItemPlace.Crest2,
        ItemPlace.Crest3,
        ItemPlace.Digimental1,
        ItemPlace.Digimental2,
        ItemPlace.Digimental3,
    ];

    /// <summary>
    /// ✅ Slots gravados na cauda do <c>S2C 0x02</c> (após X/Y no offset 4724), pareados com o offset relativo
    /// ao início da cauda (<c>offset_0x02 - 4724</c>; confirmado em <c>DigimonRPG.bin @ 0x00B5BF21..0x00B5C150</c>).
    /// </summary>
    public static readonly (ItemPlace Place, int TailOffset)[] TailSlots =
    [
        (ItemPlace.Aura,           0x1274 - 4724), // 0
        (ItemPlace.Digivice,       0x12E0 - 4724), // 108
        (ItemPlace.Socks,          0x1354 - 4724), // 224
        (ItemPlace.Shoes,          0x13C0 - 4724), // 332
        (ItemPlace.Pants,          0x142C - 4724), // 440
        (ItemPlace.Gloves,         0x1498 - 4724), // 548
        (ItemPlace.Shirt,          0x1504 - 4724), // 656
        (ItemPlace.Jacket,         0x1570 - 4724), // 764
        (ItemPlace.Hat,            0x15DC - 4724), // 872
        (ItemPlace.CostumeSet,     0x1648 - 4724), // 980
        (ItemPlace.Earring,        0x16B4 - 4724), // 1088
        (ItemPlace.Necklace,       0x1720 - 4724), // 1196
        (ItemPlace.Ring,           0x178C - 4724), // 1304
        (ItemPlace.DarkCard,       0x17F8 - 4724), // 1412
        (ItemPlace.Spirit1,        0x1864 - 4724), // 1520
        (ItemPlace.Spirit2,        0x18D0 - 4724), // 1628
        (ItemPlace.Spirit3,        0x193C - 4724), // 1736
        (ItemPlace.Wings,          0x1AD4 - 4724), // 2144
        (ItemPlace.FloatingEffect, 0x1B40 - 4724), // 2252
        (ItemPlace.Emblem,         0x1BAC - 4724), // 2360
        (ItemPlace.Font,           0x1C18 - 4724), // 2468
        (ItemPlace.Gem,            0x1C84 - 4724), // 2576
        (ItemPlace.Watch,          0x1CF0 - 4724), // 2684
    ];

    /// <summary>
    /// ✅ Slots copiados no <c>S2C 0xB1</c> (1252 B, confirmado em <c>DigimonRPG.bin @ 0x00B5C397..0x00B5C569</c>
    /// e <c>captures.db conn=234/428</c>).
    /// </summary>
    public static readonly (ItemPlace Place, int PacketOffset)[] TamerRefreshSlots =
    [
        (ItemPlace.Crest1,      0x018), // 24
        (ItemPlace.Crest2,      0x084), // 132
        (ItemPlace.Crest3,      0x0F0), // 240
        (ItemPlace.Digimental1, 0x15C), // 348
        (ItemPlace.Digimental2, 0x1C8), // 456
        (ItemPlace.Digimental3, 0x234), // 564
        (ItemPlace.Digivice,    0x2A0), // 672
        (ItemPlace.Spirit1,     0x30C), // 780
        (ItemPlace.Spirit2,     0x378), // 888
        (ItemPlace.Spirit3,     0x3E4), // 996
        (ItemPlace.DarkCard,    0x478), // 1144
    ];

    public static bool IsEquipmentPlace(ItemPlace place) => place switch
    {
        ItemPlace.Crest1 or ItemPlace.Crest2 or ItemPlace.Crest3 => true,
        ItemPlace.Digimental1 or ItemPlace.Digimental2 or ItemPlace.Digimental3 => true,
        ItemPlace.Aura or ItemPlace.Digivice => true,
        ItemPlace.Socks or ItemPlace.Shoes or ItemPlace.Pants or ItemPlace.Gloves => true,
        ItemPlace.Shirt or ItemPlace.Jacket or ItemPlace.Hat or ItemPlace.CostumeSet => true,
        ItemPlace.Earring or ItemPlace.Necklace or ItemPlace.Ring or ItemPlace.DarkCard => true,
        ItemPlace.Spirit1 or ItemPlace.Spirit2 or ItemPlace.Spirit3 => true,
        ItemPlace.Wings or ItemPlace.FloatingEffect or ItemPlace.Emblem => true,
        ItemPlace.Font or ItemPlace.Gem or ItemPlace.Watch => true,
        _ => false,
    };

    /// <summary>
    /// ✅ Valida se o <see cref="ItemInfo.Type"/> do item é aceito no slot de equipamento alvo
    /// (confirmado em <c>DigimonRPG.bin @ 0x00B0E740..0x00B0FEFD</c>).
    /// </summary>
    public static bool AcceptsType(ItemPlace place, byte itemType) => place switch
    {
        ItemPlace.Crest1 or ItemPlace.Crest2 or ItemPlace.Crest3 => itemType == 1,
        ItemPlace.Digimental1 or ItemPlace.Digimental2 or ItemPlace.Digimental3 => itemType == 2,
        ItemPlace.Aura => itemType is 8 or 27,
        ItemPlace.Digivice => itemType == 9,
        ItemPlace.Socks => itemType == 15,
        ItemPlace.Shoes => itemType == 16,
        ItemPlace.Pants => itemType == 17,
        ItemPlace.Gloves => itemType == 18,
        ItemPlace.Shirt => itemType == 19,
        ItemPlace.Jacket => itemType == 20,
        ItemPlace.Hat => itemType == 21,
        ItemPlace.CostumeSet => itemType == 28,
        ItemPlace.Earring => itemType == 23,
        ItemPlace.Necklace => itemType == 24,
        ItemPlace.Ring => itemType == 25,
        ItemPlace.DarkCard => itemType == 37,
        ItemPlace.Spirit1 or ItemPlace.Spirit2 or ItemPlace.Spirit3 => itemType == 38,
        ItemPlace.Wings => itemType == 40,
        ItemPlace.FloatingEffect => itemType == 41,
        ItemPlace.Emblem => itemType == 42,
        ItemPlace.Font => itemType == 43,
        ItemPlace.Gem => itemType == 44,
        ItemPlace.Watch => itemType == 45,
        _ => false,
    };

    /// <summary>Validação server-side completa: slot válido, tipo compatível, não expirado, quantidade 1 e nível mínimo do Tamer.</summary>
    public static bool CanEquip(ItemPlace place, InventoryItem item, ushort tamerLevel, long unixNow)
    {
        if (!IsEquipmentPlace(place)) return false;
        if (item.Amount != 1 || item.Info is not { } info) return false;
        if (item.IsExpired(unixNow)) return false;
        if (!AcceptsType(place, info.Type)) return false;
        if (info.Level > 1 && tamerLevel < info.Level) return false;
        return true;
    }
}
