using DRPG.Launcher.Scenes;

namespace DRPG.Launcher.Tests;

public sealed class TextFieldTests
{
    private static TextField Field(int max = 8, bool allowSpace = false) => new(0, 0, 100, 18, max, masked: false, allowSpace);

    [Fact]
    public void Typing_appends_and_backspace_removes()
    {
        var f = Field();
        Assert.True(f.Type("abc"));
        f.Backspace();
        Assert.Equal("ab", f.Text);
        f.Backspace(); f.Backspace(); f.Backspace();
        Assert.Equal("", f.Text);
    }

    [Theory]
    [InlineData("a b", "ab")]
    [InlineData("éça", "a")]
    [InlineData("\r\n\t\0x", "x")]
    [InlineData("한글z", "z")]
    [InlineData("\u007f", "")]
    public void Only_printable_ascii_is_accepted(string input, string expected)
    {
        var f = Field();
        Assert.False(f.Type(input));
        Assert.Equal(expected, f.Text);
    }

    [Fact]
    public void Space_only_where_allowed()
    {
        var f = Field(allowSpace: true);
        Assert.True(f.Type("a b"));
        Assert.Equal("a b", f.Text);
    }

    [Fact]
    public void Length_is_capped()
    {
        var f = Field(max: 4);
        Assert.False(f.Type("abcdef"));
        Assert.Equal("abcd", f.Text);
        f.SetText(new string('x', 1000));
        Assert.Equal(4, f.Text.Length);
    }

    [Fact]
    public void Hit_test_uses_the_box()
    {
        var f = new TextField(10, 20, 30, 18, 8, masked: true, allowSpace: true);
        Assert.True(f.Contains(10, 20));
        Assert.True(f.Contains(39, 37));
        Assert.False(f.Contains(40, 20));
        Assert.False(f.Contains(10, 38));
    }
}
