using LifeVerse.Shared.Terrain;

namespace LifeVerse.Api.Modules.Map;

/// <summary>An object on the island as the map draws it: where it stands and how big it is, seen from above.</summary>
public readonly record struct Footprint(float X, float Z, float SizeX, float SizeZ, float RotationY);

/// <summary>
/// Draws an island from above: water by depth, land by the paint of the ground, a little relief from the slope, and the footprints of what is built on it. It reads only the saved terrain and the objects, so it
/// needs no 3D and can run anywhere the API runs. The colours are the ones the mini-map of the game already uses.
/// </summary>
public static class MapRenderer
{
    private static readonly (float R, float G, float B)[] Ground =
    [(0x5a, 0x9a, 0x47), (0x8a, 0x6a, 0x48), (0xd8, 0xc9, 0x95), (0x8d, 0x8d, 0x90), (0x9c, 0x98, 0x90), (0xf2, 0xf6, 0xf9), (0x4a, 0x36, 0x28), (0xa3, 0x9b, 0x52)];
    private static readonly (float R, float G, float B) Shallow = (0x58, 0xa8, 0xd4), Deep = (0x13, 0x3f, 0x73), Building = (0xe8, 0xd7, 0xa0), BuildingEdge = (0x6d, 0x81, 0x8d);

    private static float Mix(float a, float b, float t) => a + (b - a) * t;

    /// <summary>The colour of a point of the terrain: <paramref name="x"/>, <paramref name="z"/> in vertices (they may fall between two).</summary>
    public static (byte R, byte G, byte B) Terrain(TerrainMap map, float x, float z)
    {
        var x0 = Math.Clamp((int)MathF.Floor(x), 0, map.Cells); var z0 = Math.Clamp((int)MathF.Floor(z), 0, map.Cells); var x1 = Math.Min(x0 + 1, map.Cells); var z1 = Math.Min(z0 + 1, map.Cells);
        var fx = Math.Clamp(x - x0, 0f, 1f); var fz = Math.Clamp(z - z0, 0f, 1f);
        float Height(int ix, int iz) => map.HeightAt(ix, iz);
        var height = Mix(Mix(Height(x0, z0), Height(x1, z0), fx), Mix(Height(x0, z1), Height(x1, z1), fx), fz);
        var depth = map.SeaLevel - height;
        if (depth > 0) { var t = Math.Clamp(depth / 6f, 0f, 1f); return ((byte)Mix(Shallow.R, Deep.R, t), (byte)Mix(Shallow.G, Deep.G, t), (byte)Mix(Shallow.B, Deep.B, t)); }
        float r = 0, g = 0, b = 0;
        for (var layer = 0; layer < TerrainMap.LayerCount; layer++)
        {
            var w = (Mix(map.SplatAt(x0, z0, layer), map.SplatAt(x1, z0, layer), fx) * (1 - fz) + Mix(map.SplatAt(x0, z1, layer), map.SplatAt(x1, z1, layer), fx) * fz) / 255f;
            r += Ground[layer].R * w; g += Ground[layer].G * w; b += Ground[layer].B * w;
        }
        var lift = Math.Clamp(height * .03f, 0f, .35f); r = Mix(r, 255, lift); g = Mix(g, 255, lift); b = Mix(b, 255, lift);
        var slope = (Height(Math.Min(x0 + 1, map.Cells), z0) - Height(Math.Max(x0 - 1, 0), z0)) + (Height(x0, Math.Min(z0 + 1, map.Cells)) - Height(x0, Math.Max(z0 - 1, 0))); // light from the north-west
        var shade = Math.Clamp(1f - slope * .045f, .78f, 1.18f);
        return ((byte)Math.Clamp(r * shade, 0, 255), (byte)Math.Clamp(g * shade, 0, 255), (byte)Math.Clamp(b * shade, 0, 255));
    }

    /// <summary>The island as a <paramref name="size"/> × <paramref name="size"/> picture (RGB bytes), north up. At most <see cref="MaxFootprints"/> objects are drawn.</summary>
    public static byte[] Render(TerrainMap map, IReadOnlyList<Footprint> objects, int size)
    {
        var pixels = new byte[size * size * 3]; var half = map.HalfExtent; var extent = half * 2f;
        for (var py = 0; py < size; py++)
            for (var px = 0; px < size; px++)
            {
                var (r, g, b) = Terrain(map, (px + .5f) / size * map.Cells, (py + .5f) / size * map.Cells); var at = (py * size + px) * 3; pixels[at] = r; pixels[at + 1] = g; pixels[at + 2] = b;
            }
        foreach (var o in objects.Take(MaxFootprints)) Stamp(pixels, size, (o.X + half) / extent * size, (o.Z + half) / extent * size, o, size / extent);
        return pixels;
    }

    public const int MaxFootprints = 6000;

    private static void Stamp(byte[] pixels, int size, float cx, float cz, Footprint o, float scale)
    {
        var hx = Math.Max(.7f, Math.Abs(o.SizeX) * .5f * scale); var hz = Math.Max(.7f, Math.Abs(o.SizeZ) * .5f * scale); var cos = MathF.Cos(o.RotationY * MathF.PI / 180f); var sin = MathF.Sin(o.RotationY * MathF.PI / 180f);
        var reach = (int)MathF.Ceiling(MathF.Abs(hx * cos) + MathF.Abs(hz * sin)); var reachZ = (int)MathF.Ceiling(MathF.Abs(hx * sin) + MathF.Abs(hz * cos));
        for (var y = (int)cz - reachZ - 1; y <= (int)cz + reachZ + 1; y++)
            for (var x = (int)cx - reach - 1; x <= (int)cx + reach + 1; x++)
            {
                if (x < 0 || y < 0 || x >= size || y >= size) continue;
                var dx = x + .5f - cx; var dz = y + .5f - cz; var lx = dx * cos - dz * sin; var lz = dx * sin + dz * cos; // into the frame of the object
                if (MathF.Abs(lx) > hx || MathF.Abs(lz) > hz) continue;
                var edge = MathF.Abs(lx) > hx - 1f || MathF.Abs(lz) > hz - 1f; var color = edge && hx > 2.5f && hz > 2.5f ? BuildingEdge : Building; var at = (y * size + x) * 3;
                pixels[at] = (byte)color.R; pixels[at + 1] = (byte)color.G; pixels[at + 2] = (byte)color.B;
            }
    }
}
