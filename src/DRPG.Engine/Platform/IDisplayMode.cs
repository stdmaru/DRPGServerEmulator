namespace DRPG.Engine.Platform;

/// <summary>Switches the game window between a window and the full screen, while the game runs.</summary>
public interface IDisplayMode
{
    void SetFullscreen(bool fullscreen);
}
