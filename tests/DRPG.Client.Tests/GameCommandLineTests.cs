namespace DRPG.Client.Tests;

public sealed class GameCommandLineTests
{
    private const string Key = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void Two_tokens_are_the_user_and_the_key()
    {
        var command = GameCommandLine.Parse(["tamer1", Key]);

        Assert.Equal("tamer1", command?.Username);
        Assert.Equal(Key, command?.Key);
    }

    [Fact]
    public void One_string_with_a_space_is_the_same_as_two_arguments()
    {
        // the original gets one command line string and splits it on spaces
        Assert.Equal(GameCommandLine.Parse(["tamer1", Key]), GameCommandLine.Parse([$"tamer1 {Key}"]));
    }

    [Theory]
    [InlineData]
    [InlineData("tamer1")]
    [InlineData("a", "b", "c")]
    [InlineData("a b c")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a", "")]
    public void Anything_but_exactly_two_tokens_is_refused(params string[] args)
    {
        Assert.Null(GameCommandLine.Parse(args));
    }

    [Theory]
    [InlineData("  a   b  ")]
    [InlineData("a  b")]
    [InlineData(" a b")]
    public void Extra_spaces_count_as_one_separator(string line)
    {
        Assert.Equal(new GameCommandLine("a", "b"), GameCommandLine.Parse([line]));
    }

    [Fact]
    public void The_key_stays_out_of_the_text_form()
    {
        Assert.DoesNotContain(Key, GameCommandLine.Parse(["tamer1", Key])!.ToString());
    }
}
