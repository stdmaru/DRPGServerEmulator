using System.Runtime.InteropServices;
using DRPG.Launcher.Rendering;
using DRPG.Launcher.Rendering.Backends;
using SDL;
using static SDL.SDL3;

namespace DRPG.Launcher.Platform;

/// <summary>Input in frame coordinates (800×600), already converted from window pixels.</summary>
public abstract record InputEvent;
public sealed record QuitEvent : InputEvent;
public sealed record MouseMoveEvent(int X, int Y) : InputEvent;
public sealed record MouseButtonEvent(int X, int Y, bool Down) : InputEvent;
public sealed record KeyEvent(LauncherKey Key) : InputEvent;
public sealed record TextEvent(string Text) : InputEvent;

public enum LauncherKey { Other, Enter, Escape, Tab, Backspace }

/// <summary>SDL window, renderer and event pump. Nothing else in the launcher calls SDL directly.</summary>
public sealed unsafe class SdlPlatform : IDisposable
{
    private readonly SDL_Window* _window;
    private readonly SdlRenderer _renderer;

    public SdlPlatform(string title, bool fullscreen, bool hidden)
    {
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
            throw new InvalidOperationException($"SDL_Init: {SDL_GetError()}");
        // Fixed 800×600 window: not resizable, so sprites are never scaled by a fraction (docs/cliente/tela-login-launcher.md).
        var flags = SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY;
        if (hidden)
            flags |= SDL_WindowFlags.SDL_WINDOW_HIDDEN;
        _window = SDL_CreateWindow(title, IRenderer.FrameWidth, IRenderer.FrameHeight, flags);
        if (_window == null)
            throw new InvalidOperationException($"SDL_CreateWindow: {SDL_GetError()}");
        if (fullscreen)
            SDL_SetWindowFullscreen(_window, true);
        _renderer = new SdlRenderer(_window);
        SDL_StartTextInput(_window);
    }

    public IRenderer Renderer => _renderer;

    public static ulong TicksMs => SDL_GetTicks();

    public void Poll(List<InputEvent> events)
    {
        events.Clear();
        SDL_Event e;
        while (SDL_PollEvent(&e))
        {
            _renderer.ToFrameCoordinates(&e);
            switch (e.Type)
            {
                case SDL_EventType.SDL_EVENT_QUIT:
                    events.Add(new QuitEvent());
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                    events.Add(new MouseMoveEvent((int)e.motion.x, (int)e.motion.y));
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN or SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP when e.button.button == 1:
                    events.Add(new MouseButtonEvent((int)e.button.x, (int)e.button.y, e.button.down));
                    break;
                case SDL_EventType.SDL_EVENT_KEY_DOWN:
                    events.Add(new KeyEvent(e.key.key switch
                    {
                        SDL_Keycode.SDLK_RETURN or SDL_Keycode.SDLK_KP_ENTER => LauncherKey.Enter,
                        SDL_Keycode.SDLK_ESCAPE => LauncherKey.Escape,
                        SDL_Keycode.SDLK_TAB => LauncherKey.Tab,
                        SDL_Keycode.SDLK_BACKSPACE => LauncherKey.Backspace,
                        _ => LauncherKey.Other,
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
