using DRPG.Engine.Audio;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Ui;

/// <summary>
/// The window manager of the field (✅ <c>DAT_01158500</c>, docs/cliente/janelas.md): the windows in drawing order, the last one on top.
/// The original makes all 0x1e6 windows at start (<c>0x00b9a170</c> → factory <c>0x00b930f0</c>); here only the windows the client has
/// are registered, and an id with no window does nothing, like a window the factory does not make (<c>0x00b99f70</c> returns 0).
/// </summary>
public sealed class WindowManager(SoundBank sounds)
{
    /// <summary>✅ The minimap (window 5) opens and closes without a sound; 0x1d opens without one too.</summary>
    public const int MiniMapWindow = 5, SilentOpenWindow = 0x1d;

    /// <summary>✅ The sounds of the game scene (<c>0x00b4bab0</c>): <c>Sound\w_on.wav</c> and <c>Sound\w_off.wav</c>.</summary>
    public const int OpenSound = 0xe, CloseSound = 0xf;

    /// <summary>
    /// ✅ The windows Escape never closes (<c>0x00b93030</c>): the minimap, 7..10, 0x12, 0x16, 0x21, 0x42, 0x68, 0x3f, 0x123, the quest
    /// panels 0x17b and 0x1ac.
    /// </summary>
    private static readonly HashSet<int> KeptOnEscape = [5, 7, 8, 9, 10, 0x12, 0x16, 0x21, 0x42, 0x68, 0x3f, 0x123, 0x17b, 0x1ac];

    /// <summary>✅ While the exit window counts down, only it is drawn (<c>0x00b9a7c0</c>).</summary>
    public const int ExitWindow = 6;

    private readonly List<GameWindow> _windows = [];

    /// <summary>
    /// ✅ <c>[DAT_01158504]+0x3eb0</c>: set while the exit window counts down. No window opens (<c>0x00b9a290</c>), no key is read
    /// (<c>0x00b4c080</c>), the ground takes no click (<c>0x00b4c1a0</c>) and only the exit window is drawn.
    /// </summary>
    public bool Locked { get; set; }

    public IReadOnlyList<GameWindow> Windows => _windows;

    public void Register(GameWindow window)
    {
        if (Find(window.Id) is not null)
            throw new InvalidOperationException($"window {window.Id} is registered twice");
        _windows.Add(window);
    }

    /// <summary>✅ <c>0x00b99f70</c>: the window with that id, or null.</summary>
    public GameWindow? Find(int id) => _windows.Find(w => w.Id == id);

    public T Get<T>() where T : GameWindow => _windows.OfType<T>().Single();

    /// <summary>✅ <c>0x00b9a230</c>.</summary>
    public bool IsOpen(int id) => Find(id)?.Visible == true;

    /// <summary>
    /// ✅ <c>0x00b9a290</c>: a window that exists and is closed opens on top of the others (moved to the end of the list,
    /// <c>0x00b99fa0</c>) with <see cref="OpenSound"/>, except the minimap and 0x1d. Nothing opens while <see cref="Locked"/>.
    /// ❓ The cases for windows the client does not have yet (0xb, 0xc..0xe, 0x12, 0x42, 0xd8, 0x88) are left out.
    /// </summary>
    public void Open(int id)
    {
        if (Locked || Find(id) is not { Visible: false } window)
            return;
        window.Open();
        _windows.Remove(window);
        _windows.Add(window);
        if (id is not MiniMapWindow and not SilentOpenWindow)
            sounds.PlayEffect(OpenSound);
    }

    /// <summary>✅ <c>0x00b9a120</c>: an open window closes with <see cref="CloseSound"/>, except the minimap.</summary>
    public void Close(int id)
    {
        if (Find(id) is not { Visible: true } window)
            return;
        window.Close();
        if (id != MiniMapWindow)
            sounds.PlayEffect(CloseSound);
    }

    /// <summary>✅ <c>0x00b9a290</c> / <c>0x00b9a120</c> as the bar's menu buttons use them: closes an open window, opens a closed one.</summary>
    public void Toggle(int id)
    {
        if (IsOpen(id))
            Close(id);
        else
            Open(id);
    }

    /// <summary>✅ <c>0x00b9a7c0</c>: every visible window, in order; only the exit window while <see cref="Locked"/>.</summary>
    public void Frame(IRenderer renderer, MouseState mouse, ulong nowMs)
    {
        // a window may open or close another one from its frame: walk a copy
        foreach (var window in _windows.ToArray())
        {
            if (!window.Visible || (Locked && window.Id != ExitWindow))
                continue;
            if (window.Contains(mouse.X, mouse.Y))
                mouse.Hot = window;                                    // ✅ 0x00b641b0: the window under the mouse is the hot control
            window.Frame(renderer, mouse, nowMs);
        }
    }

    /// <summary>
    /// ✅ <c>0x00b93030</c>: from the top, the first visible window Escape may close gets the key (<c>Key(Escape)</c>) and, if still
    /// visible, is closed directly (vtable <c>+0x1c</c>, no sound). True when one was found.
    /// </summary>
    public bool CloseTopmost()
    {
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            var window = _windows[i];
            if (!window.Visible || KeptOnEscape.Contains(window.Id))
                continue;
            window.Key(GameKey.Escape);
            if (window.Visible)
                window.Close();
            return true;
        }
        return false;
    }

    /// <summary>
    /// A visible window is under the point, so the ground takes no click. ❓ The original's mouse handler (<c>0x00b4c1a0</c>) lists the
    /// windows that block the ground one by one; the windows the client has are all of that kind (a click on the minimap walks to the
    /// spot it shows, <c>0x00b9d120</c>, never to the ground under it).
    /// </summary>
    public bool Covers(int x, int y) => _windows.Exists(w => w.Visible && w.Contains(x, y));
}
