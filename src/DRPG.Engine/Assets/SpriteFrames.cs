using System.Collections;
using DRPG.Engine.Rendering;
using DRPG.Shared.Formats.Sprite;

namespace DRPG.Engine.Assets;

/// <summary>
/// The frames of a parsed sprite, each decoded and uploaded the first time it is read. For the big animated sprites
/// (128 frames of a tamer layer, of which a scene shows a handful): uploading all of them at once froze the screen.
/// The sprite's two optional layers come the same way: <see cref="Shadow"/> and <see cref="Edge"/>.
/// </summary>
public sealed class SpriteFrames : IReadOnlyList<ITexture>, IDisposable
{
    private readonly SpriteFile _sprite;
    private readonly SpriteLayer _layer;
    private readonly IRenderer _renderer;
    private readonly ITexture?[] _textures;

    public SpriteFrames(SpriteFile sprite, IRenderer renderer)
        : this(sprite, sprite.Image, renderer)
    {
        Shadow = sprite.Shadow is { } shadow ? new SpriteFrames(sprite, shadow, renderer) : null;
        Edge = sprite.Extra is { } edge ? new SpriteFrames(sprite, edge, renderer) : null;
    }

    private SpriteFrames(SpriteFile sprite, SpriteLayer layer, IRenderer renderer)
    {
        _sprite = sprite;
        _layer = layer;
        _renderer = renderer;
        _textures = new ITexture?[sprite.Frames.Count];
    }

    /// <summary>
    /// ✅ The first optional layer (<c>+0x1c..+0x24</c> of the v322 sprite, read by <c>0x00871e30</c>): the shadow, drawn blended
    /// under the sprite by <c>0x00b57460</c> with the <c>shadow</c> option. Null when the file has none.
    /// </summary>
    public SpriteFrames? Shadow { get; }

    /// <summary>
    /// ✅ The second optional layer (<c>+0x28..+0x30</c>, read by <c>0x00871df0</c>): the soft edge drawn blended by <c>0x00b57330</c>
    /// with the <c>anti</c> option. Null when the file has none.
    /// </summary>
    public SpriteFrames? Edge { get; }

    public int Count => _textures.Length;

    public ITexture this[int index]
    {
        get
        {
            if (_textures[index] is { } texture)
                return texture;
            var info = _sprite.Frames[index];
            if (_layer == _sprite.Image)
                return _textures[index] = _renderer.CreateTexture(info.Width, info.Height, _sprite.Decode(_layer, index));
            try
            {
                return _textures[index] = _renderer.CreateTexture(info.Width, info.Height, _sprite.Decode(_layer, index));
            }
            catch (SpriteFormatException)
            {
                // ✅ 0x00871e30/0x00871df0 give no pixels for a frame outside the layer: nothing is drawn
                return _textures[index] = _renderer.CreateTexture(0, 0, []);
            }
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
        Shadow?.Dispose();
        Edge?.Dispose();
    }
}
