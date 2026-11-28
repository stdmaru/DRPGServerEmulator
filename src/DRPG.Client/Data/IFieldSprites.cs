using DRPG.Client.Ui;
using DRPG.Engine.Rendering;

namespace DRPG.Client.Data;

/// <summary>
/// The sprites the field opens while it runs (<see cref="AnimatedSprites"/>). A missing or broken file is null and logged once by
/// the implementation, never an error: the entity is simply not drawn.
/// </summary>
public interface IFieldSprites
{
    /// <summary>✅ <c>0x00b23d20</c> / <c>0x00b46120</c>: the animation of a digimon species.</summary>
    SpriteAnimator? OpenDigimon(int species);

    /// <summary>✅ <c>0x00b40d30</c>: the animation <c>Sprite\&lt;name&gt;.ani</c> of a table NPC.</summary>
    SpriteAnimator? OpenNpc(string spriteName);

    /// <summary>✅ <c>0x00b21cd0</c>: a tamer body and its default clothes.</summary>
    TamerDoll? OpenTamer(int tamerId);

    /// <summary>The frames of an effect sprite (<c>Effect\*.spr</c>).</summary>
    IReadOnlyList<ITexture>? OpenEffect(string path);
}
