namespace DRPG.Launcher;

/// <summary>
/// The "remember ID" checkbox, kept in the game's <c>option.ini</c> under a section of our own:
/// <code>
/// [DRPGLauncher]
/// Remember=1
/// Login=player
/// </code>
/// ❓ The official launcher writes <c>[Member] id=1</c> there, but whether that is this flag or something else is
/// unknown (it is packed), so that key is left alone. Only the login is stored, never the password.
/// </summary>
public sealed class RememberedLogin(string optionIniPath)
{
    public const string Section = "DRPGLauncher";
    public const int MaxLoginLength = 64;

    public bool Remember { get; private set; }
    public string Login { get; private set; } = "";

    public static RememberedLogin Load(string optionIniPath)
    {
        var state = new RememberedLogin(optionIniPath);
        try
        {
            var values = IniFile.Read(optionIniPath);
            state.Remember = values.TryGetValue($"{Section}.remember", out var r) && r == "1";
            if (state.Remember && values.TryGetValue($"{Section}.login", out var login) && IsStorable(login))
                state.Login = login;
        }
        catch (IOException)
        {
            // unreadable option.ini: start unchecked
        }
        catch (UnauthorizedAccessException)
        {
        }
        return state;
    }

    /// <summary>Flips the checkbox and saves it; unchecking also forgets the login.</summary>
    public void Toggle()
    {
        Remember = !Remember;
        if (!Remember)
            Login = "";
        Save();
    }

    /// <summary>Called after a successful login: stores the login only while the box is checked.</summary>
    public void LoggedIn(string login)
    {
        if (!Remember || !IsStorable(login))
            return;
        Login = login;
        Save();
    }

    private void Save()
    {
        try
        {
            IniFile.Set(optionIniPath, Section, "Remember", Remember ? "1" : "0");
            IniFile.Set(optionIniPath, Section, "Login", Login);
        }
        catch (IOException e)
        {
            Console.Error.WriteLine($"option.ini not saved: {e.Message}");
        }
        catch (UnauthorizedAccessException e)
        {
            Console.Error.WriteLine($"option.ini not saved: {e.Message}");
        }
    }

    /// <summary>Printable ASCII without INI syntax, so a hostile login cannot add lines or sections.</summary>
    private static bool IsStorable(string login) =>
        login.Length is > 0 and <= MaxLoginLength && login.All(c => c is > ' ' and < (char)127 and not (';' or '[' or ']' or '='));
}
