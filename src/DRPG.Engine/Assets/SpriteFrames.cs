using System.Collections;
using DRPG.Engine.Rendering;
using DRPG.Shared.Formats.Sprite;

namespace DRPG.Engine.Assets;

/// <summary>
/// The frames of a parsed sprite, each decoded and uploaded the first time it is read. For the big animated sprites
/// (128 frames of a tamer layer, of which a scene shows a handful): uploading all of them at once froze the screen.
/// </summary>
public sealed class SpriteFrames(SpriteFile sprite, IRenderer renderer) : IReadOnlyList<ITexture>, IDisposable
{
    private readonly ITexture?[] _textures = new ITexture?[sprite.Frames.Count];

    public int Count => _textures.Length;

    public ITexture this[int index]
    {
        get
        {
            if (_textures[index] is { } texture)
                return texture;
            var info = sprite.Frames[index];
            return _textures[index] = renderer.CreateTexture(info.Width, info.Height, sprite.Decode(sprite.Image, index));
        }
    }

    /// <summary>How many frames are uploaded so far.</summary>
    public int Uploaded => _textures.Count(t => t is not null);

    public IEnumerator<ITexture> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        for (int i = 0; i < _textures.Length; i++)
        {
            _textures[i]?.Dispose();
            _textures[i] = null;
        }
    }
}
