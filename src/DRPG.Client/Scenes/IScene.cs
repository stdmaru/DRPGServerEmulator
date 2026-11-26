using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Scenes;

/// <summary>
/// A scene, with the shape of the original vtables (docs/cliente/cenas.md#interface-das-cenas-vtables): [0] destructor
/// (<see cref="IDisposable.Dispose"/>), [1] init, [2] frame. The frame both updates and draws, like the original.
/// </summary>
public interface IScene : IDisposable
{
    /// <summary>Minimum time between two frames; 0 = as fast as the display allows. The loading screen runs at 30 FPS.</summary>
    int FrameIntervalMs { get; }

    void Init();

    void Handle(InputEvent input);

    /// <summary>Updates and draws one frame. The cursor is drawn after it by the loop.</summary>
    void Frame(IRenderer renderer, ulong nowMs);
}
