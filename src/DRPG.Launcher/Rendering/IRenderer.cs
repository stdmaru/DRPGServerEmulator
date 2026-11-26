namespace DRPG.Launcher.Rendering;

/// <summary>A GPU texture owned by a renderer. Only the renderer that created it can draw it.</summary>
public interface ITexture : IDisposable
{
    int Width { get; }
    int Height { get; }
}

/// <summary>
/// Everything the launcher draws goes through this interface (docs/arquitetura/cliente.md). Coordinates are in the
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

    /// <summary>Draws only the left <paramref name="width"/> pixels of the texture (progress bars).</summary>
    void DrawLeft(ITexture texture, int x, int y, int width);
    void EndFrame();

    /// <summary>Copies the last frame (frame coordinates) to an RGBA8888 buffer, for screenshots and tests.</summary>
    uint[] Capture();
}
