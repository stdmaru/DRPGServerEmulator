using System.Diagnostics;
using DRPG.Launcher.Patching;
using DRPG.Shared.Protocol.Launcher;

namespace DRPG.Launcher;

/// <summary>
/// Starts <c>LanuncherInfo.RunExeFileName</c> from the install with <c>&lt;game username&gt; &lt;game key&gt;</c>, the
/// command line of the official launcher (docs/protocolo/servidores.md). On Linux, <c>[Game] Runner</c> (for
/// example <c>wine</c>) runs it. Everything that reaches the command line is checked first: the server's answer is
/// not trusted to be well formed.
/// </summary>
public static class GameStarter
{
    public static ProcessStartInfo Build(string installDirectory, string runExeFileName, string? runner, string gameUsername, string gameKey)
    {
        if (string.IsNullOrWhiteSpace(runExeFileName) || runExeFileName.Contains('/') || runExeFileName.Contains('\\'))
            throw new GameStartException("DR_Configuration.json: RunExeFileName must be a file name in the game folder.");
        var exe = Patcher.SafeTarget(installDirectory, runExeFileName)
            ?? throw new GameStartException("DR_Configuration.json: RunExeFileName is not allowed.");
        if (!File.Exists(exe))
            throw new GameStartException($"{runExeFileName} not found in the game folder.");
        if (gameUsername.Length is 0 or > LauncherLimits.MaxGameUsernameBytes || !gameUsername.All(c => c is > ' ' and < (char)127))
            throw new GameStartException("The server sent an invalid game username.");
        if (gameKey.Length != LauncherLimits.GameKeyChars || !gameKey.All(char.IsAsciiHexDigit))
            throw new GameStartException("The server sent an invalid game key.");

        var info = new ProcessStartInfo
        {
            FileName = runner ?? exe,
            WorkingDirectory = installDirectory,
            UseShellExecute = false,
        };
        if (runner is not null)
            info.ArgumentList.Add(exe);
        info.ArgumentList.Add(gameUsername);
        info.ArgumentList.Add(gameKey);
        return info;
    }

    /// <summary>Returns null when the game started, otherwise the message for the status box.</summary>
    public static string? Start(string installDirectory, string runExeFileName, string? runner, string gameUsername, string gameKey)
    {
        try
        {
            using var process = Process.Start(Build(installDirectory, runExeFileName, runner, gameUsername, gameKey));
            return process is null ? "The game did not start." : null;
        }
        catch (GameStartException e)
        {
            return e.Message;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return $"The game did not start: {e.Message}";
        }
    }
}

public sealed class GameStartException(string message) : Exception(message);
