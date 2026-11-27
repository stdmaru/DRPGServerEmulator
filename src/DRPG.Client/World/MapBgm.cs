namespace DRPG.Client.World;

/// <summary>
/// The background music of a map (✅ v322 <c>0x00ad3460</c>, called by the map load <c>0x00ad3730</c>; the table is in
/// docs/cliente/cena-jogo.md#bgm-do-mapa). Null = no track: the one that was playing stops and nothing starts.
/// </summary>
public static class MapBgm
{
    private readonly record struct Entry(string? File, int First, int Last);

    // ✅ one range per line of the table of the page; a map that is in no range has no music; "vazio" (an empty path) is null too
    private static readonly Entry[] Entries =
    [
        new(null, 0, 0), new(null, 14, 17), new(null, 30, 33),
        new("Sound\\007.ogg", 1, 1), new("Sound\\007.ogg", 5, 5), new("Sound\\007.ogg", 250, 250),
        new("Sound\\001.ogg", 2, 4), new("Sound\\001.ogg", 6, 13), new("Sound\\001.ogg", 24, 29), new("Sound\\001.ogg", 44, 46),
        new("Sound\\001.ogg", 79, 79), new("Sound\\001.ogg", 131, 131),
        new("Sound\\009.ogg", 18, 19), new("Sound\\009.ogg", 38, 39), new("Sound\\009.ogg", 52, 53),
        new("Sound\\020.ogg", 20, 20), new("Sound\\020.ogg", 40, 40),
        new("Sound\\105.ogg", 21, 23), new("Sound\\105.ogg", 41, 43),
        new("sound\\035.ogg", 34, 35), new("sound\\035.ogg", 99, 99), new("sound\\035.ogg", 107, 107), new("sound\\035.ogg", 132, 132), new("sound\\035.ogg", 134, 134),
        new("sound\\036.ogg", 36, 36),
        new("sound\\037.ogg", 37, 37), new("sound\\037.ogg", 51, 51),
        new("Sound\\060.ogg", 60, 65),
        new("Sound\\070.ogg", 70, 70),
        new("Sound\\071.ogg", 71, 77), new("Sound\\071.ogg", 95, 95),
        new("Sound\\080.ogg", 80, 87), new("Sound\\080.ogg", 89, 89), new("Sound\\080.ogg", 98, 98), new("Sound\\080.ogg", 112, 116),
        new("Sound\\Ws_cave.ogg", 96, 96), new("Sound\\Ws_cave.ogg", 100, 101), new("Sound\\Ws_cave.ogg", 108, 108),
        new("Sound\\WS_Ocean.ogg", 97, 97), new("Sound\\WS_Ocean.ogg", 102, 103), new("Sound\\WS_Ocean.ogg", 109, 109),
        new("Sound\\WS_Passage.ogg", 104, 104),
        new("Sound\\WS_BossRoom.ogg", 105, 105),
        new("Sound\\Riddle.ogg", 106, 106),
        new("sound\\XUANWUMON_map.ogg", 117, 117), new("sound\\XUANWUMON_map.ogg", 119, 119),
        new("sound\\SPIRALMOUNTAIN_map.ogg", 125, 128),
        new("sound\\DarkForest.ogg", 129, 129),
        new("sound\\UnderForest.ogg", 130, 130),
        new("sound\\ZhuqiaomonDungeon.ogg", 133, 133), new("sound\\ZhuqiaomonDungeon.ogg", 136, 141),
        new("sound\\MugenMountain.ogg", 135, 135), new("sound\\MugenMountain.ogg", 142, 142),
        new("sound\\HellsField_Floor.ogg", 143, 143), new("sound\\HellsField_Floor.ogg", 155, 156),
        new("sound\\UnderGroundPassage_Hard.ogg", 144, 149),
        new("sound\\Gehenna.ogg", 150, 151),
        new("sound\\HellsField_Passage.ogg", 152, 154),
        new("Sound\\BZRoom.ogg", 200, 204),
    ];

    public static string? FileOf(int map)
    {
        foreach (var entry in Entries)
            if (map >= entry.First && map <= entry.Last)
                return entry.File;
        return null;
    }
}
