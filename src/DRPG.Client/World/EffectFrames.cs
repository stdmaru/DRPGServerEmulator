using DRPG.Client.Data;
using DRPG.Engine.Rendering;

namespace DRPG.Client.World;

/// <summary>An effect sprite opened on first use and kept; a missing or broken file is opened (and logged) once and draws nothing.</summary>
public sealed class EffectFrames(IFieldSprites sprites, string path)
{
    private IReadOnlyList<ITexture>? _frames;
    private bool _opened;

    /// <summary>The frames, or null when the file is missing or broken.</summary>
    public IReadOnlyList<ITexture>? Frames
    {
        get
        {
            if (!_opened)
            {
                _opened = true;
                _frames = sprites.OpenEffect(path);
            }
            return _frames;
        }
    }

    /// <summary>Frame <paramref name="index"/> (wrapped), or null when there are no frames.</summary>
    public ITexture? Frame(int index) => Frames is { Count: > 0 } frames ? frames[index % frames.Count] : null;
}
