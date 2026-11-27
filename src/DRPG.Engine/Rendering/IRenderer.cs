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

    /// <summary>Draws a rectangular sub-region of <paramref name="texture"/> at (x, y), for clipped text boxes.</summary>
    void DrawRegion(ITexture texture, int x, int y, int sourceX, int sourceY, int width, int height);

    /// <summary>The scale of <see cref="DrawRegionBlended"/>'s alpha: 32 = opaque, as in the original's blends.</summary>
    const int OpaqueAlpha = 32;

    /// <summary>
    /// <see cref="DrawRegion"/> mixed with what is already in the frame: <c>(source · alpha + frame · (32 − alpha)) / 32</c> per channel,
    /// <paramref name="alpha"/> clamped to 0..<see cref="OpaqueAlpha"/>; transparent pixels stay out (✅ v322 <c>0x00874710</c>/<c>0x008749d0</c>,
    /// the mode −1 of <c>0x00876a30</c>).
    /// </summary>
    void DrawRegionBlended(ITexture texture, int x, int y, int sourceX, int sourceY, int width, int height, int alpha);

    /// <summary>
    /// Subtracts <paramref name="amount"/> (0..255) from every colour channel of what is already in the frame, never
    /// below 0: the fade to black of the scene changes (✅ v322 <c>FUN_00b55f10</c>).
    /// </summary>
    void Darken(int amount);

    void EndFrame();

    /// <summary>Copies the last frame (frame coordinates) to an RGBA8888 buffer, for screenshots and tests.</summary>
    uint[] Capture();
}
