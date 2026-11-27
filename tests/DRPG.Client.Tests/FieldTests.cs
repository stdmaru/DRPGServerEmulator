using System.Text;
using DRPG.Client.Scenes;
using DRPG.Client.Ui;
using DRPG.Client.World;
using DRPG.Shared.Formats.Map;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Formats.Sprite;

namespace DRPG.Client.Tests;

/// <summary>The map bgm table, the gauges of the HUD, the idle animation and the field map, with synthetic files (nothing of the game).</summary>
public sealed class MapBgmTests
{
    [Theory]
    [InlineData(1, "Sound\\007.ogg")]
    [InlineData(5, "Sound\\007.ogg")]
    [InlineData(250, "Sound\\007.ogg")]
    [InlineData(2, "Sound\\001.ogg")]
    [InlineData(4, "Sound\\001.ogg")]
    [InlineData(79, "Sound\\001.ogg")]
    [InlineData(131, "Sound\\001.ogg")]
    [InlineData(60, "Sound\\060.ogg")]
    [InlineData(65, "Sound\\060.ogg")]
    [InlineData(200, "Sound\\BZRoom.ogg")]
    [InlineData(204, "Sound\\BZRoom.ogg")]
    [InlineData(105, "Sound\\WS_BossRoom.ogg")]
    public void A_map_in_a_range_has_its_track(int map, string file) => Assert.Equal(file, MapBgm.FileOf(map));

    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    [InlineData(17)]
    [InlineData(30)]
    [InlineData(33)]
    [InlineData(50)]
    [InlineData(205)]
    [InlineData(255)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void A_map_with_no_entry_or_an_empty_one_has_no_music(int map) => Assert.Null(MapBgm.FileOf(map));

    [Fact]
    public void No_map_belongs_to_two_different_tracks()
    {
        // the table of the original is checked in order: a map in two ranges with different files would make the order matter
        var tracks = new Dictionary<int, string?>();
        for (int map = 0; map < 256; map++)
        {
            var file = MapBgm.FileOf(map);
            if (file is not null)
                tracks[map] = file;
        }
        Assert.NotEmpty(tracks);
        Assert.All(tracks, pair => Assert.Equal(pair.Value, MapBgm.FileOf(pair.Key)));
    }
}

public sealed class FieldHudTests
{
    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(50, 100, 50)]
    [InlineData(1, 3, 33)]
    [InlineData(2, 3, 66)]
    [InlineData(100, 100, 100)]
    [InlineData(150, 100, 100)]
    [InlineData(-5, 100, 0)]
    [InlineData(10, 0, 0)]
    [InlineData(10, -3, 0)]
    public void The_percent_is_truncated_and_held_in_0_to_100(long current, long maximum, int percent) =>
        Assert.Equal(percent, FieldHud.Percent(current, maximum));

    [Theory]
    [InlineData(100, 0, 1)]
    [InlineData(100, 50, 50)]
    [InlineData(100, 100, 100)]
    [InlineData(100, 150, 100)]
    [InlineData(100, -20, 1)]
    [InlineData(1, 100, 1)]
    public void The_width_of_a_gauge_is_never_below_one_pixel_and_never_above_the_frame(int frameWidth, int percent, int width) =>
        Assert.Equal(width, FieldHud.GaugeWidth(frameWidth, percent));

    [Theory]
    [InlineData(0x33, true)]
    [InlineData(0x35, true)]
    [InlineData(0x36, false)]
    [InlineData(0x6a, true)]
    [InlineData(0x86, false)]
    [InlineData(0x87, true)]
    [InlineData(0x95, true)]
    [InlineData(0x96, false)]
    [InlineData(0x98, true)]
    [InlineData(0x9c, true)]
    [InlineData(0x9d, false)]
    [InlineData(0, false)]
    [InlineData(79, false)]
    public void The_cell_is_hidden_only_on_the_maps_of_the_list(int map, bool hidden) => Assert.Equal(hidden, FieldHud.HidesCell(map));
}

public sealed class FieldHudButtonsTests
{
    private static HudValues Values() => new("Marcus", 1, 5, "Net Ocean", 1, "Plains", 5, 6, null, new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void The_reward_button_is_drawn_at_its_place_outside_the_battle_zone()
    {
        using var fixture = new SceneFixture();
        fixture.Game.ChannelLine = 0;
        var hud = new FieldHud(fixture.Game);

        hud.Draw(fixture.Renderer, Values());

        var reward = Assert.Single(fixture.Renderer.Draws, d => d.Texture.Width == 57 && d.Texture.Height == 20);
        Assert.Equal((451, 462), (reward.X, reward.Y));
    }

    [Theory]
    [InlineData(GameContext.FirstBattleZoneLine)]
    [InlineData(GameContext.LastBattleZoneLine)]
    public void The_reward_button_is_not_drawn_in_the_battle_zone_channels(int line)
    {
        using var fixture = new SceneFixture();
        fixture.Game.ChannelLine = line;
        var hud = new FieldHud(fixture.Game);

        hud.Draw(fixture.Renderer, Values());

        Assert.DoesNotContain(fixture.Renderer.Draws, d => d.Texture.Width == 57 && d.Texture.Height == 20);
    }
}

public sealed class FieldAnimatorTests
{
    /// <summary>An <c>.ani</c> of 12 frames: animation 1 = frames 0..3, 2 = 4..7, 3 = 8..11, every frame lasting <paramref name="duration"/>.</summary>
    private static AniFile Ani(int duration)
    {
        const int frames = 12;
        var data = new byte[0x2a + frames * 12 + 960];
        Encoding.ASCII.GetBytes(AniFile.Signature).CopyTo(data, 0);
        Encoding.ASCII.GetBytes("d_000.spr").CopyTo(data, 0x0e);
        BitConverter.GetBytes(frames).CopyTo(data, 0x26);
        for (int i = 0; i < frames; i++)
            BitConverter.GetBytes(duration).CopyTo(data, 0x2a + i * 12);
        void Set(int animation, int start, int count)
        {
            int at = 0x2a + frames * 12 + animation * AniFile.DirectionCount * 6;
            BitConverter.GetBytes((ushort)start).CopyTo(data, at);
            BitConverter.GetBytes((ushort)(start + count - 1)).CopyTo(data, at + 2);
            BitConverter.GetBytes((ushort)count).CopyTo(data, at + 4);
        }
        Set(1, 0, 4);
        Set(2, 4, 4);
        Set(3, 8, 4);
        return AniFile.Parse(data);
    }

    private static FieldAnimator Animator(int duration, bool digimon, int species, int offset)
    {
        var textures = Enumerable.Range(0, 12).Select(_ => (DRPG.Engine.Rendering.ITexture)new FakeTexture(8, 8)).ToList();
        return new FieldAnimator(new SpriteAnimator(Ani(duration), textures), digimon, species, offset);
    }

    [Fact]
    public void A_frame_stays_for_its_duration_plus_one_step()
    {
        var animator = Animator(duration: 2, digimon: false, species: 0, offset: 0);

        for (int step = 1; step <= 3; step++)
        {
            animator.Step();
            Assert.Equal(0, animator.View.Frame);
        }
        animator.Step();
        Assert.Equal(1, animator.View.Frame);
    }

    [Fact]
    public void The_loop_shows_every_frame_and_wraps_after_the_last_one()
    {
        var animator = Animator(duration: 0, digimon: false, species: 0, offset: 0);

        var shown = new List<int>();
        for (int step = 0; step < 9; step++)
        {
            animator.Step();
            shown.Add(animator.View.Frame);
        }

        Assert.Equal([0, 1, 2, 3, 0, 1, 2, 3, 0], shown);              // unlike the character selection, the last frame appears
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 0)]
    [InlineData(7, 0)]
    [InlineData(1000, 0)]
    public void An_initial_phase_past_the_last_frame_starts_at_the_first(int offset, int firstFrame)
    {
        var animator = Animator(duration: 0, digimon: false, species: 0, offset);

        animator.Step();

        Assert.Equal(firstFrame, animator.View.Frame);
    }

    [Theory]
    [InlineData(0x11b)]
    [InlineData(0x11c)]
    [InlineData(0x15d)]
    [InlineData(0x343)]
    [InlineData(0x360)]
    public void The_two_step_species_ignore_the_duration_of_the_file(int species)
    {
        var animator = Animator(duration: 0, digimon: true, species, offset: 0);

        animator.Step();
        animator.Step();
        animator.Step();
        Assert.Equal(0, animator.View.Frame);                           // 3 steps on the first frame
        animator.Step();
        Assert.Equal(1, animator.View.Frame);
    }

    [Fact]
    public void A_digimon_changes_to_an_idle_animation_at_step_200_and_back_at_320()
    {
        var animator = Animator(duration: 0, digimon: true, species: 3, offset: 0);

        for (int step = 1; step < FieldAnimator.ChangeStep; step++)
            animator.Step();
        Assert.Equal(FieldAnimator.Standing, animator.Animation);
        animator.Step();
        Assert.Equal(FieldAnimator.IdleA, animator.Animation);
        Assert.Equal(4, animator.View.Frame);                           // the first frame of animation 2

        for (int step = FieldAnimator.ChangeStep + 1; step < FieldAnimator.BackStep; step++)
            animator.Step();
        Assert.Equal(FieldAnimator.IdleA, animator.Animation);
        animator.Step();
        Assert.Equal(FieldAnimator.Standing, animator.Animation);
        Assert.Equal(0, animator.IdleCounter);                          // and the cycle starts again
    }

    [Fact]
    public void The_roll_of_zero_picks_the_other_idle_animation()
    {
        var animator = Animator(duration: 0, digimon: true, species: 3, offset: 0);

        for (int step = 1; step < FieldAnimator.ChangeStep; step++)
            animator.Step();
        animator.Step(idleRoll: 0);

        Assert.Equal(FieldAnimator.IdleB, animator.Animation);
        Assert.Equal(8, animator.View.Frame);
    }

    [Theory]
    [InlineData(false, 3)]              // a tamer never changes
    [InlineData(true, 0xfb)]            // nor the species of the exception
    public void Some_characters_never_leave_the_standing_animation(bool digimon, int species)
    {
        var animator = Animator(duration: 0, digimon, species, offset: 0);

        for (int step = 0; step < FieldAnimator.BackStep + 10; step++)
            animator.Step();

        Assert.Equal(FieldAnimator.Standing, animator.Animation);
    }

    [Fact]
    public void Switching_to_an_animation_the_sprite_lacks_changes_nothing()
    {
        var animator = Animator(duration: 0, digimon: true, species: 3, offset: 0);
        animator.Step();
        animator.Step();

        animator.Switch(7);
        animator.Switch(-1);
        animator.Switch(99);

        Assert.Equal(FieldAnimator.Standing, animator.Animation);
        Assert.Equal(2, animator.Offset);
    }
}

public sealed class FieldMapTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("drpg-field-").FullName;
    private readonly FakeRenderer _renderer = new();
    private readonly List<string> _log = [];
    private readonly List<IDisposable> _open = [];

    public void Dispose()
    {
        foreach (var item in _open)
            item.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private static void Name(BinaryWriter w, string name)
    {
        var field = new byte[MapFile.NameBytes];
        Encoding.ASCII.GetBytes(name).CopyTo(field, 0);
        w.Write(field);
    }

    private static MapFile Map(int width, int height, (int Obj, int Tile, uint Frame)[] cells, string[] tiles, string[] objects)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((uint)width);
        w.Write((uint)height);
        w.Write(64u);
        w.Write(32u);
        for (int i = 0; i < width * height; i++)
        {
            var (obj, tile, frame) = i < cells.Length ? cells[i] : (-1, -1, 0u);
            w.Write(0u);
            w.Write(obj);
            w.Write(-1);
            w.Write((short)tile);
            w.Write((short)0);
            w.Write(frame);
            w.Write(1u);
        }
        w.Write((ushort)tiles.Length);
        foreach (var name in tiles) Name(w, name);
        w.Write((ushort)objects.Length);
        foreach (var name in objects) Name(w, name);
        return MapFile.Parse(ms.ToArray());
    }

    private static ushort[] Image(int width, int height, ushort rgb)
    {
        var words = new List<ushort> { (ushort)height };
        for (int y = 0; y < height; y++)
        {
            words.Add(1);
            words.Add(0);
            words.Add((ushort)width);
            for (int x = 0; x < width; x++) words.Add(rgb);
        }
        return [.. words];
    }

    private static byte[] Til(int frames, int width, int height)
    {
        var words = new List<ushort>();
        var offsets = new List<uint>();
        for (int i = 0; i < frames; i++)
        {
            offsets.Add((uint)words.Count);
            words.AddRange(Image(width, height, 0x07E0));
        }
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes(TileSetFile.Signature));
        w.Write(640u);
        w.Write(640u);
        w.Write((uint)frames);
        for (int i = 0; i < frames; i++) w.Write((uint)width);
        for (int i = 0; i < frames; i++) w.Write((uint)height);
        foreach (var o in offsets) w.Write(o);
        w.Write((uint)words.Count);
        foreach (var word in words) w.Write(word);
        return ms.ToArray();
    }

    private static byte[] Obj(int width, int height, int anchorX, int anchorY)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        Name(w, "tree");
        w.Write((ushort)width);
        w.Write((ushort)height);
        w.Write((ushort)anchorX);
        w.Write((ushort)anchorY);
        w.Write(new byte[MapObjectFile.MaskBytes]);
        var image = Image(width, height, 0xF800);
        w.Write((uint)image.Length);
        foreach (var word in image) w.Write(word);
        return ms.ToArray();
    }

    private FieldMap Field(MapFile map, params (string Name, byte[] Content)[] files)
    {
        var path = Path.Combine(_directory, $"pack{_open.Count}.hf");
        PackWriter.Create(path, files.Length > 0 ? files : [("map\\unused.bin", [0])]);
        var pack = PackArchive.Open(path);
        var field = new FieldMap(map, pack, _renderer, _log.Add);
        _open.Add(field);
        return field;
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(400, 300, 0, 0)]
    [InlineData(1000, 700, 600, 400)]
    [InlineData(5000, 5000, 1760, 680)]          // 40 × 64 − 800 and 40 × 32 − 600
    [InlineData(-300, -300, 0, 0)]
    public void The_camera_centres_the_point_and_stays_inside_the_map(int worldX, int worldY, int cameraX, int cameraY)
    {
        var field = Field(Map(40, 40, [], [], []));

        Assert.Equal((cameraX, cameraY), field.Camera(worldX, worldY));
    }

    [Fact]
    public void A_map_smaller_than_the_screen_never_scrolls()
    {
        var field = Field(Map(5, 5, [], [], []));

        Assert.Equal((0, 0), field.Camera(10_000, 10_000));
        Assert.Equal((320, 160), (field.PixelWidth, field.PixelHeight));
    }

    [Fact]
    public void The_queue_is_ordered_by_y_and_keeps_the_order_of_entry_for_equal_y()
    {
        var a = new DrawItem(50, null, 1, 0);
        var b = new DrawItem(10, null, 2, 0);
        var c = new DrawItem(50, null, 3, 0);
        var d = new DrawItem(10, null, 4, 0);
        var queue = new List<DrawItem> { a, b, c, d };

        FieldMap.Sort(queue);

        Assert.Equal([2, 4, 1, 3], queue.Select(i => i.X));
    }

    [Fact]
    public void The_queue_runs_the_custom_draws_and_the_textures_in_order()
    {
        var order = new List<string>();
        var texture = new FakeTexture(4, 4);
        var queue = new List<DrawItem>
        {
            new(1, texture, 5, 6),
            new(2, null, 7, 8, (_, x, y) => order.Add($"custom {x},{y}")),
            new(3, null, 9, 9),
        };

        FieldMap.DrawQueue(_renderer, queue);

        Assert.Equal(["custom 7,8"], order);
        Assert.Equal([(texture, 5, 6)], _renderer.Draws.Select(d => (d.Texture, d.X, d.Y)));
    }

    [Fact]
    public void The_ground_draws_the_frame_of_each_cell_at_its_corner_minus_the_camera()
    {
        var field = Field(Map(3, 2, [(-1, 0, 0), (-1, 0, 1), (-1, -1, 0), (-1, 0, 0)], ["g.til"], []), ("map\\g.til", Til(2, 64, 32)));

        field.DrawGround(_renderer, 0, 0);

        Assert.Equal([(0, 0), (64, 0), (0, 32)], _renderer.Draws.Select(d => (d.X, d.Y)));   // the cell without a tileset and the empty ones draw nothing
        Assert.All(_renderer.Draws, d => Assert.Equal((64, 32), (d.Texture.Width, d.Texture.Height)));
    }

    [Fact]
    public void The_ground_moves_with_the_camera()
    {
        var field = Field(Map(3, 2, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0)], ["g.til"], []), ("map\\g.til", Til(1, 64, 32)));

        field.DrawGround(_renderer, 70, 10);

        Assert.Contains((64 - 70, 0 - 10), _renderer.Draws.Select(d => (d.X, d.Y)));
        Assert.Contains((128 - 70, 32 - 10), _renderer.Draws.Select(d => (d.X, d.Y)));
    }

    [Fact]
    public void A_tileset_the_pack_lacks_is_logged_once_and_drawn_as_nothing()
    {
        var field = Field(Map(3, 2, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)], ["missing.til"], []));

        field.DrawGround(_renderer, 0, 0);
        field.DrawGround(_renderer, 0, 0);

        Assert.Empty(_renderer.Draws);
        Assert.Single(_log, line => line.Contains("LoadTile Error") && line.Contains("missing.til"));
    }

    [Fact]
    public void A_corrupt_tileset_is_logged_and_the_scene_goes_on()
    {
        var field = Field(Map(2, 1, [(-1, 0, 0), (-1, 0, 0)], ["bad.til"], []), ("map\\bad.til", [1, 2, 3, 4]));

        field.DrawGround(_renderer, 0, 0);

        Assert.Empty(_renderer.Draws);
        Assert.Single(_log, line => line.Contains("LoadTile Error"));
    }

    [Fact]
    public void A_cell_pointing_at_a_frame_the_tileset_lacks_draws_nothing()
    {
        var field = Field(Map(2, 1, [(-1, 0, 5), (-1, 0, 0)], ["g.til"], []), ("map\\g.til", Til(1, 64, 32)));

        field.DrawGround(_renderer, 0, 0);

        Assert.Single(_renderer.Draws);
        Assert.Equal(64, _renderer.Draws[0].X);
    }

    [Fact]
    public void An_object_is_queued_by_the_cell_and_the_anchor_inside_it()
    {
        // anchor (74, 40): 74 mod 64 = 10 and 40 mod 32 = 8; the image corner is the cell's minus the whole cells of the anchor (64, 32)
        var cells = new (int, int, uint)[3 * 4];
        Array.Fill(cells, (-1, -1, 0u));
        cells[2 * 3 + 1] = (0, -1, 0u);                                 // cell (1, 2)
        var field = Field(Map(3, 4, cells, [], ["tree.obj"]), ("map\\tree.obj", Obj(100, 90, 74, 40)));
        var queue = new List<DrawItem>();

        field.QueueObjects(queue, 0, 0);

        var item = Assert.Single(queue);
        Assert.Equal((0, 32, 72), (item.X, item.DrawY, item.Y));
        Assert.Equal(100, item.Texture!.Width);
    }

    [Fact]
    public void An_object_wholly_outside_the_window_is_not_queued()
    {
        var cells = new (int, int, uint)[3 * 3];
        Array.Fill(cells, (-1, -1, 0u));
        cells[0] = (0, -1, 0u);
        var field = Field(Map(3, 3, cells, [], ["tree.obj"]), ("map\\tree.obj", Obj(10, 10, 600, 600)));   // drawn far above and left of the cell
        var queue = new List<DrawItem>();

        field.QueueObjects(queue, 0, 0);

        Assert.Empty(queue);
    }

    [Fact]
    public void An_object_the_pack_lacks_or_with_a_corrupt_file_is_logged_and_skipped()
    {
        var cells = new (int, int, uint)[2];
        cells[0] = (0, -1, 0u);
        cells[1] = (1, -1, 0u);
        var field = Field(Map(2, 1, cells, [], ["missing.obj", "bad.obj"]), ("map\\bad.obj", [9, 9, 9]));
        var queue = new List<DrawItem>();

        field.QueueObjects(queue, 0, 0);
        field.QueueObjects(queue, 0, 0);

        Assert.Empty(queue);
        Assert.Equal(2, _log.Count(line => line.Contains("LoadObject Error")));      // once each, not once per frame
    }
}
