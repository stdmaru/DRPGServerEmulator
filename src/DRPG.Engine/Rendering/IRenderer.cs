namespace DRPG.Engine.Rendering;

/// <summary>A GPU texture owned by a renderer. Only the renderer that created it can draw it.</summary>
public interface ITexture : IDisposable
{
    int Width { get; }
    int Height { get; }
}

/// <summary>
/// Everything the engine draws goes through this interface (docs/arquitetura/cliente.md). Coordinates are in the
/// fixed 800×600 frame; the backend scales it into the window only by whole numbers, with black bars, and never stretches.
/// </summary>
public interface IRenderer : IDisposable
{
    const int FrameWidth = 800;
    const int FrameHeight = 600;

    string BackendName { get; }

    /// <summary>Uploads RGBA8888 pixels (R in the lowest byte, see <c>SpriteFile.Decode</c>); alpha 0 is transparent.</summary>
    ITexture CreateTexture(int width, int height, ReadOnlySpan<uint> pixels);

    void BeginFrame();
    void Draw(ITexture texture, int x, int y);

    /// <summary>Draws the columns <paramref name="sourceX"/> .. <paramref name="sourceX"/> + <paramref name="width"/>
    /// of the texture at (x, y): progress bars (left part) and text fields (the tail of a long text).</summary>
    void DrawPart(ITexture texture, int x, int y, int sourceX, int width);

    /// <summary>
    /// Subtracts <paramref name="amount"/> (0..255) from every colour channel of what is already in the frame, never
    /// below 0: the fade to black of the scene changes (✅ v322 <c>FUN_00b55f10</c>).
    /// </summary>
    void Darken(int amount);

    void EndFrame();

    /// <summary>Copies the last frame (frame coordinates) to an RGBA8888 buffer, for screenshots and tests.</summary>
    uint[] Capture();
}
