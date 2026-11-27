namespace DRPG.Client;

/// <summary>
/// The command line of <c>DigimonRPG.bin</c> (✅ v322 <c>0x00aa24b0</c>): <c>&lt;username&gt; &lt;key&gt;</c>, exactly two
/// tokens separated by spaces; the launcher passes the game username and the 40-character game key.
/// </summary>
public sealed record GameCommandLine(string Username, string Key)
{
    /// <summary>
    /// ❓ Text of the original error box (<c>MessageBoxA</c> with title "DigimonRPG", text at <c>0x01029990</c>, not
    /// readable in the dump); this one is ours.
    /// </summary>
    public const string ErrorText = "Please start the game from the launcher.";

    public const string ErrorTitle = "DigimonRPG";

    /// <summary>
    /// Joins the arguments with a space and splits on spaces. ❓ The original splits a <c>std::string</c> on 0x20 with a
    /// helper (<c>FUN_00f06800</c>); repeated, leading and trailing spaces are treated as one separator here.
    /// Returns null unless there are exactly two tokens.
    /// </summary>
    public static GameCommandLine? Parse(IReadOnlyList<string> args)
    {
        var tokens = string.Join(' ', args).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 2 ? new GameCommandLine(tokens[0], tokens[1]) : null;
    }

    /// <summary>Never prints the key.</summary>
    public override string ToString() => $"{nameof(GameCommandLine)}({Username})";
}
