using System.Runtime.InteropServices;
using DRPG.Engine.Rendering;
using DRPG.Engine.Rendering.Backends;
using SDL;
using static SDL.SDL3;

namespace DRPG.Engine.Platform;

/// <summary>Input in frame coordinates (800×600), already converted from window pixels.</summary>
public abstract record InputEvent;
public sealed record QuitEvent : InputEvent;
public sealed record MouseMoveEvent(int X, int Y) : InputEvent;
public sealed record MouseLeaveEvent : InputEvent;
public sealed record MouseButtonEvent(int X, int Y, bool Down) : InputEvent;
public sealed record MouseWheelEvent(int Delta) : InputEvent;
public sealed record KeyEvent(GameKey Key) : InputEvent;
public sealed record TextEvent(string Text) : InputEvent;

public enum GameKey { Other, Enter, Escape, Tab, Backspace }

/// <summary>SDL window, renderer and event pump. Nothing else in a game executable calls SDL directly.</summary>
public sealed unsafe class SdlPlatform : IDisplayMode, IDisposable
{
    private readonly SDL_Window* _window;
    private readonly SdlRenderer _renderer;

    public SdlPlatform(string title, bool fullscreen, bool hidden)
    {
        // Linux: prefer X11 (XWayland under Wayland). Hyprland honours X11 fixed-size hints, like it does for the
        // original game under Wine, but ignores them for native Wayland windows. An SDL_VIDEO_DRIVER environment
        // variable still wins over this hint (offscreen screenshots, users who want Wayland).
        if (OperatingSystem.IsLinux())
            SDL_SetHint("SDL_VIDEO_DRIVER", "x11,wayland");
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
            throw new InvalidOperationException($"SDL_Init: {SDL_GetError()}");
        // Fixed 800×600 window: not resizable, so sprites are never scaled by a fraction (docs/cliente/tela-login-launcher.md).
        var flags = SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY;
        if (hidden)
            flags |= SDL_WindowFlags.SDL_WINDOW_HIDDEN;
        _window = SDL_CreateWindow(title, IRenderer.FrameWidth, IRenderer.FrameHeight, flags);
        if (_window == null)
            throw new InvalidOperationException($"SDL_CreateWindow: {SDL_GetError()}");
        // min = max asks the compositor for a fixed size. Some tiling compositors (Hyprland) ignore it, so the
        // renderer also copes with any window size (SdlRenderer.BeginFrame).
        SDL_SetWindowMinimumSize(_window, IRenderer.FrameWidth, IRenderer.FrameHeight);
        SDL_SetWindowMaximumSize(_window, IRenderer.FrameWidth, IRenderer.FrameHeight);
        if (fullscreen)
            SDL_SetWindowFullscreen(_window, true);
        _renderer = new SdlRenderer(_window);
        SDL_StartTextInput(_window);
        // The game's cursor is a sprite drawn by the launcher (CursorView); the system one would sit on top of it.
        if (!hidden)
            SDL_HideCursor();
    }

    public IRenderer Renderer => _renderer;

    /// <summary>The window keeps its fixed 800×600 size; the renderer letterboxes it on a full screen.</summary>
    public void SetFullscreen(bool fullscreen) => SDL_SetWindowFullscreen(_window, fullscreen);

    public static ulong TicksMs => SDL_GetTicks();

    /// <summary>
    /// A modal error box (<c>SDL_ShowSimpleMessageBox</c>), usable before any window exists: the original shows its
    /// start-up errors with <c>MessageBoxA</c>.
    /// </summary>
    public static void ShowError(string title, string text) =>
        SDL_ShowSimpleMessageBox(SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR, title, text, null);

    /// <summary>Waits without spinning; used to hold a scene to its own frame rate.</summary>
    public static void Delay(uint milliseconds) => SDL_Delay(milliseconds);

    public void Poll(List<InputEvent> events)
    {
        events.Clear();
        SDL_Event e;
        while (SDL_PollEvent(&e))
        {
            switch (e.Type)
            {
                case SDL_EventType.SDL_EVENT_QUIT:
                    events.Add(new QuitEvent());
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                {
                    var (x, y) = _renderer.ToFrame(e.motion.x, e.motion.y);
                    events.Add(new MouseMoveEvent(x, y));
                }
                    break;
                case SDL_EventType.SDL_EVENT_WINDOW_MOUSE_LEAVE:
                    events.Add(new MouseLeaveEvent());
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN or SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP when e.button.button == 1:
                {
                    var (x, y) = _renderer.ToFrame(e.button.x, e.button.y);
                    events.Add(new MouseButtonEvent(x, y, e.button.down));
                }
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                    if (e.wheel.y > 0)
                        events.Add(new MouseWheelEvent(1));
                    else if (e.wheel.y < 0)
                        events.Add(new MouseWheelEvent(-1));
                    break;
                case SDL_EventType.SDL_EVENT_KEY_DOWN:
                    events.Add(new KeyEvent(e.key.key switch
                    {
                        SDL_Keycode.SDLK_RETURN or SDL_Keycode.SDLK_KP_ENTER => GameKey.Enter,
                        SDL_Keycode.SDLK_ESCAPE => GameKey.Escape,
                        SDL_Keycode.SDLK_TAB => GameKey.Tab,
                        SDL_Keycode.SDLK_BACKSPACE => GameKey.Backspace,
                        _ => GameKey.Other,
                    }));
                    break;
                case SDL_EventType.SDL_EVENT_TEXT_INPUT:
                    events.Add(new TextEvent(Marshal.PtrToStringUTF8((IntPtr)e.text.text) ?? ""));
                    break;
            }
        }
    }

    public void Dispose()
    {
        _renderer.Dispose();
        SDL_DestroyWindow(_window);
        SDL_Quit();
    }
}
