using DRPG.Client.Data;
using DRPG.Client.Features.Field;
using DRPG.Client.Startup;
using DRPG.Client.Ui;
using DRPG.Client.World;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;
using Xunit;

namespace DRPG.Client.Tests;

public sealed class EntityPlatesTests : IDisposable
{
    private const int Language = 0;
    private static readonly Serial Wild = new(9, 9), Leader = new(7, 7);

    private readonly SceneFixture _scene = new();

    private EntityPlates Plates(MonsterRegenTable? regen = null) =>
        new(new UiSprites(_scene.Game), _scene.Game.Text, regen ?? MonsterRegenTable.Empty);

    private int Width(string text) => _scene.Game.Text.Measure(_scene.Renderer, text).Width;

    private static FieldDigimonPacket WildPacket(string name = "Yuramon", ushort species = 14, ushort stage = 2, ushort level = 3,
        byte grade = 2) => new(Wild, name, species, stage, level, grade, 1, 0, 6, 6, 6, 6);

    private static Func<CharacterDataPacket, CharacterDataPacket> WithLeader(byte tamerId = 1, ushort tamerClass = 1) => c =>
    {
        var party = c.Party.ToArray();
        party[0] = DigimonStatus.Empty with { Uid = 1, Serial = Leader, SpeciesId = 14, Name = "Agumon", Level = 5, Stage = 2, Hp = 30, MaxHp = 60 };
        return c with { Name = "Marcus", TamerId = tamerId, TamerClass = tamerClass, Party = party };
    };

    [Fact]
    public void Nothing_under_the_mouse_shows_nothing()
    {
        using var field = new FieldFixture(FieldFixture.Open(12, 12), new Cell(2, 2));
        field.State.Hover(new Cell(9, 9));
        Assert.Null(Plates().PlateOf(_scene.Renderer, field.State, field.Character, 0, 0, Language));
        Assert.Null(Plates().BarOf(field.State, field.Character, 0, 0));
    }

    [Fact]
    public void A_wild_shows_its_level_and_name_in_yellow_with_its_grade_above_its_height()
    {
        using var field = new FieldFixture(FieldFixture.Open(12, 12), new Cell(2, 2));
        field.Handlers.Handle(WildPacket());
        field.State.Hover(new Cell(6, 6));

        var plate = Plates().PlateOf(_scene.Renderer, field.State, field.Character, 0, 0, Language)!;

        var wild = new Cell(6, 6);
        Assert.Equal("LV3 Yuramon", plate.Text);
        Assert.Equal((wild.CenterX - Width("LV3 Yuramon") / 2 - 7, wild.CenterY - 0x55), (plate.X, plate.Y));      // stage 2: 85 px
        Assert.Equal((PlateStyle.Wild, 2), (plate.Style, plate.Icon));
        Assert.Null(Plates().BarOf(field.State, field.Character, 0, 0));                                          // not the leader
    }

    [Fact]
    public void A_wild_name_cut_by_the_packet_shows_the_full_name_of_monster_regen()
    {
        var regen = MonsterRegenTable.From(new Dictionary<string, IReadOnlyList<string[]>>
        {
            ["MonsterRegen"] = new List<string[]> { new[] { "10710203", "203", "Dukemon : Crimson Mode", "71", "1", "0" } },
        });
        using var field = new FieldFixture(FieldFixture.Open(12, 12), new Cell(2, 2));
        field.Handlers.Handle(WildPacket(name: "Dukemon : Crimson Mo", species: 203, level: 71));
        field.State.Hover(new Cell(6, 6));

        var plate = Plates(regen).PlateOf(_scene.Renderer, field.State, field.Character, 0, 0, Language)!;

        Assert.Equal("LV71 Dukemon : Crimson Mode", plate.Text);
    }

    [Fact]
    public void Above_language_1_a_digimon_shows_no_plate()
    {
        using var field = new FieldFixture(FieldFixture.Open(12, 12), new Cell(2, 2));
        field.Handlers.Handle(WildPacket());
        field.State.Hover(new Cell(6, 6));
        Assert.Null(Plates().PlateOf(_scene.Renderer, field.State, field.Character, 0, 0, language: 2));
    }

    [Fact]
    public void The_tamer_shows_its_name_in_brackets_with_its_class_13_px_left_of_its_feet()
    {
        using var field = new FieldFixture(FieldFixture.Open(12, 12), new Cell(5, 5), character: WithLeader(tamerId: 1, tamerClass: 2));
        field.State.Hover(new Cell(5, 5));

        var plate = Plates().PlateOf(_scene.Renderer, field.State, field.Character, 0, 0, Language)!;

        var tamer = new Cell(5, 5);
        Assert.Equal("[Marcus]", plate.Text);
        Assert.Equal((tamer.CenterX - 13 - Width("[Marcus]") / 2, tamer.CenterY - 100), (plate.X, plate.Y));
        Assert.Equal((PlateStyle.Tamer, 2), (plate.Style, plate.Icon));
    }

    [Theory]
    [InlineData(8, 0x8c)]
    [InlineData(1, 100)]
    public void Tamer_8_is_taller(byte tamerId, int height)
    {
        using var field = new FieldFixture(FieldFixture.Open(12, 12), new Cell(5, 5), character: WithLeader(tamerId: tamerId, tamerClass: 0));
        field.State.Hover(new Cell(5, 5));

        var plate = Plates().PlateOf(_scene.Renderer, field.State, field.Character, 0, 0, Language)!;

        Assert.Equal(new Cell(5, 5).CenterY - height, plate.Y);
        Assert.Equal(PlateStyle.Plain, plate.Style);                                                              // class 0: no icon
        Assert.Equal(new Cell(5, 5).CenterX - 13 - Width("[Marcus]") / 2 - 7, plate.X);
    }

    [Fact]
    public void The_leader_shows_its_plate_and_its_hp_bar_ten_px_below_its_feet()
    {
        using var field = new FieldFixture(FieldFixture.Open(12, 12), new Cell(5, 5), character: WithLeader());
        var partner = field.State.Party.Partner!.Cell;
        field.State.Hover(partner);
        var plates = Plates();

        var plate = plates.PlateOf(_scene.Renderer, field.State, field.Character, 0, 0, Language)!;
        var bar = plates.BarOf(field.State, field.Character, 0, 0)!;

        Assert.Equal(("LV5 Agumon", PlateStyle.Plain), (plate.Text, plate.Style));
        Assert.Equal((partner.CenterX - 20, partner.CenterY + 10, 30L, 60L), (bar.X, bar.Y, bar.Hp, bar.MaxHp));
        _scene.Renderer.Draws.Clear();
        plates.DrawBar(_scene.Renderer, bar);
        var fill = Assert.Single(_scene.Renderer.Draws, d => d.Y == bar.Y + 1);
        Assert.Equal(EntityPlates.FillWidth(40, 30, 60), fill.PartWidth);
        Assert.Equal(20, EntityPlates.FillWidth(40, 30, 60));                                                     // (int)(39 · 50 / 100 + 1)
    }

    [Theory]
    [InlineData(0x2b, false)]
    [InlineData(0x49, false)]
    [InlineData(0x93, true)]
    [InlineData(0x50, true)]
    public void An_npc_shows_its_name_in_brackets_except_two(int npcId, bool shown)
    {
        var npcs = NpcTable.From(new Dictionary<string, IReadOnlyList<string[]>>
        {
            ["NPC_INFO"] = new List<string[]>
            {
                new[] { "79", npcId.ToString(), "Nyang", "0", "8", "8", "NPC_a", "icon_npc", "0", "1", "0", "0", "0", "1", "0" },
            },
        });
        using var field = new FieldFixture(FieldFixture.Open(12, 12), new Cell(2, 2), npcs: npcs);
        field.State.Hover(new Cell(8, 8));

        var plate = Plates().PlateOf(_scene.Renderer, field.State, field.Character, 0, 0, Language);

        if (!shown)
        {
            Assert.Null(plate);
            return;
        }
        int lift = npcId == 0x93 ? 50 : 0;
        Assert.Equal(("[Nyang]", PlateStyle.Plain), (plate!.Text, plate.Style));
        Assert.Equal((new Cell(8, 8).CenterX - Width("[Nyang]") / 2 - 7, new Cell(8, 8).CenterY - 100 - lift), (plate.X, plate.Y));
    }

    [Fact]
    public void The_plate_is_four_rows_of_7_px_pieces_mixed_at_16_with_the_grade_icon_and_the_text()
    {
        var plates = Plates();
        var plate = new EntityPlate("LV3 Yuramon", 100, 200, 2, PlateStyle.Wild);
        int pieces = (Width(plate.Text) + 14 + 4) / 7;
        _scene.Renderer.Draws.Clear();

        plates.DrawPlate(_scene.Renderer, _scene.Game.SpriteOptions, plate);

        var draws = _scene.Renderer.Draws;
        var box = draws.Where(d => (d.Texture.Width, d.Texture.Height) == (14, 7)).ToList();
        Assert.Equal(4 * (pieces + 2), box.Count);                                                        // y − 7, y, y + 7, y + 14
        Assert.Contains(box, d => (d.X, d.Y, d.PartWidth) == (100, 193, 7));                                      // top-left
        Assert.Contains(box, d => (d.X, d.Y, d.PartWidth) == (100 + (pieces + 1) * 7, 214, 14));                  // bottom-right, 14 wide
        Assert.All(box, d => Assert.Equal(0x10, d.Alpha));
        Assert.Contains(draws, d => (d.Texture.Width, d.Texture.Height, d.X, d.Y) == (13, 11, 103, 199));       // grade at (x + 3, y − 1)
    }
    public void Dispose() => _scene.Dispose();
}

public sealed class MonsterRegenTableTests
{
    private static MonsterRegenTable Table(params string[][] rows) =>
        MonsterRegenTable.From(new Dictionary<string, IReadOnlyList<string[]>> { ["MonsterRegen"] = rows });

    [Fact]
    public void The_first_row_of_the_species_whose_first_20_characters_match_gives_the_name()
    {
        var table = Table(
            new[] { "1", "203", "Other : Long Name Mode", "1", "1", "0" },
            new[] { "2", "203", "Dukemon : Crimson Mode", "1", "1", "0" },
            new[] { "3", "203", "Dukemon : Crimson Modeee", "1", "1", "0" });
        Assert.Equal("Dukemon : Crimson Mode", table.FullName(203, "Dukemon : Crimson Mo"));
        Assert.Equal("Dukemon : Crimson Mo", table.FullName(204, "Dukemon : Crimson Mo"));                    // another species
        Assert.Equal("Agumon", table.FullName(203, "Agumon"));
    }

    [Theory]
    [InlineData("1", "203", "Name", "1", "1")]                                                                     // 5 columns
    [InlineData("1", "x", "Name", "1", "1", "0")]                                                                  // species not a number
    public void A_malformed_row_stops_the_load(params string[] row) =>
        Assert.Throws<InvalidDataException>(() => Table(row));

    [Fact]
    public void A_pack_without_the_table_has_no_names() =>
        Assert.Equal("Agumon", MonsterRegenTable.From(new Dictionary<string, IReadOnlyList<string[]>>()).FullName(1, "Agumon"));
}

public sealed class NameHeightTests
{
    [Theory]
    [InlineData(0x35, 3, 0x41)]
    [InlineData(0x292, 1, 0x41)]
    [InlineData(0xa0, 1, 0x82)]
    [InlineData(14, 1, 0x37)]
    [InlineData(14, 5, 0x82)]
    [InlineData(14, 2, 0x55)]
    [InlineData(0x54, 1, 0x37)]                                                                                    // counts as 0x33: by stage
    [InlineData(14, 0x101, 0x37)]                                                                                  // ✅ the stage is a byte
    public void A_digimon_is_as_tall_as_its_species_or_its_stage(int species, int stage, int height) =>
        Assert.Equal(height, SpeciesTables.NameHeight(species, stage));
}
