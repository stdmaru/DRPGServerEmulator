using DRPG.Client.Data;
using DRPG.Client.Ui;
using DRPG.Engine.Rendering;
using DRPG.Shared.Formats.Map;

namespace DRPG.Client.World;

/// <summary>
/// Renders and animates map portal swirls driven by <see cref="MapCell.Type"/> (✅ v322 <c>0x00b380d0</c>,
/// <c>0x00a159a0</c>, <c>0x00a121f0</c>, <c>0x00876a30</c> mode 5 additive); the warp-out of a map change is <see cref="WarpEffect"/>:
/// <list type="bullet">
///   <item><c>1000</c> / <c>1005</c>: <c>Effect\effect_portal.spr</c>, ground layer, centred on cell, mode 5 additive.</item>
///   <item><c>1001</c>: <c>Effect\effect_portal2.spr</c>, ground layer, offset <c>(-11, +19 - h)</c>, mode 5 additive.</item>
///   <item><c>1002</c>: <c>Effect\effect_portal3.spr</c>, overlay layer (above entities), offset <c>(-46, +21 - h)</c>, mode 5 additive.</item>
///   <item><c>1003</c>: <c>Effect\effect_portal_square.spr</c>, ground layer, centred on cell, opaque.</item>
/// </list>
/// </summary>
public sealed class FieldPortals(IFieldSprites sprites)
{
    public const int TypeStandard = 1000;
    public const int TypePortal2 = 1001;
    public const int TypePortal3 = 1002;
    public const int TypeSquare = 1003;
    public const int TypeStandardAlt = 1005;

    /// <summary>✅ <c>0x00b4e550</c> line 605: cells with <c>1 &lt;= type &lt;= 8999</c> show <c>cursor_door.spr</c> (case 10).</summary>
    public const int MinPortalCursorType = 1, MaxPortalCursorType = 8999;

    /// <summary>✅ <c>0x00b380d0</c>: visible cell margins around the camera window.</summary>
    public const int MarginLeft = 15, MarginTop = 20, MarginRight = 30, MarginBottom = 40;

    /// <summary>✅ <c>0x00a121f0</c> lines 568-591: the standard swirls advance every 2 logic steps, the square one every 4.</summary>
    public const int StandardStepsPerFrame = 2, SquareStepsPerFrame = 4;

    /// <summary>✅ <c>0x00b380d0</c>: the corner of the 1001 swirl from the cell's top-left corner, its bottom at <c>+19</c>.</summary>
    private const int Portal2OffsetX = -11, Portal2Bottom = 19;

    /// <summary>✅ <c>0x00b380d0</c>: the corner of the 1002 swirl from the cell's top-left corner, its bottom at <c>+21</c>.</summary>
    private const int Portal3OffsetX = -46, Portal3Bottom = 21;

    private readonly EffectFrames _portal1 = new(sprites, "Effect\\effect_portal.spr");
    private readonly EffectFrames _portal2 = new(sprites, "Effect\\effect_portal2.spr");
    private readonly EffectFrames _portal3 = new(sprites, "Effect\\effect_portal3.spr");
    private readonly EffectFrames _portalSquare = new(sprites, "Effect\\effect_portal_square.spr");

    private int _portalTick;
    private int _portalFrame;
    private int _squareTick;
    private int _squareFrame;

    internal int PortalFrame => _portalFrame;
    internal int SquareFrame => _squareFrame;

    /// <summary>✅ <c>0x00b4e550</c> line 605: true when a cell type switches the cursor to <c>Interface\cursor_door.spr</c>.</summary>
    public static bool IsPortalCursorType(int cellType) => cellType is >= MinPortalCursorType and <= MaxPortalCursorType;

    /// <summary>✅ <c>0x00a121f0</c> lines 568-591: advances the swirl frames (once per 30 FPS logic step).</summary>
    public void Step()
    {
        if (++_portalTick >= StandardStepsPerFrame)
        {
            _portalTick = 0;
            _portalFrame++;
            if (_portal1.Frames is { Count: > 0 } standard && _portalFrame >= standard.Count)
                _portalFrame = 0;
        }
        if (++_squareTick >= SquareStepsPerFrame)
        {
            _squareTick = 0;
            _squareFrame++;
            if (_portalSquare.Frames is { Count: > 0 } square && _squareFrame >= square.Count)
                _squareFrame = 0;
        }
    }

    /// <summary>✅ <c>0x00b380d0</c> + <c>0x00a121f0(0)</c>: draws ground-layer portals (types 1000, 1001, 1003, 1005) before entities.</summary>
    public void DrawGround(IRenderer renderer, FieldMap field, int cameraX, int cameraY, SpriteOptions options)
    {
        var bounds = VisibleBounds(field.Map, cameraX, cameraY);
        for (int x = bounds.FirstX; x <= bounds.LastX; x++)
        {
            int screenX = x * MapFile.CellWidth - cameraX;
            for (int y = bounds.FirstY; y <= bounds.LastY; y++)
            {
                int screenY = y * MapFile.CellHeight - cameraY;
                switch (field.Map[x, y].Type)
                {
                    case TypeStandard:
                    case TypeStandardAlt:
                        if (_portal1.Frame(_portalFrame) is { } standard)
                            DrawMode5(renderer, standard, CenteredX(screenX, standard), CenteredY(screenY, standard), options);
                        break;
                    case TypePortal2:
                        if (_portal2.Frame(_portalFrame) is { } portal2)
                            DrawMode5(renderer, portal2, screenX + Portal2OffsetX, screenY + Portal2Bottom - portal2.Height, options);
                        break;
                    case TypeSquare:
                        if (_portalSquare.Frame(_squareFrame) is { } square)
                            renderer.Draw(square, CenteredX(screenX, square), CenteredY(screenY, square));
                        break;
                }
            }
        }
    }

    /// <summary>✅ <c>0x00b380d0</c> + <c>0x00a121f0(1)</c>: draws overlay-layer portals (type 1002) after the y-sorted entity queue.</summary>
    public void DrawOverlay(IRenderer renderer, FieldMap field, int cameraX, int cameraY, SpriteOptions options)
    {
        var bounds = VisibleBounds(field.Map, cameraX, cameraY);
        for (int x = bounds.FirstX; x <= bounds.LastX; x++)
        {
            int screenX = x * MapFile.CellWidth - cameraX;
            for (int y = bounds.FirstY; y <= bounds.LastY; y++)
            {
                if (field.Map[x, y].Type != TypePortal3 || _portal3.Frame(_portalFrame) is not { } portal3)
                    continue;
                int screenY = y * MapFile.CellHeight - cameraY;
                DrawMode5(renderer, portal3, screenX + Portal3OffsetX, screenY + Portal3Bottom - portal3.Height, options);
            }
        }
    }

    /// <summary>✅ <c>0x00b569f0(..., mode=5, ...)</c> → <c>0x00876a30</c> case 5: additive when the <c>alpha</c> option is on, plain otherwise.</summary>
    internal static void DrawMode5(IRenderer renderer, ITexture texture, int x, int y, SpriteOptions options)
    {
        if (options.Alpha)
            renderer.DrawAdditive(texture, x, y);
        else
            renderer.Draw(texture, x, y);
    }

    private static int CenteredX(int screenX, ITexture texture) => screenX + MapFile.CellWidth / 2 - texture.Width / 2;

    private static int CenteredY(int screenY, ITexture texture) => screenY + MapFile.CellHeight / 2 - texture.Height / 2;

    private readonly record struct CellBounds(int FirstX, int LastX, int FirstY, int LastY);

    private static CellBounds VisibleBounds(MapFile map, int cameraX, int cameraY)
    {
        int cellX = cameraX / MapFile.CellWidth;
        int cellY = cameraY / MapFile.CellHeight;
        return new CellBounds(
            Math.Max(0, cellX - MarginLeft),
            Math.Min(map.Width - 1, cellX + FieldMap.ViewCellsX + MarginRight),
            Math.Max(0, cellY - MarginTop),
            Math.Min(map.Height - 1, cellY + FieldMap.ViewCellsY + MarginBottom));
    }
}
