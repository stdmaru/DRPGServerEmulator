using DRPG.Client.Data;
using DRPG.Client.Startup;
using DRPG.Client.Text;
using DRPG.Client.Ui;
using DRPG.Engine.Platform;

namespace DRPG.Client.Tests;

/// <summary>The small shared systems of the scenes: text, mouse, fade, tables, preload lists and the dev script.</summary>
public sealed class UiPartsTests
{
    [Theory]
    [InlineData("Level %3d", 12, "Level  12")]
    [InlineData("%d%%", 50, "50%")]
    [InlineData("%s!", "hi", "hi!")]
    public void Printf_formats_the_subset_the_original_uses(string format, object argument, string expected) =>
        Assert.Equal(expected, PrintfFormat.Format(format, argument));

    [Fact]
    public void Printf_never_throws_on_hostile_formats()
    {
        Assert.NotNull(PrintfFormat.Format("%", 1));
        Assert.NotNull(PrintfFormat.Format("%d %d %d", 1));
        Assert.NotNull(PrintfFormat.Format("%99999999d", 1));
        Assert.NotNull(PrintfFormat.Format("%s", 5));
        Assert.NotNull(PrintfFormat.Format("%q%0.f", "x"));
    }

    [Fact]
    public void Text_attach_prefers_the_language_file_and_reports_missing_ids()
    {
        var texts = new TextAttach();
        texts.AddTableRows([["10", "table text"], ["11", "a", "b"], ["x", "no"], ["-1", "no"], ["12"]]);
        Assert.Equal("table text", texts[10]);
        Assert.Equal("a|b", texts[11]);
        Assert.Equal(2, texts.Count);
        texts.AddLanguageFile("\uFEFF10|overridden\r13|new\n\rbad line\r|nothing\r99999999999|x"u8);
        Assert.Equal("overridden", texts[10]);
        Assert.Equal("new", texts[13]);
        Assert.Equal(TextAttach.MissingText, texts[404]);
    }

    [Fact]
    public void Text_attach_survives_binary_garbage()
    {
        var texts = new TextAttach();
        texts.AddLanguageFile(new byte[] { 0xFF, 0xFE, 0x00, (byte)'|', 0x0D, 0x7C, 0x7C });
        Assert.Equal(TextAttach.MissingText, texts[1]);
    }

    [Fact]
    public void Fade_starts_clear_and_ends_after_the_limit()
    {
        var fade = new FadeOut();
        Assert.Equal(0, fade.Amount);
        Assert.False(fade.Done);
        var renderer = new FakeRenderer();
        for (int i = 0; i < 100 && !fade.Done; i++)
            fade.Frame(renderer, 1f / 30);
        Assert.True(fade.Done);
        Assert.Equal(255, fade.Amount);
        Assert.NotEmpty(renderer.Darkens);
    }

    [Fact]
    public void A_second_press_in_time_and_place_is_a_double_click_but_a_third_starts_over()
    {
        var mouse = new MouseState();
        mouse.Handle(new MouseButtonEvent(10, 10, true), 1000);
        Assert.True(mouse.Pressed);
        Assert.False(mouse.DoubleClicked);
        mouse.EndFrame();
        mouse.Handle(new MouseButtonEvent(11, 10, true), 1300);
        Assert.True(mouse.DoubleClicked);
        mouse.EndFrame();
        mouse.Handle(new MouseButtonEvent(11, 10, true), 1400);
        Assert.False(mouse.DoubleClicked);
        mouse.EndFrame();
        mouse.Handle(new MouseButtonEvent(11, 10, true), 1400 + MouseState.DoubleClickMs + 1);
        Assert.False(mouse.DoubleClicked);
        mouse.Handle(new MouseButtonEvent(50, 50, true), 1400 + MouseState.DoubleClickMs + 2);
        Assert.False(mouse.DoubleClicked);
    }

    [Fact]
    public void Leaving_the_window_releases_the_button()
    {
        var mouse = new MouseState();
        mouse.Handle(new MouseButtonEvent(1, 1, true), 0);
        mouse.Handle(new MouseLeaveEvent(), 1);
        Assert.False(mouse.Down);
        Assert.Equal(-1, mouse.X);
    }

    [Theory]
    [InlineData(34, 10)]
    [InlineData(37, 15)]
    [InlineData(21, 21)] // unmapped numbers use themselves
    public void The_species_maps_to_its_sprite_id(int species, int sprite) => Assert.Equal(sprite, SpeciesTables.SpriteId(species));

    [Fact]
    public void The_slot_offset_has_a_default_and_the_listed_exceptions()
    {
        Assert.Equal((-40, -14), SpeciesTables.OffsetOf(21));
        Assert.Equal((-84, -54), SpeciesTables.OffsetOf(26));
        Assert.Equal((-100, -70), SpeciesTables.OffsetOf(678));
        Assert.Equal((135, 110), SpeciesTables.CenterOf(645));
        Assert.Null(SpeciesTables.CenterOf(21));
    }

    [Fact]
    public void Species_tables_accept_any_number()
    {
        foreach (int species in new[] { int.MinValue, -1, 0, 100000, int.MaxValue })
        {
            _ = SpeciesTables.SpriteId(species);
            _ = SpeciesTables.OffsetOf(species);
            _ = SpeciesTables.CenterOf(species);
        }
    }

    [Fact]
    public void Preload_lists_skip_comments_blank_lines_and_headers()
    {
        var list = PreloadList.Parse("# comment\r\n[Section]\r\n\r\nSprite\\a.spr\r\n  Sprite\\b.spr  \n");
        Assert.Equal([@"Sprite\a.spr", @"Sprite\b.spr"], list);
    }

    [Theory]
    [InlineData(PreloadList.LoadingScreenGui)]
    [InlineData(PreloadList.GameInit)]
    [InlineData(PreloadList.Title)]
    public void The_embedded_lists_are_not_empty(string name) => Assert.NotEmpty(PreloadList.Read(name));

    [Fact]
    public void An_unknown_embedded_list_is_an_error() =>
        Assert.Throws<InvalidOperationException>(() => PreloadList.Read("nothing.txt"));

    [Fact]
    public void The_dev_script_plays_events_in_time_order_and_ignores_garbage()
    {
        Assert.Null(DevScript.Parse(null));
        Assert.Null(DevScript.Parse("garbage;x:y;1:teleport"));
        var script = DevScript.Parse("100:move:5:6;200:down;abc:move:1:1;300:up;400:key:esc;500:move:1")!;
        Assert.Empty(script.Due(50));
        var first = script.Due(250).ToList();
        Assert.IsType<MouseMoveEvent>(first[0]);
        Assert.Equal(new MouseButtonEvent(5, 6, true), first[1]);
        Assert.Equal(2, first.Count);
        Assert.Equal(2, script.Due(10_000).Count());
        Assert.Empty(script.Due(20_000));
    }
}
