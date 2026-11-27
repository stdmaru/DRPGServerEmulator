namespace DRPG.Engine.Rendering;

/// <summary>Binary PPM: no image library needed, every viewer and Pillow read it.</summary>
public static class Screenshot
{
    public static void WritePpm(string path, uint[] rgba, int width, int height)
    {
        using var f = File.Create(path);
        f.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
        var row = new byte[width * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                uint p = rgba[y * width + x];
                row[x * 3] = (byte)p;
                row[x * 3 + 1] = (byte)(p >> 8);
                row[x * 3 + 2] = (byte)(p >> 16);
            }
            f.Write(row);
        }
    }
}
