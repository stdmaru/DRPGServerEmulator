using DRPG.Engine.Rendering;
using DRPG.Shared.Formats.Map;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Formats.Sprite;

namespace DRPG.Client.World;

/// <summary>One entry of the field's draw queue (✅ <c>0x00a0d340</c>): sorted by y, then by the order of entry.</summary>
public readonly record struct DrawItem(int Y, ITexture? Texture, int X, int DrawY, Action<IRenderer, int, int>? Custom = null);

/// <summary>
/// A loaded map and what it draws (✅ docs/cliente/mapas.md#desenho-campo-): the ground of the visible cells straight from the
/// <c>.til</c> frames, and the objects anchored in cells around the camera, put in the queue that the tamer and the digimon share.
/// The tilesets and the objects are read when a cell first needs them and their frames uploaded when first drawn; a file the pack
/// lacks or cannot parse is logged once and drawn as nothing (✅ the original warns and goes on).
/// </summary>
public sealed class FieldMap : IDisposable
{
    /// <summary>✅ <c>[+0x511c90/94]</c>: the visible window is 800 / 64 + 2 by 600 / 32 + 2 cells.</summary>
    public const int ViewCellsX = 800 / MapFile.CellWidth + 2, ViewCellsY = 600 / MapFile.CellHeight + 2;

    /// <summary>✅ <c>0x00b36630</c>: the objects are looked for 10 cells to the left, 15 above, 20 to the right and 30 below the window.</summary>
    public const int ObjectMarginLeft = 10, ObjectMarginTop = 15, ObjectMarginRight = 20, ObjectMarginBottom = 30;

    private readonly PackArchive _pack;
    private readonly IRenderer _renderer;
    private readonly Action<string> _log;
    private readonly TileSetFile?[] _tileSets;
    private readonly bool[] _tileSetTried;
    private readonly ITexture?[][] _tiles;
    private readonly bool[][] _tileTried;
    private readonly MapObjectFile?[] _objects;
    private readonly bool[] _objectTried;
    private readonly ITexture?[] _objectTextures;

    public FieldMap(MapFile map, PackArchive pack, IRenderer renderer, Action<string> log)
    {
        Map = map;
        _pack = pack;
        _renderer = renderer;
        _log = log;
        _tileSets = new TileSetFile?[map.TileNames.Count];
        _tileSetTried = new bool[map.TileNames.Count];
        _tiles = new ITexture?[map.TileNames.Count][];
        _tileTried = new bool[map.TileNames.Count][];
        _objects = new MapObjectFile?[map.ObjectNames.Count];
        _objectTried = new bool[map.ObjectNames.Count];
        _objectTextures = new ITexture?[map.ObjectNames.Count];
    }

    public MapFile Map { get; }

    /// <summary>The size of the map in pixels (✅ <c>width · 64</c> × <c>height · 32</c>).</summary>
    public int PixelWidth => Map.Width * MapFile.CellWidth;

    public int PixelHeight => Map.Height * MapFile.CellHeight;

    /// <summary>
    /// ✅ <c>0x00afe310</c>: the scroll that centres (<paramref name="worldX"/>, <paramref name="worldY"/>) — <c>x − 400</c>,
    /// <c>y − 300</c> — held inside <c>0 .. width · 64 − 800</c> and <c>0 .. height · 32 − 600</c>.
    /// </summary>
    public (int X, int Y) Camera(int worldX, int worldY) => (
        Math.Clamp(worldX - IRenderer.FrameWidth / 2, 0, Math.Max(0, PixelWidth - IRenderer.FrameWidth)),
        Math.Clamp(worldY - IRenderer.FrameHeight / 2, 0, Math.Max(0, PixelHeight - IRenderer.FrameHeight)));

    /// <summary>✅ <c>0x00b36900</c>: the frame of the cell's tileset by the cell's top-left corner, for the visible cells.</summary>
    public void DrawGround(IRenderer renderer, int cameraX, int cameraY)
    {
        int firstX = Math.Max(0, cameraX / MapFile.CellWidth), firstY = Math.Max(0, cameraY / MapFile.CellHeight);
        int lastX = Math.Min(Map.Width - 1, firstX + ViewCellsX), lastY = Math.Min(Map.Height - 1, firstY + ViewCellsY);
        for (int y = firstY; y <= lastY; y++)
            for (int x = firstX; x <= lastX; x++)
            {
                var cell = Map[x, y];
                if (cell.TileSet == MapCell.None || Tile(cell.TileSet, cell.TileFrame) is not { } texture)
                    continue;
                renderer.Draw(texture, x * MapFile.CellWidth - cameraX, y * MapFile.CellHeight - cameraY);
            }
    }

    /// <summary>
    /// ✅ <c>0x00b36630</c>: puts the objects anchored in the cells around the window in <paramref name="queue"/>, with the
    /// y of the queue = the cell's y + <c>anchorY mod 32</c> (✅ <c>0x00a0cd80(7, obj, x + anchorX mod 64, y + anchorY mod 32)</c>).
    /// ❓ The image's top-left corner is the cell's minus whole cells of the anchor (<c>anchor − anchor mod</c> 64 / 32): the
    /// anchor of the file is the point of the cell that sits on the map cell.
    /// </summary>
    public void QueueObjects(List<DrawItem> queue, int cameraX, int cameraY)
    {
        int cellX = cameraX / MapFile.CellWidth, cellY = cameraY / MapFile.CellHeight;
        int firstX = Math.Max(0, cellX - ObjectMarginLeft), lastX = Math.Min(Map.Width - 1, cellX + ViewCellsX + ObjectMarginRight);
        int firstY = Math.Max(0, cellY - ObjectMarginTop), lastY = Math.Min(Map.Height - 1, cellY + ViewCellsY + ObjectMarginBottom);
        for (int y = firstY; y <= lastY; y++)
            for (int x = firstX; x <= lastX; x++)
            {
                int index = Map[x, y].Object;
                if (index == MapCell.None || Object(index) is not { } obj || ObjectTexture(index) is not { } texture)
                    continue;
                int screenX = x * MapFile.CellWidth - cameraX, screenY = y * MapFile.CellHeight - cameraY;
                int drawX = screenX - (obj.AnchorX - obj.AnchorX % MapFile.CellWidth);
                int drawY = screenY - (obj.AnchorY - obj.AnchorY % MapFile.CellHeight);
                if (drawX >= IRenderer.FrameWidth || drawY >= IRenderer.FrameHeight || drawX + texture.Width <= 0 || drawY + texture.Height <= 0)
                    continue;
                queue.Add(new DrawItem(screenY + obj.AnchorY % MapFile.CellHeight, texture, drawX, drawY));
            }
    }

    /// <summary>✅ The queue's order: by y, and by the order of entry when equal (<c>0x00a0afc0</c>, a stable sort).</summary>
    public static void Sort(List<DrawItem> queue)
    {
        var indexed = new (DrawItem Item, int Order)[queue.Count];
        for (int i = 0; i < indexed.Length; i++)
            indexed[i] = (queue[i], i);
        Array.Sort(indexed, (a, b) => a.Item.Y != b.Item.Y ? a.Item.Y.CompareTo(b.Item.Y) : a.Order.CompareTo(b.Order));
        for (int i = 0; i < indexed.Length; i++)
            queue[i] = indexed[i].Item;
    }

    public static void DrawQueue(IRenderer renderer, List<DrawItem> queue)
    {
        foreach (var item in queue)
        {
            if (item.Custom is { } custom)
                custom(renderer, item.X, item.DrawY);
            else if (item.Texture is { } texture)
                renderer.Draw(texture, item.X, item.DrawY);
        }
    }

    private ITexture? Tile(int tileSet, int frame)
    {
        if (tileSet < 0 || tileSet >= _tiles.Length)
            return null;
        var set = TileSet(tileSet);
        if (set is null || frame < 0 || frame >= set.FrameCount)
            return null;
        var textures = _tiles[tileSet] ??= new ITexture?[set.FrameCount];
        var tried = _tileTried[tileSet] ??= new bool[set.FrameCount];
        if (textures[frame] is { } known || tried[frame])
            return textures[frame];
        tried[frame] = true;
        try
        {
            var info = set.Sprite.Frames[frame];
            if (info.Width > 0 && info.Height > 0)
                textures[frame] = _renderer.CreateTexture(info.Width, info.Height, set.DecodeFrame(frame));
        }
        catch (SpriteFormatException e)
        {
            _log($"map\\{Map.TileNames[tileSet]} frame {frame}: {e.Message}");
        }
        return textures[frame];
    }

    private TileSetFile? TileSet(int index)
    {
        if (_tileSetTried[index])
            return _tileSets[index];
        _tileSetTried[index] = true;
        _tileSets[index] = Read(Map.TileNames[index], "LoadTile Error", data => TileSetFile.Parse(data));
        return _tileSets[index];
    }

    private MapObjectFile? Object(int index)
    {
        if (_objectTried[index])
            return _objects[index];
        _objectTried[index] = true;
        _objects[index] = Read(Map.ObjectNames[index], "LoadObject Error", data => MapObjectFile.Parse(data));
        return _objects[index];
    }

    private ITexture? ObjectTexture(int index)
    {
        if (_objectTextures[index] is { } known || Object(index) is not { } obj)
            return _objectTextures[index];
        try
        {
            if (obj.Width > 0 && obj.Height > 0)
                _objectTextures[index] = _renderer.CreateTexture(obj.Width, obj.Height, obj.DecodeImage());
        }
        catch (SpriteFormatException e)
        {
            _log($"map\\{Map.ObjectNames[index]}: {e.Message}");
            _objects[index] = null;
        }
        return _objectTextures[index];
    }

    /// <summary>✅ <c>Map\%s</c> in the pack; the errors of the original are <c>LoadTile Error : %s</c> and <c>LoadObject Error : %s</c>.</summary>
    private T? Read<T>(string name, string error, Func<byte[], T> parse) where T : class
    {
        var path = $"map\\{name}";
        try
        {
            if (_pack.Contains(path))
                return parse(_pack.ReadEntry(path));
            _log($"{error} : {path} (not in the pack)");
        }
        catch (Exception e) when (e is MapFormatException or PackFormatException or InvalidDataException or IOException)
        {
            _log($"{error} : {path}: {e.Message}");
        }
        return null;
    }

    public void Dispose()
    {
        foreach (var textures in _tiles)
            if (textures is not null)
                foreach (var texture in textures)
                    texture?.Dispose();
        foreach (var texture in _objectTextures)
            texture?.Dispose();
    }
}
