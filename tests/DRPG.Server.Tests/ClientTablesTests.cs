using DRPG.Shared.Formats.Pack;
using DRPG.Server.Data.GameData;
using DRPG.Server.Game.Stats;

namespace DRPG.Server.Tests;

public class ClientTablesTests
{
    [Fact]
    public void Species_take_growth_from_dex_by_id_then_by_name()
    {
        var s = TestSupport.Data.Species;
        Assert.Equal(new Attributes(2, 1, 1, 1), s[10].Growth);
        Assert.Equal((byte)2, s[50].Stage);
        Assert.Equal((ushort)31, s[50].Skill2);
        Assert.Equal(new Attributes(1, 2, 3, 4), s[60].Growth);   // dex com outro id, mesmo nome
        Assert.Null(s[61].Growth);                                 // sem linha na dex
        Assert.Equal("Namedmon", s[62].Name);                      // nome vazio no digimon_us
    }

    [Fact]
    public void Base_attributes_grow_per_level_and_rookie_adds_bonus()
    {
        var s = TestSupport.Data.Species;
        Assert.Equal(new Attributes(10, 10, 10, 10), StatFormulas.BaseAttributes(s[10], 1));
        Assert.Equal(new Attributes(28, 19, 19, 19), StatFormulas.BaseAttributes(s[10], 10));
        Assert.Equal(new Attributes(30 + 30, 30 + 20, 30 + 20, 30 + 10), StatFormulas.BaseAttributes(s[50], 11));
        Assert.Equal(new Attributes(10, 10, 10, 10), StatFormulas.BaseAttributes(s[61], 30));
    }

    [Fact]
    public void Items_and_zones_come_from_the_client()
    {
        var data = TestSupport.Data;
        Assert.Equal("Test Seed", data.Items[41].Name);
        Assert.False(data.Items.ContainsKey(0));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 13, 40, 44, 79, 90, 91], data.ZoneIds.Select(i => (int)i));
    }

    [Fact]
    public void Captured_portal_wins_over_map_table_name()
    {
        var p = Assert.Single(TestSupport.Data.PortalsOf(40), p => p.X == 4 && p.Y == 80);
        Assert.Equal(new PortalInfo(40, 4, 80, 44, 193, 43), p);
    }

    [Fact]
    public void Unknown_portal_arrives_next_to_the_return_portal()
    {
        // 90 → 91: portal de volta em (50, 50), centro dos portais de 91 em (70, 30) → 5 casas por eixo
        var p = Assert.Single(TestSupport.Data.PortalsOf(90));
        Assert.Equal(new PortalInfo(90, 10, 10, 91, 55, 45), p);
        Assert.Equal(2, TestSupport.Data.PortalsOf(91).Count);
    }
}

/// <summary>
/// O pacote real do cliente (Resources/Client, v322, fora do git) carrega junto com as tabelas do servidor.
/// Sem a cópia local o teste passa sem checar nada.
/// </summary>
public class RealClientTablesTests
{
    [Fact]
    public void Real_client_pack_loads_with_server_resources()
    {
        var resources = Path.Combine(AppContext.BaseDirectory, "Resources");
        var table = Path.Combine(resources, "Client", "table.hf");
        if (!File.Exists(table)) return;
        var grids = Path.Combine(resources, "Client", ClientExtractor.GridsFile);
        var data = GameDataLoader.Load(resources, PackArchive.Open(table), File.Exists(grids) ? MapGrid.Load(grids) : null);
        Assert.True(data.Species.Count > 700);
        Assert.True(data.Items.Count > 10_000);
        Assert.True(data.Cards.Count > 400);
        Assert.True(data.DigimonTitles.Count > 200);
        Assert.Equal(new Attributes(1, 0, 0, 0), data.DigimonTitles[20100].Modifiers.Attributes); // First Win
        Assert.Equal(new Attributes(15, 15, 15, 15), data.DigimonTitles[20030].Modifiers.Attributes); // Endless Journey
        Assert.Equal(("Golden Drill X", CardTrigger.Attack), (data.Cards[74].Name, data.Cards[74].Trigger));
        Assert.Equal(CardTrigger.Attack, data.Cards[19].Trigger);
        Assert.Equal(CardTrigger.Defense, data.Cards[5].Trigger);
        Assert.Equal((SkillInfo.TypeMelee, (byte)1), (data.Skills[236].Type, data.Skills[236].TargetCount));   // Hop Hip
        Assert.Equal((SkillInfo.TypeRanged, (byte)2), (data.Skills[9].Type, data.Skills[9].TargetCount));      // Double Fire
        Assert.Equal((SkillInfo.TypeRanged, (byte)5), (data.Skills[1078].Type, data.Skills[1078].TargetCount)); // Sun of Sorrows
        Assert.Contains((byte)79, data.ZoneIds);
        Assert.Equal(new PortalInfo(1, 3, 120, 2, 165, 17), data.PortalsOf(1).Single(p => p.X == 3));
        Assert.Equal((byte)1, data.Species[14].Stage);
        Assert.Equal(new Attributes(2, 2, 2, 2), data.Species[14].Growth);
        if (data.Grids.Count > 0)
        {
            Assert.Equal((88, 132), (data.Grids[79].Width, data.Grids[79].Height));
            Assert.True(data.Grids[79].IsWalkable(56, 56));     // posição inicial
            Assert.Equal((176, 263), (data.Grids[2].Width, data.Grids[2].Height));
        }
        Assert.True(data.SpawnsOf(2).Sum(s => s.MaxCount) > 0);
        Assert.Equal(8, data.SpawnsOf(46).Sum(s => s.MaxCount));
        Assert.Equal(3, data.SpawnsOf(79).Sum(s => s.MaxCount));
        var yuramon = data.Species[14];
        Console.WriteLine($"real: {data.Species.Count} espécies ({data.Species.Values.Count(s => s.Growth is null)} sem dex), " +
            $"{data.Items.Count} itens, {data.ZoneIds.Count} zonas, {data.Portals.Values.Sum(p => p.Count)} portais; " +
            $"Yuramon {yuramon.Name} estágio {yuramon.Stage} cresc. {yuramon.Growth}");
    }
}
