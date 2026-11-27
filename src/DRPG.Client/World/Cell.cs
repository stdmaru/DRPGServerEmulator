using DRPG.Shared.Formats.Map;

namespace DRPG.Client.World;

/// <summary>A cell of the field map (64×32 px, <see cref="MapFile.CellWidth"/> × <see cref="MapFile.CellHeight"/>).</summary>
public readonly record struct Cell(int X, int Y)
{
    /// <summary>✅ <c>0x0085e7b0</c>: the centre of the cell in map pixels, <c>x · 64 + 32</c>.</summary>
    public int CenterX => X * MapFile.CellWidth + MapFile.CellWidth / 2;

    /// <summary>✅ <c>0x0085e7b0</c>: the centre of the cell in map pixels, <c>y · 32 + 16</c>.</summary>
    public int CenterY => Y * MapFile.CellHeight + MapFile.CellHeight / 2;

    /// <summary>✅ <c>0x0085e940</c> / <c>0x00b36c20</c>: the cell that holds the map pixel (<paramref name="worldX"/>, <paramref name="worldY"/>).</summary>
    public static Cell AtPixel(int worldX, int worldY) => new(worldX / MapFile.CellWidth, worldY / MapFile.CellHeight);

    /// <summary>
    /// Chebyshev distance (<c>max(|dx|, |dy|)</c>): X and Y advance together on a diagonal, so this is the number of steps
    /// (the per-axis checks of <c>0x008671c0</c> and <c>0x00859340</c>).
    /// </summary>
    public int DistanceTo(Cell other) => Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

    /// <summary>The same cell with negative coordinates raised to 0 (✅ <c>0x0085e890</c> / <c>0x008656b0</c> never keep a negative cell).</summary>
    public Cell NotNegative() => new(Math.Max(0, X), Math.Max(0, Y));

    public override string ToString() => $"({X}, {Y})";
}
