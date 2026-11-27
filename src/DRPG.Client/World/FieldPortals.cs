using DRPG.Client.Ui;
using DRPG.Engine.Assets;
using DRPG.Engine.Rendering;
using DRPG.Shared.Formats.Map;

namespace DRPG.Client.World;

/// <summary>
/// Renders and animates map portal swirls driven by <see cref="MapCell.Type"/> (✅ v322 <c>0x00b380d0</c>,
/// <c>0x00a159a0</c>, <c>0x00a121f0</c>):
/// <list type="bullet">
///   <item><c>1000</c> / <c>1005</c>: <c>Effect\effect_portal.spr</c>, ground layer, centred on cell, blended.</item>
///   <item><c>1001</c>: <c>Effect\effect_portal2.spr</c>, ground layer, offset <c>(-11, +19 - h)</c>, blended.</item>
///   <item><c>1002</c>: <c>Effect\effect_portal3.spr</c>, overlay layer (above entities), offset <c>(-46, +21 - h)</c>, blended.</item>
///   <item><c>1003</c>: <c>Effect\effect_portal_square.spr</c>, ground layer, centred on cell, opaque.</item>
/// </list>
/// </summary>
public sealed class FieldPortals(SpriteStore sprites)
{
    public const int TypeStandard = 1000;
    public const int TypePortal2 = 1001;
    public const int TypePortal3 = 1002;
    public const int TypeSquare = 1003;
    public const int TypeStandardAlt = 1005;

    /// <summary>✅ <c>0x00b380d0</c>: visible cell margins around the camera window.</summary>
    public const int MarginLeft = 15, MarginTop = 20, MarginRight = 30, MarginBottom = 40;

    /// <summary>✅ <c>0x00a121f0</c>: alpha 16/32 (0x10) passed to <c>0x00b569f0</c>.</summary>
    public const int PortalBlendAlpha = 16;

    private IReadOnlyList<ITexture>? _portal1;
    private IReadOnlyList<ITexture>? _portal2;
    private IReadOnlyList<ITexture>? _portal3;
    private IReadOnlyList<ITexture>? _portalSquare;
    private bool _tried1, _tried2, _tried3, _triedSquare;

    private int _portalTick;
    private int _portalFrame;
    private int _squareTick;
    private int _squareFrame;

    internal int PortalFrame => _portalFrame;
    internal int SquareFrame => _squareFrame;

    /// <summary>
    /// ✅ <c>0x00a121f0</c> lines 568-591: advances portal animation frames (called once per 30 FPS logic step).
    /// Standard portals advance every 2 logic steps (<c>DAT_0135acd4 == 3</c> across two calls/frame);
    /// square portals advance every 4 logic steps (<c>DAT_0135ace0 == 8</c> across two calls/frame).
    /// </summary>
    public void Step()
    {
        if (++_portalTick >= 2)
        {
            _portalTick = 0;
            _portalFrame++;
            if (_portal1 is { Count: > 0 } p1 && _portalFrame >= p1.Count)
                _portalFrame = 0;
        }
        if (++_squareTick >= 4)
        {
            _squareTick = 0;
            _squareFrame++;
            if (_portalSquare is { Count: > 0 } ps && _squareFrame >= ps.Count)
                _squareFrame = 0;
        }
    }

    /// <summary>✅ <c>0x00b380d0</c> + <c>0x00a121f0(0)</c>: draws ground-layer portals (types 1000, 1001, 1003, 1005) before entities.</summary>
    public void DrawGround(IRenderer renderer, FieldMap field, int cameraX, int cameraY, SpriteOptions options)
    {
        var (firstX, lastX, firstY, lastY) = VisibleBounds(field.Map, cameraX, cameraY);
        for (int x = firstX; x <= lastX; x++)
        {
            int screenX = x * MapFile.CellWidth - cameraX;
            for (int y = firstY; y <= lastY; y++)
            {
                int type = field.Map[x, y].Type;
                if (type is not (TypeStandard or TypeStandardAlt or TypePortal2 or TypeSquare))
                    continue;
                int screenY = y * MapFile.CellHeight - cameraY;
                switch (type)
                {
                    case TypeStandard:
                    case TypeStandardAlt:
                        if (FrameOf(ref _portal1, ref _tried1, "Effect\\effect_portal.spr", _portalFrame) is { } tex1)
                            options.DrawBlended(renderer, tex1,
                                screenX + MapFile.CellWidth / 2 - tex1.Width / 2,
                                screenY + MapFile.CellHeight / 2 - tex1.Height / 2,
                                PortalBlendAlpha);
                        break;
                    case TypePortal2:
                        if (FrameOf(ref _portal2, ref _tried2, "Effect\\effect_portal2.spr", _portalFrame) is { } tex2)
                            options.DrawBlended(renderer, tex2,
                                screenX - 11,
                                screenY + 19 - tex2.Height,
                                PortalBlendAlpha);
                        break;
                    case TypeSquare:
                        if (FrameOf(ref _portalSquare, ref _triedSquare, "Effect\\effect_portal_square.spr", _squareFrame) is { } texSq)
                            renderer.Draw(texSq,
                                screenX + MapFile.CellWidth / 2 - texSq.Width / 2,
                                screenY + MapFile.CellHeight / 2 - texSq.Height / 2);
                        break;
                }
            }
        }
    }

    /// <summary>✅ <c>0x00b380d0</c> + <c>0x00a121f0(1)</c>: draws overlay-layer portals (type 1002) after the Y-sorted entity queue.</summary>
    public void DrawOverlay(IRenderer renderer, FieldMap field, int cameraX, int cameraY, SpriteOptions options)
    {
        var (firstX, lastX, firstY, lastY) = VisibleBounds(field.Map, cameraX, cameraY);
        for (int x = firstX; x <= lastX; x++)
        {
            int screenX = x * MapFile.CellWidth - cameraX;
            for (int y = firstY; y <= lastY; y++)
            {
                if (field.Map[x, y].Type != TypePortal3)
                    continue;
                int screenY = y * MapFile.CellHeight - cameraY;
                if (FrameOf(ref _portal3, ref _tried3, "Effect\\effect_portal3.spr", _portalFrame) is { } tex3)
                    options.DrawBlended(renderer, tex3,
                        screenX - 46,
                        screenY + 21 - tex3.Height,
                        PortalBlendAlpha);
            }
        }
    }

    private static (int FirstX, int LastX, int FirstY, int LastY) VisibleBounds(MapFile map, int cameraX, int cameraY)
    {
        int cellX = cameraX / MapFile.CellWidth;
        int cellY = cameraY / MapFile.CellHeight;
        return (
            Math.Max(0, cellX - MarginLeft),
            Math.Min(map.Width - 1, cellX + FieldMap.ViewCellsX + MarginRight),
            Math.Max(0, cellY - MarginTop),
            Math.Min(map.Height - 1, cellY + FieldMap.ViewCellsY + MarginBottom));
    }

    private ITexture? FrameOf(ref IReadOnlyList<ITexture>? cache, ref bool tried, string path, int frame)
    {
        if (!tried)
        {
            tried = true;
            try
            {
                cache = sprites.LoadLazy(path);
            }
            catch (Exception)
            {
                cache = null;
            }
        }
        if (cache is not { Count: > 0 })
            return null;
        return cache[frame % cache.Count];
    }
}
