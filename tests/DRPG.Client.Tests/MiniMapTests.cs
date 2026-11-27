using DRPG.Client.Features.Field;
using DRPG.Client.Startup;
using DRPG.Client.Ui;
using DRPG.Client.World;

namespace DRPG.Client.Tests;

public sealed class MiniMapTests : IDisposable
{
    private readonly SceneFixture _scene = new();
    private readonly FieldFixture _field;
    private readonly MiniMapWindow _miniMap;
    private readonly List<MapPortalInfo> _portals = [new("West", true, 0, 0), new("Off", false, 29, 19)];

    public MiniMapTests()
    {
        var npcs = NpcTable.From(new Dictionary<string, IReadOnlyList<string[]>>
        {
            ["NPC_INFO"] = new List<string[]>
            {
                new[] { "1", "322", "GM. Mitsuo Yamaki", "0", "15", "10", "NPC_yamaki", "icon_npc", "26", "1", "238", "0", "0", "2", "1" },
                new[] { "1", "323", "Hidden", "0", "3", "3", "NPC_b", "icon_npc", "0", "1", "0", "0", "0", "1", "0" },
            },
        });
        _field = new FieldFixture(FieldFixture.Open(30, 20), new Cell(15, 10), mapId: 1, npcs: npcs);
        var ui = new UiSprites(_scene.Game);
        var tooltips = new Tooltips(ui.Load(Tooltips.PanelSprite), new MessageBox(ui.Load("gui\\dr2_in_000_003.spr"), _scene.Game.Text), _scene.Game.Text);
        _miniMap = new MiniMapWindow(_scene.Game, ui, tooltips, _field.State, npcs, _ => _portals);
        _scene.Windows.Register(_miniMap);
        _scene.Windows.Open(MiniMapWindow.WindowId);
    }

    private List<FakeRenderer.DrawCall> Frame()
    {
        _scene.Renderer.Draws.Clear();
        _scene.Windows.Frame(_scene.Renderer, _scene.Game.Mouse, 0);
        _scene.Game.Mouse.EndFrame();
        return [.. _scene.Renderer.Draws];
    }

    [Theory]
    [InlineData(1, "interface\\w_minimap_1.spr", 0)]
    [InlineData(19, "interface\\w_minimap_1.spr", 18)]
    [InlineData(20, "interface\\w_minimap_2.spr", 0)]
    [InlineData(34, "interface\\w_minimap_2.spr", 14)]
    [InlineData(35, "interface\\w_minimap_2.spr", 9)]
    [InlineData(36, "interface\\w_minimap_2.spr", 8)]
    [InlineData(37, "interface\\w_minimap_2.spr", 5)]
    [InlineData(0x33, "interface\\w_minimap_2.spr", 5)]
    [InlineData(46, "interface\\w_minimap_3.spr", 6)]
    [InlineData(65, "interface\\w_minimap_4.spr", 5)]
    [InlineData(70, "interface\\w_minimap_6.spr", 0)]
    [InlineData(202, "interface\\w_minimap_5.spr", 2)]
    [InlineData(88, "interface\\w_minimap_7.spr", 0)]
    [InlineData(118, "interface\\w_minimap_7.spr", 1)]
    [InlineData(123, "interface\\w_minimap_7.spr", 5)]
    public void Each_map_has_the_picture_of_the_original(int mapId, string sprite, int frame) =>
        Assert.Equal((sprite, frame), MiniMapWindow.Picture(mapId));

    [Theory]
    [InlineData(0)]
    [InlineData(47)]
    [InlineData(119)]
    [InlineData(255)]
    [InlineData(-1)]
    public void A_map_outside_the_table_has_no_picture(int mapId) => Assert.Null(MiniMapWindow.Picture(mapId));

    [Theory]
    [InlineData(0x4f, false)]                                                  // the tutorial
    [InlineData(0x22, false)]
    [InlineData(0xfa, false)]
    [InlineData(1, true)]
    [InlineData(0x58, true)]
    public void Some_maps_never_show_it(int mapId, bool shows) => Assert.Equal(shows, MiniMapWindow.ShowsOn(mapId));

    [Fact]
    public void It_draws_the_title_frame_picture_markers_and_the_player_where_the_original_does()
    {
        var draws = Frame();
        Assert.Contains(draws, d => (d.Texture.Width, d.Texture.Height, d.X, d.Y) == (158, 20, 641, 0));
        Assert.Contains(draws, d => (d.Texture.Width, d.Texture.Height, d.X, d.Y) == (158, 126, 641, 0x14));
        var picture = Assert.Single(draws, d => (d.Texture.Width, d.Texture.Height) == (150, 120));
        Assert.Equal((645, 22, MiniMapWindow.PictureAlpha), (picture.X, picture.Y, picture.Alpha));
        var markers = draws.Where(d => (d.Texture.Width, d.Texture.Height) == (12, 12)).Select(d => (d.X, d.Y)).ToList();
        // the NPC at its row's cell (15, 10) and the portal that is on at (0, 0); the hidden NPC and the portal that is off are not drawn
        Assert.Equal([(645 + 75 - 6, 22 + 60 - 6), (645 - 6, 22 - 6)], markers);
        var point = Assert.Single(draws, d => (d.Texture.Width, d.Texture.Height) == (8, 8));
        Assert.Equal((645 + (int)((float)(15 * 64 + 32) / (30 * 64) * 150) - 4, 22 + (int)((float)(10 * 32 + 16) / (20 * 32) * 120) - 4), (point.X, point.Y));
    }

    [Fact]
    public void Without_the_alpha_option_the_frame_sits_higher_and_the_picture_is_plain()
    {
        _scene.Game.SpriteOptions = _scene.Game.SpriteOptions with { Alpha = false };
        var draws = Frame();
        Assert.Contains(draws, d => (d.Texture.Width, d.Texture.Height, d.X, d.Y) == (158, 126, 641, 0xf));
        Assert.Equal(DRPG.Engine.Rendering.IRenderer.OpaqueAlpha, Assert.Single(draws, d => d.Texture.Width == 150).Alpha);
    }

    [Fact]
    public void The_name_shows_while_the_mouse_is_on_a_marker()
    {
        _scene.Game.Mouse.Move(645 + 75 - 6 + 1, 22 + 60 - 6 + 1);
        var draws = Frame();
        var box = Assert.Single(draws, d => d.Texture.Width == 800);
        Assert.Equal((int?)MiniMapWindowNameHeight, box.PartHeight);
        Assert.Equal(22 + 60 - 6 + 1 - 0x10, box.Y);
    }

    private const int MiniMapWindowNameHeight = 0x11;

    [Fact]
    public void The_button_collapses_it_to_its_title_and_opens_it_again()
    {
        void Click()
        {
            _scene.Game.Mouse.Press(MiniMapWindow.CollapseX + 2, MiniMapWindow.CollapseY + 2);
            Frame();
            _scene.Game.Mouse.Release(MiniMapWindow.CollapseX + 2, MiniMapWindow.CollapseY + 2);
            Frame();
        }

        Click();
        Assert.True(_miniMap.Collapsed);
        var draws = Frame();
        Assert.DoesNotContain(draws, d => d.Texture.Width == 150);
        Assert.Contains(draws, d => (d.Texture.Width, d.Texture.Height) == (10, 11));     // the collapsed button
        Assert.False(_scene.Windows.Covers(700, 60));
        Click();
        Assert.False(_miniMap.Collapsed);
        Assert.True(_scene.Windows.Covers(700, 60));
    }

    [Fact]
    public void On_a_map_without_a_minimap_nothing_is_drawn_and_the_ground_is_free()
    {
        using var tutorial = new FieldFixture(FieldFixture.Open(10, 10), new Cell(2, 2), mapId: 0x4f);
        var ui = new UiSprites(_scene.Game);
        var window = new MiniMapWindow(_scene.Game, ui, new Tooltips([], new MessageBox([], _scene.Game.Text), _scene.Game.Text),
            tutorial.State, NpcTable.Empty, _ => []);
        var windows = new WindowManager(_scene.Sounds);
        windows.Register(window);
        windows.Open(MiniMapWindow.WindowId);
        _scene.Renderer.Draws.Clear();
        windows.Frame(_scene.Renderer, _scene.Game.Mouse, 0);
        Assert.Empty(_scene.Renderer.Draws);
        Assert.False(windows.Covers(700, 10));
        Assert.False(window.ShowsMap);
    }

    public void Dispose()
    {
        _field.Dispose();
        _scene.Dispose();
    }
}
