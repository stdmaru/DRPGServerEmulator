using DRPGServer.Data.GameData;
using DRPGServer.Game.Items;
using DRPGServer.Game.Stats;

namespace DRPGServer.Tests;

public sealed class ItemUseTests
{
    private static readonly string RealPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Item", "ItemEffects.xml");

    [Fact]
    public void ItemEffects_xml_maps_teleports_and_taming_nets()
    {
        var effects = GameDataLoader.LoadItemUseEffects(RealPath);

        Assert.Equal([new TeleportInMapEffect()], effects[900]);
        Assert.Equal([new TeleportInMapEffect()], effects[907]);
        Assert.Equal([new TamingNetEffect(StatFormulas.InTraining, 100)], effects[10022]);
        Assert.Equal([new TamingNetEffect(StatFormulas.Rookie, 100)], effects[10026]);
        Assert.Equal(ItemUseEntry.AtPosition, effects[900][0].Entry);
        Assert.Equal(ItemUseEntry.OnTarget, effects[10022][0].Entry);
    }

    [Fact]
    public void Loaded_items_carry_their_use_effects()
    {
        Assert.Equal([new TeleportInMapEffect()], TestSupport.Data.FindItem(907)!.UseEffects);
        Assert.Empty(TestSupport.Data.FindItem(41)!.UseEffects);
    }

    [Theory]
    [InlineData("""<Item ID="1" Name="x"><Effect Type="Fly" /></Item>""")]                                        // tipo desconhecido
    [InlineData("""<Item ID="1" Name="x"><Effect Type="TamingNet" Stage="Rookie" /></Item>""")]                     // falta Chance
    [InlineData("""<Item ID="1" Name="x"><Effect Type="TamingNet" Stage="2" Chance="100" /></Item>""")]             // estágio por número
    [InlineData("""<Item ID="1" Name="x"><Effect Type="TamingNet" Stage="Rookie" Chance="101" /></Item>""")]        // chance acima de 100
    [InlineData("""<Item ID="1" Name="x"><Effect Type="TamingNet" Stage="Rookie" Chance="0" /></Item>""")]          // chance zero
    [InlineData("""<Item ID="1" Name="x"><Effect Type="TeleportInMap" MapId="3" /></Item>""")]                     // parâmetro não declarado
    [InlineData("""<Item ID="1" Name="x"><Effect Type="TeleportInMap" /><Effect Type="TamingNet" Stage="Rookie" Chance="1" /></Item>""")] // 0x85 e 0x41 juntos
    [InlineData("""<Item ID="1" Name="x" />""")]                                                                   // sem efeito
    [InlineData("""<Item ID="1" Name="x"><OnUse /></Item>""")]                                                     // elemento estranho
    [InlineData("""<Item ID="1" Name="x"><Effect Type="TeleportInMap" /></Item><Item ID="1" Name="y"><Effect Type="TeleportInMap" /></Item>""")] // duplicado
    public void ItemEffects_xml_rejects_malformed_items(string items)
    {
        var path = Path.Combine(Path.GetTempPath(), $"items-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, $"<ItemEffects>{items}</ItemEffects>");
        try
        {
            Assert.Throws<GameDataException>(() => GameDataLoader.LoadItemUseEffects(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
