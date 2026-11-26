namespace DRPG.Engine.Rendering;

/// <summary>A TTF face at one size that turns a line of text into a texture (RGBA8888, R in the low byte).</summary>
public interface IFont : IDisposable
{
    int LineHeight { get; }

    ITexture Render(IRenderer renderer, string text, uint rgba);
}
