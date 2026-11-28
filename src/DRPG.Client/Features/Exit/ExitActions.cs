using DRPG.Client.Net;

namespace DRPG.Client.Features.Exit;

/// <summary>What the exit window asks of the server, the only place of the feature that sends a packet.</summary>
public sealed class ExitActions(WorldSession session)
{
    /// <summary>✅ <c>0x00ae4bf0</c>: <c>C2S 0x2B</c>, empty.</summary>
    public void ExitGame() => session.ExitGame();
}
