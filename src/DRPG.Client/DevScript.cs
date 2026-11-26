using DRPG.Engine.Platform;

namespace DRPG.Client;

/// <summary>
/// Development only (like <c>DRPG_SCREENSHOT</c>): input played into the game at given times, so a screenshot can show
/// a hover or a click without a person. <c>DRPG_SCRIPT="3000:move:130:200;3100:down;3200:up;3300:key:esc"</c>, times in
/// milliseconds since the loading screen ended. Anything it cannot read is ignored.
/// </summary>
public sealed class DevScript
{
    public const string Variable = "DRPG_SCRIPT";

    private readonly List<(ulong At, InputEvent Event)> _events;
    private int _next;

    private DevScript(List<(ulong, InputEvent)> events) => _events = events;

    public static DevScript? FromEnvironment() => Parse(Environment.GetEnvironmentVariable(Variable));

    public static DevScript? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var events = new List<(ulong, InputEvent)>();
        int x = 0, y = 0;
        foreach (var step in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = step.Split(':');
            if (parts.Length < 2 || !ulong.TryParse(parts[0], out var at))
                continue;
            switch (parts[1])
            {
                case "move" when parts.Length == 4 && int.TryParse(parts[2], out var mx) && int.TryParse(parts[3], out var my):
                    (x, y) = (mx, my);
                    events.Add((at, new MouseMoveEvent(x, y)));
                    break;
                case "down":
                    events.Add((at, new MouseButtonEvent(x, y, true)));
                    break;
                case "up":
                    events.Add((at, new MouseButtonEvent(x, y, false)));
                    break;
                case "key" when parts.Length == 3:
                    events.Add((at, new KeyEvent(parts[2] == "esc" ? GameKey.Escape : GameKey.Other)));
                    break;
            }
        }
        return events.Count == 0 ? null : new DevScript(events.OrderBy(e => e.Item1).ToList());
    }

    /// <summary>The events due at <paramref name="elapsedMs"/> that were not given yet.</summary>
    public IEnumerable<InputEvent> Due(ulong elapsedMs)
    {
        while (_next < _events.Count && _events[_next].At <= elapsedMs)
            yield return _events[_next++].Event;
    }
}
