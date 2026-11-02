using System.IO.Compression;
using System.Text.Json;

namespace LifeVerse.Shared.Terrain;

public enum TerrainTool : byte { Raise, Lower, Flatten, Smooth, Paint, Noise }

/// <summary>
/// Engine-independent island heightmap plus four texture layers. The island server owns the
/// authoritative copy; clients only replay edits the server accepted. The default is flat ground
/// covered by layer 0, like a freshly created region.
/// </summary>
public sealed class TerrainMap
{
    public const int DefaultCells = 128;
    public const float DefaultCellSize = 2f;
    public const int LayerCount = 4;
    public const float MinHeight = -8f;
    public const float MaxHeight = 48f;
    public const float MaxBrushRadius = 24f;
    /// <summary>Water surface height; flat land (0) stands just above it and the shore slopes down into the sea.</summary>
    public const float SeaLevel = -.8f;
    /// <summary>Width of the beach that slopes from the flat interior down to the sea floor at the island edge.</summary>
    public const float ShoreWidth = 24f;
    /// <summary>Open ocean around the island that avatars can swim in before reaching the world limit.</summary>
    public const float OceanMargin = 192f;
    private const int MinUnits = -8000; // MinHeight in millimetres
    private const int MaxCells = 256;
    private const uint Magic = 0x3154564C; // "LVT1"

    private readonly float[] _heights;
    private readonly byte[] _splat;

    public int Cells { get; }
    public float CellSize { get; }
    public int VertexCount => Cells + 1;
    public float HalfExtent => Cells * CellSize / 2f;
    public long Revision { get; set; }
    public IReadOnlyList<float> Heights => _heights;
    public IReadOnlyList<byte> Splat => _splat;

    public TerrainMap(int cells = DefaultCells, float cellSize = DefaultCellSize)
    {
        Cells = Math.Clamp(cells, 4, MaxCells);
        CellSize = Math.Clamp(cellSize, .25f, 16f);
        _heights = new float[VertexCount * VertexCount];
        _splat = new byte[VertexCount * VertexCount * LayerCount];
        // Flat grassy interior with a sandy beach sloping to the sea floor along the edge.
        for (var z = 0; z <= Cells; z++)
        for (var x = 0; x <= Cells; x++)
        {
            var i = z * VertexCount + x;
            var edge = Math.Min(Math.Min(x, z), Math.Min(Cells - x, Cells - z)) * CellSize;
            _heights[i] = MathF.Round(MinHeight * (1f - SmoothStep(0f, ShoreWidth, edge)) * 1000f) / 1000f;
            var sand = 1f - SmoothStep(ShoreWidth * .25f, ShoreWidth * .6f, edge);
            var sandWeight = (byte)MathF.Round(sand * 255f);
            _splat[i * LayerCount] = (byte)(255 - sandWeight);
            _splat[i * LayerCount + 2] = sandWeight;
        }
    }

    public bool IsBorder(int x, int z) => x == 0 || z == 0 || x == Cells || z == Cells;
    public static float WaterDepth(float groundHeight) => SeaLevel - groundHeight;

    public float HeightAt(int x, int z) => _heights[Math.Clamp(z, 0, Cells) * VertexCount + Math.Clamp(x, 0, Cells)];

    /// <summary>Bilinear ground height at a world position; positions outside the island clamp to its edge.</summary>
    public float SampleHeight(float worldX, float worldZ)
    {
        var gx = Math.Clamp((worldX + HalfExtent) / CellSize, 0f, Cells);
        var gz = Math.Clamp((worldZ + HalfExtent) / CellSize, 0f, Cells);
        var x0 = Math.Min((int)gx, Cells - 1);
        var z0 = Math.Min((int)gz, Cells - 1);
        var fx = gx - x0;
        var fz = gz - z0;
        var h00 = HeightAt(x0, z0);
        var h10 = HeightAt(x0 + 1, z0);
        var h01 = HeightAt(x0, z0 + 1);
        var h11 = HeightAt(x0 + 1, z0 + 1);
        return (h00 * (1 - fx) + h10 * fx) * (1 - fz) + (h01 * (1 - fx) + h11 * fx) * fz;
    }

    public byte SplatAt(int x, int z, int layer) => _splat[(Math.Clamp(z, 0, Cells) * VertexCount + Math.Clamp(x, 0, Cells)) * LayerCount + Math.Clamp(layer, 0, LayerCount - 1)];

    /// <summary>
    /// Applies one brush dab at a world position. <paramref name="strength"/> is metres for raise/lower/noise
    /// and a 0..1 blend for flatten/smooth/paint. Heights are kept on a 1 mm grid so replays stay identical.
    /// </summary>
    public bool Apply(TerrainTool tool, float worldX, float worldZ, float radius, float strength, int layer = 0)
    {
        if (!float.IsFinite(worldX) || !float.IsFinite(worldZ) || !float.IsFinite(radius) || !float.IsFinite(strength)) return false;
        radius = Math.Clamp(radius, CellSize / 2f, MaxBrushRadius);
        strength = Math.Clamp(strength, 0f, tool is TerrainTool.Raise or TerrainTool.Lower or TerrainTool.Noise ? 2f : 1f);
        layer = Math.Clamp(layer, 0, LayerCount - 1);
        var cx = (worldX + HalfExtent) / CellSize;
        var cz = (worldZ + HalfExtent) / CellSize;
        var span = radius / CellSize;
        var target = tool == TerrainTool.Flatten ? SampleHeight(worldX, worldZ) : 0f;
        var source = tool == TerrainTool.Smooth ? (float[])_heights.Clone() : _heights;
        var changed = false;
        for (var z = Math.Max(0, (int)MathF.Floor(cz - span)); z <= Math.Min(Cells, (int)MathF.Ceiling(cz + span)); z++)
        for (var x = Math.Max(0, (int)MathF.Floor(cx - span)); x <= Math.Min(Cells, (int)MathF.Ceiling(cx + span)); x++)
        {
            var distance = MathF.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) * CellSize;
            if (distance > radius) continue;
            var t = 1f - distance / radius;
            var weight = t * t * (3f - 2f * t);
            // The outermost ring is the sea floor that continues out to the world limit; it never changes.
            if (IsBorder(x, z)) continue;
            var index = z * VertexCount + x;
            switch (tool)
            {
                case TerrainTool.Raise: changed |= SetHeight(index, _heights[index] + strength * weight); break;
                case TerrainTool.Lower: changed |= SetHeight(index, _heights[index] - strength * weight); break;
                case TerrainTool.Noise: changed |= SetHeight(index, _heights[index] + (Hash(x, z, Revision) - .5f) * 2f * strength * weight); break;
                case TerrainTool.Flatten: changed |= SetHeight(index, Lerp(_heights[index], target, strength * weight)); break;
                case TerrainTool.Smooth: changed |= SetHeight(index, Lerp(source[index], NeighbourAverage(source, x, z), strength * weight)); break;
                case TerrainTool.Paint: changed |= PaintVertex(index, layer, strength * weight); break;
                default: return false;
            }
        }
        if (changed) Revision++;
        return changed;
    }

    public byte[] Serialize()
    {
        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, System.Text.Encoding.UTF8, true))
        {
            writer.Write(Magic); writer.Write(Cells); writer.Write(CellSize); writer.Write(Revision);
            // Heights are always whole millimetres (see SetHeight), so storing the integer count is lossless.
            foreach (var height in _heights) writer.Write((ushort)Math.Clamp((int)MathF.Round(height * 1000f) - MinUnits, 0, ushort.MaxValue));
            writer.Write(_splat);
        }
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, true)) raw.WriteTo(deflate);
        return packed.ToArray();
    }

    public static bool TryDeserialize(byte[] data, out TerrainMap map)
    {
        map = new TerrainMap();
        if (data.Length is < 16 or > 8 * 1024 * 1024) return false;
        try
        {
            using var deflate = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress);
            var header = new byte[20];
            deflate.ReadExactly(header);
            if (BitConverter.ToUInt32(header, 0) != Magic) return false;
            var cells = BitConverter.ToInt32(header, 4);
            var cellSize = BitConverter.ToSingle(header, 8);
            if (cells is < 4 or > MaxCells || !float.IsFinite(cellSize) || cellSize is < .25f or > 16f) return false;
            var loaded = new TerrainMap(cells, cellSize) { Revision = BitConverter.ToInt64(header, 12) };
            var heights = new byte[loaded._heights.Length * 2];
            deflate.ReadExactly(heights);
            for (var i = 0; i < loaded._heights.Length; i++) loaded._heights[i] = (BitConverter.ToUInt16(heights, i * 2) + MinUnits) / 1000f;
            for (var z = 0; z <= cells; z++)
            for (var x = 0; x <= cells; x++)
                if (loaded.IsBorder(x, z)) loaded._heights[z * loaded.VertexCount + x] = MinHeight; // maps saved before the ocean existed
            deflate.ReadExactly(loaded._splat);
            if (deflate.ReadByte() != -1) return false;
            map = loaded;
            return true;
        }
        catch (Exception exception) when (exception is EndOfStreamException or InvalidDataException or IOException) { return false; }
    }

    public string ToJson() => JsonSerializer.Serialize(new { v = 1, data = Convert.ToBase64String(Serialize()) });

    /// <summary>Parses a persisted snapshot. Empty or legacy snapshots yield the default flat island.</summary>
    public static TerrainMap FromJson(string? json)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.GetString() is { Length: > 0 } text && TryDeserialize(Convert.FromBase64String(text), out var map)) return map;
            }
            catch (Exception exception) when (exception is JsonException or FormatException) { }
        }
        return new TerrainMap();
    }

    private bool SetHeight(int index, float value)
    {
        value = MathF.Round(Math.Clamp(value, MinHeight, MaxHeight) * 1000f) / 1000f;
        if (value == _heights[index]) return false;
        _heights[index] = value;
        return true;
    }

    private bool PaintVertex(int index, int layer, float amount)
    {
        var offset = index * LayerCount;
        var weights = new float[LayerCount];
        for (var i = 0; i < LayerCount; i++) weights[i] = _splat[offset + i];
        var target = Math.Min(255f, weights[layer] + amount * 255f);
        var others = 255f - weights[layer];
        var remaining = 255f - target;
        for (var i = 0; i < LayerCount; i++)
            if (i != layer) weights[i] = others > 0 ? weights[i] * remaining / others : 0;
        weights[layer] = target;
        var result = new byte[LayerCount];
        var sum = 0;
        for (var i = 0; i < LayerCount; i++) { result[i] = (byte)MathF.Round(weights[i]); sum += result[i]; }
        result[layer] = (byte)Math.Clamp(result[layer] + (255 - sum), 0, 255);
        var changed = false;
        for (var i = 0; i < LayerCount; i++) if (_splat[offset + i] != result[i]) { _splat[offset + i] = result[i]; changed = true; }
        return changed;
    }

    private float NeighbourAverage(float[] heights, int x, int z)
    {
        var sum = 0f; var count = 0;
        for (var oz = -1; oz <= 1; oz++)
        for (var ox = -1; ox <= 1; ox++)
        {
            var nx = x + ox; var nz = z + oz;
            if (nx < 0 || nz < 0 || nx > Cells || nz > Cells) continue;
            sum += heights[nz * VertexCount + nx]; count++;
        }
        return sum / count;
    }

    private static float Hash(int x, int z, long seed)
    {
        var h = (uint)(x * 374761393 + z * 668265263 + seed * 2246822519L);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
    }

    private static float Lerp(float from, float to, float amount) => from + (to - from) * amount;

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
