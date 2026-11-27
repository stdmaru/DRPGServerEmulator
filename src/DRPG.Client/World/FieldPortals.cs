using DRPG.Client.Ui;
using DRPG.Engine.Assets;
using DRPG.Engine.Rendering;
using DRPG.Shared.Formats.Map;

namespace DRPG.Client.World;

/// <summary>
/// Renders and animates map portal swirls driven by <see cref="MapCell.Type"/> (✅ v322 <c>0x00b380d0</c>,
/// <c>0x00a159a0</c>, <c>0x00a121f0</c>, <c>0x00876a30</c> mode 5 additive) and the <c>0xa8</c> warp-out effect
/// (<c>Effect\effect_map.spr</c>, ✅ <c>0x00afe310</c> → <c>0x00a1b670(0xa8)</c> → <c>0x00a4a9d7</c> / <c>0x00a46e0a</c> → <c>0x00aba410</c>):
/// <list type="bullet">
///   <item><c>1000</c> / <c>1005</c>: <c>Effect\effect_portal.spr</c>, ground layer, centred on cell, mode 5 additive.</item>
///   <item><c>1001</c>: <c>Effect\effect_portal2.spr</c>, ground layer, offset <c>(-11, +19 - h)</c>, mode 5 additive.</item>
///   <item><c>1002</c>: <c>Effect\effect_portal3.spr</c>, overlay layer (above entities), offset <c>(-46, +21 - h)</c>, mode 5 additive.</item>
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

    /// <summary>✅ <c>0x00b4e550</c> line 605: cells with <c>1 &lt;= type &lt;= 8999</c> show <c>cursor_door.spr</c> (case 10).</summary>
    public const int MinPortalCursorType = 1, MaxPortalCursorType = 8999;

    /// <summary>✅ <c>0x00b380d0</c>: visible cell margins around the camera window.</summary>
    public const int MarginLeft = 15, MarginTop = 20, MarginRight = 30, MarginBottom = 40;

    /// <summary>✅ <c>0x00a4aa92</c>: vertical offset (+100 px) of the <c>0xa8</c> warp effect (<c>Effect\effect_map.spr</c>).</summary>
    public const int WarpOffsetY = 0x64;

    /// <summary>✅ <c>0x00a1be69</c> + <c>0x00a4ab1c</c>: delay = 1 (each frame of <c>effect_map.spr</c> stays for 2 logic steps).</summary>
    public const int WarpFrameDelay = 1;

    private IReadOnlyList<ITexture>? _portal1;
    private IReadOnlyList<ITexture>? _portal2;
    private IReadOnlyList<ITexture>? _portal3;
    private IReadOnlyList<ITexture>? _portalSquare;
    private IReadOnlyList<ITexture>? _warpEffect;
    private bool _tried1, _tried2, _tried3, _triedSquare, _triedWarp;

    private int _portalTick;
    private int _portalFrame;
    private int _squareTick;
    private int _squareFrame;

    private bool _warpPlaying;
    private int _warpFrame;
    private int _warpDelay;

    internal int PortalFrame => _portalFrame;
    internal int SquareFrame => _squareFrame;
    internal bool IsWarpPlaying => _warpPlaying;
    internal int WarpFrame => _warpFrame;

    /// <summary>✅ <c>0x00b4e550</c> line 605: true when a cell type switches the cursor to <c>Interface\cursor_door.spr</c>.</summary>
    public static bool IsPortalCursorType(int cellType) => cellType is >= MinPortalCursorType and <= MaxPortalCursorType;

    /// <summary>
    /// ✅ <c>0x00a121f0</c> lines 568-591: advances portal swirl frames (called once per 30 FPS logic step).
    /// Standard portals advance every 2 logic steps; square portals advance every 4 logic steps.
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

    /// <summary>✅ <c>0x00afe310</c> line 51 → <c>0x00a1b670(0xa8)</c>: starts the warp-out animation (<c>Effect\effect_map.spr</c>).</summary>
    public void StartWarpEffect()
    {
        EnsureLoaded(ref _warpEffect, ref _triedWarp, "Effect\\effect_map.spr");
        _warpPlaying = true;
        _warpFrame = 0;
        _warpDelay = 0;
    }

    /// <summary>
    /// ✅ <c>0x00a4a9d7</c> → <c>0x00a4ab1c</c> → <c>0x00a4ab91</c>: advances the <c>0xa8</c> warp animation by one logic step.
    /// Returns true on the step it finishes, signalling the scene to call <c>0x00aba410</c> (map transition loading).
    /// </summary>
    public bool StepWarpEffect()
    {
        if (!_warpPlaying)
            return false;
        int frameCount = _warpEffect?.Count ?? 0;
        if (frameCount == 0)
        {
            _warpPlaying = false;
            return true;
        }
        if (_warpDelay >= WarpFrameDelay)
        {
            _warpFrame++;
            _warpDelay = 0;
        }
        else
        {
            _warpDelay++;
        }
        if (_warpFrame >= frameCount)
        {
            _warpPlaying = false;
            return true;
        }
        return false;
    }

    /// <summary>Cancels any active warp effect (e.g. on disconnect).</summary>
    public void ResetWarpEffect()
    {
        _warpPlaying = false;
        _warpFrame = 0;
        _warpDelay = 0;
    }

    /// <summary>
    /// ✅ <c>0x00a46e0a</c> + <c>0x00a4a9d7</c>: draws the <c>0xa8</c> warp effect (<c>Effect\effect_map.spr</c>)
    /// centred on the tamer at <c>(worldX - cameraX - w/2, worldY - cameraY - h + 100)</c> in mode 5 (additive).
    /// </summary>
    public void DrawWarpEffect(IRenderer renderer, int tamerWorldX, int tamerWorldY, int cameraX, int cameraY, SpriteOptions options)
    {
        if (!_warpPlaying || _warpEffect is not { Count: > 0 } frames)
            return;
        int idx = Math.Clamp(_warpFrame, 0, frames.Count - 1);
        var tex = frames[idx];
        int drawX = tamerWorldX - cameraX - tex.Width / 2;
        int drawY = tamerWorldY - cameraY - tex.Height + WarpOffsetY;
        DrawMode5(renderer, tex, drawX, drawY, options);
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
                            DrawMode5(renderer, tex1,
                                screenX + MapFile.CellWidth / 2 - tex1.Width / 2,
                                screenY + MapFile.CellHeight / 2 - tex1.Height / 2,
                                options);
                        break;
                    case TypePortal2:
                        if (FrameOf(ref _portal2, ref _tried2, "Effect\\effect_portal2.spr", _portalFrame) is { } tex2)
                            DrawMode5(renderer, tex2,
                                screenX - 11,
                                screenY + 19 - tex2.Height,
                                options);
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
                    DrawMode5(renderer, tex3,
                        screenX - 46,
                        screenY + 21 - tex3.Height,
                        options);
            }
        }
    }

    /// <summary>✅ <c>0x00b569f0(..., mode=5, ...)</c> → <c>0x00876a30</c> case 5: additive when <c>alpha</c> option is on, plain otherwise.</summary>
    private static void DrawMode5(IRenderer renderer, ITexture texture, int x, int y, SpriteOptions options)
    {
        if (options.Alpha)
            renderer.DrawAdditive(texture, x, y);
        else
            renderer.Draw(texture, x, y);
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
        EnsureLoaded(ref cache, ref tried, path);
        if (cache is not { Count: > 0 })
            return null;
        return cache[frame % cache.Count];
    }

    private void EnsureLoaded(ref IReadOnlyList<ITexture>? cache, ref bool tried, string path)
    {
        if (tried)
            return;
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
}
