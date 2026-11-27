using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Scenes;

/// <summary>
/// Owns the current scene and the switch between scenes (✅ v322 <c>FUN_00b500a0(n)</c>: frees the current scene and
/// creates and initializes the next). A switch asked during a frame takes effect after that frame ends.
/// </summary>
public sealed class SceneManager(Func<SceneId, IScene> create) : IDisposable
{
    private IScene? _current;
    private SceneId? _pending;

    public IScene? Current => _current;

    /// <summary>Asks for a switch; the scene changes when the running frame returns.</summary>
    public void Switch(SceneId id) => _pending = id;

    public void Handle(InputEvent input) => _current?.Handle(input);

    /// <summary>Applies a pending switch, then runs the frame of the current scene.</summary>
    public void Frame(IRenderer renderer, ulong nowMs)
    {
        ApplyPending();
        _current?.Frame(renderer, nowMs);
    }

    private void ApplyPending()
    {
        if (_pending is not { } id)
            return;
        _pending = null;
        _current?.Dispose();
        _current = null;
        var next = create(id);
        next.Init();
        _current = next;
    }

    public void Dispose()
    {
        _current?.Dispose();
        _current = null;
    }
}
