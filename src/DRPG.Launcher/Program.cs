namespace DRPG.Launcher;

/// <summary>
/// Entry point. The window (SDL3, old login screen) comes next; for now the launcher only has its
/// non-visual parts: patch configuration, patch merge and the LoginServer client.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        Console.Error.WriteLine("DRPG.Launcher: user interface not implemented yet.");
        return 1;
    }
}
