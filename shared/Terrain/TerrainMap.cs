using System.IO.Compression;
using System.Text.Json;

namespace LifeVerse.Shared.Terrain;

/// <summary>Raise, Lower, Flatten (to the height under the brush), Smooth, Paint, Noise (roughness); then Level (to a chosen height), Restore (back toward the reference ground) and Erode.</summary>
public enum TerrainTool : byte { Raise, Lower, Flatten, Smooth, Paint, Noise, Level, Restore, Erode }

/// <summary>
/// Engine-independent island heightmap plus eight texture layers. The island server owns the
/// authoritative copy; clients only replay edits the server accepted. The default is flat ground
/// covered by layer 0, like a freshly created region.
/// </summary>
public sealed class TerrainMap
{
    public const int DefaultCells = 128;
    public const float DefaultCellSize = 2f;
    public const int LayerCount = 8;
    public const float MinHeight = -8f;
    public const float MaxHeight = 48f;
    public const float MaxBrushRadius = 24f;
    /// <summary>Default water surface height; flat land (0) stands just above it and the shore slopes down into the sea.</summary>
    public const float DefaultSeaLevel = -.8f;
    public const float MinSeaLevel = MinHeight + 1f;
    public const float MaxSeaLevel = 20f;
    /// <summary>Width of the beach that slopes from the flat interior down to the sea floor at the island edge.</summary>
    public const float ShoreWidth = 24f;
    /// <summary>Open ocean around the island that avatars can swim in before reaching the world limit.</summary>
    public const float OceanMargin = 192f;
    private const int MinUnits = -8000; // MinHeight in millimetres
    private const int MaxCells = 256;
    private const uint Magic = 0x3354564C; // "LVT3": LVT2 with the number of texture layers, which grew from 4 to 8
    private const uint MagicV2 = 0x3254564C; // "LVT2": LVT1 plus the sea level (4 layers)
    private const uint MagicV1 = 0x3154564C; // "LVT1"

    private readonly float[] _heights;
    private readonly byte[] _splat;

    public int Cells { get; }
    public float CellSize { get; }
    public int VertexCount => Cells + 1;
    public float HalfExtent => Cells * CellSize / 2f;
    public long Revision { get; set; }
    public float SeaLevel { get; private set; } = DefaultSeaLevel;
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
    public float WaterDepth(float groundHeight) => SeaLevel - groundHeight;

    /// <summary>Moves the water surface (a per-island setting). Returns false when nothing changed.</summary>
    public bool SetSeaLevel(float level)
    {
        if (!float.IsFinite(level)) return false;
        level = MathF.Round(Math.Clamp(level, MinSeaLevel, MaxSeaLevel) * 100f) / 100f;
        if (level == SeaLevel) return false;
        SeaLevel = level; Revision++;
        return true;
    }

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
    /// and a 0..1 blend for the other tools. Heights are kept on a 1 mm grid so replays stay identical.
    /// </summary>
    public bool Apply(TerrainTool tool, float worldX, float worldZ, float radius, float strength, int layer = 0) =>
        Apply(tool, worldX, worldZ, radius, strength, layer, BrushOptions.Default, null);

    /// <summary>
    /// The full brush: a round or square footprint, an edge from soft to hard, a height to level to, a test for which vertices may change
    /// (the parcels a person may edit; vertices that fail it are left exactly as they are) and the ground "Restore" returns to
    /// (the plain island of the first day when none is given). Raise, lower and noise move by metres; the rest blend by 0 to 1.
    /// </summary>
    public bool Apply(TerrainTool tool, float worldX, float worldZ, float radius, float strength, int layer, BrushOptions brush, Func<int, int, bool>? allowed, TerrainMap? reference = null)
    {
        if (!float.IsFinite(worldX) || !float.IsFinite(worldZ) || !float.IsFinite(radius) || !float.IsFinite(strength)) return false;
        if (!Enum.IsDefined(tool)) return false;
        radius = Math.Clamp(radius, CellSize / 2f, MaxBrushRadius);
        strength = Math.Clamp(strength, 0f, tool is TerrainTool.Raise or TerrainTool.Lower or TerrainTool.Noise ? 2f : 1f);
        layer = Math.Clamp(layer, 0, LayerCount - 1);
        var hardness = Math.Clamp(brush.Hardness, 0f, 1f);
        var cx = (worldX + HalfExtent) / CellSize;
        var cz = (worldZ + HalfExtent) / CellSize;
        var span = radius / CellSize;
        var target = tool == TerrainTool.Flatten ? SampleHeight(worldX, worldZ) : tool == TerrainTool.Level ? (brush.TargetHeight is { } chosen && float.IsFinite(chosen) ? Math.Clamp(chosen, MinHeight, MaxHeight) : SampleHeight(worldX, worldZ)) : 0f;
        var source = tool is TerrainTool.Smooth or TerrainTool.Erode ? (float[])_heights.Clone() : _heights;
        var ground = tool == TerrainTool.Restore ? (reference is { } r && r.Cells == Cells ? r : DefaultGround(Cells, CellSize)) : null;
        var moved = tool == TerrainTool.Erode ? new float[_heights.Length] : null;
        var changed = false;
        for (var z = Math.Max(0, (int)MathF.Floor(cz - span)); z <= Math.Min(Cells, (int)MathF.Ceiling(cz + span)); z++)
        for (var x = Math.Max(0, (int)MathF.Floor(cx - span)); x <= Math.Min(Cells, (int)MathF.Ceiling(cx + span)); x++)
        {
            var distance = brush.Square ? MathF.Max(MathF.Abs(x - cx), MathF.Abs(z - cz)) * CellSize : MathF.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) * CellSize;
            if (distance > radius) continue;
            var t = 1f - distance / radius;
            var weight = Lerp(t * t * (3f - 2f * t), 1f, hardness);
            // The outermost ring is the sea floor that continues out to the world limit; it never changes.
            if (IsBorder(x, z)) continue;
            if (allowed is not null && !allowed(x, z)) continue;
            var index = z * VertexCount + x;
            switch (tool)
            {
                case TerrainTool.Raise: changed |= SetHeight(index, _heights[index] + strength * weight); break;
                case TerrainTool.Lower: changed |= SetHeight(index, _heights[index] - strength * weight); break;
                case TerrainTool.Noise: changed |= SetHeight(index, _heights[index] + (Hash(x, z, Revision) - .5f) * 2f * strength * weight); break;
                case TerrainTool.Flatten or TerrainTool.Level: changed |= SetHeight(index, Lerp(_heights[index], target, strength * weight)); break;
                case TerrainTool.Smooth: changed |= SetHeight(index, Lerp(source[index], NeighbourAverage(source, x, z), strength * weight)); break;
                case TerrainTool.Paint: changed |= PaintVertex(index, layer, strength * weight); break;
                case TerrainTool.Restore:
                    changed |= SetHeight(index, Lerp(_heights[index], ground!._heights[index], strength * weight));
                    changed |= BlendSplat(index, ground, strength * weight);
                    break;
                case TerrainTool.Erode: Erode(source, moved!, x, z, strength * weight, allowed); break;
                default: return false;
            }
        }
        if (moved is not null)
            for (var i = 0; i < moved.Length; i++) if (moved[i] != 0f) changed |= SetHeight(i, _heights[i] + moved[i]);
        if (changed) Revision++;
        return changed;
    }

    private const float Talus = .55f; // the steepest slope that stays put: rise over run

    /// <summary>Thermal erosion: where a vertex stands steeper than the talus slope over its lowest neighbour, part of the excess slides down. Material is moved, never made.</summary>
    private void Erode(float[] heights, float[] moved, int x, int z, float amount, Func<int, int, bool>? allowed)
    {
        var index = z * VertexCount + x;
        var bestExcess = 0f; var bestIndex = -1;
        for (var oz = -1; oz <= 1; oz++)
        for (var ox = -1; ox <= 1; ox++)
        {
            if (ox == 0 && oz == 0) continue;
            int nx = x + ox, nz = z + oz;
            if (nx < 0 || nz < 0 || nx > Cells || nz > Cells || IsBorder(nx, nz) || (allowed is not null && !allowed(nx, nz))) continue;
            var run = (ox != 0 && oz != 0 ? 1.4142135f : 1f) * CellSize;
            var excess = heights[index] - heights[nz * VertexCount + nx] - Talus * run;
            if (excess > bestExcess) { bestExcess = excess; bestIndex = nz * VertexCount + nx; }
        }
        if (bestIndex < 0) return;
        var slide = bestExcess * .5f * amount;
        moved[index] -= slide; moved[bestIndex] += slide;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int, float), TerrainMap> Grounds = new();
    private static TerrainMap DefaultGround(int cells, float cellSize) => Grounds.GetOrAdd((cells, cellSize), key => new TerrainMap(key.Item1, key.Item2));

    private bool BlendSplat(int index, TerrainMap other, float amount)
    {
        var weights = new float[LayerCount];
        for (var i = 0; i < LayerCount; i++) weights[i] = Lerp(_splat[index * LayerCount + i], other._splat[index * LayerCount + i], Math.Clamp(amount, 0f, 1f));
        return StoreWeights(index, weights, -1);
    }

    public byte[] Serialize()
    {
        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, System.Text.Encoding.UTF8, true))
        {
            writer.Write(Magic); writer.Write(Cells); writer.Write(CellSize); writer.Write(Revision); writer.Write(SeaLevel); writer.Write(LayerCount);
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
            var magic = BitConverter.ToUInt32(header, 0);
            if (magic != Magic && magic != MagicV2 && magic != MagicV1) return false;
            var cells = BitConverter.ToInt32(header, 4);
            var cellSize = BitConverter.ToSingle(header, 8);
            if (cells is < 4 or > MaxCells || !float.IsFinite(cellSize) || cellSize is < .25f or > 16f) return false;
            var loaded = new TerrainMap(cells, cellSize) { Revision = BitConverter.ToInt64(header, 12) };
            var fileLayers = 4; // maps saved before the layers grew have four
            if (magic != MagicV1)
            {
                var seaBytes = new byte[4]; deflate.ReadExactly(seaBytes);
                var sea = BitConverter.ToSingle(seaBytes, 0);
                if (!float.IsFinite(sea)) return false;
                loaded.SeaLevel = Math.Clamp(sea, MinSeaLevel, MaxSeaLevel);
            }
            if (magic == Magic)
            {
                var layerBytes = new byte[4]; deflate.ReadExactly(layerBytes);
                fileLayers = BitConverter.ToInt32(layerBytes, 0);
                if (fileLayers is < 1 or > LayerCount) return false;
            }
            var heights = new byte[loaded._heights.Length * 2];
            deflate.ReadExactly(heights);
            for (var i = 0; i < loaded._heights.Length; i++) loaded._heights[i] = (BitConverter.ToUInt16(heights, i * 2) + MinUnits) / 1000f;
            for (var z = 0; z <= cells; z++)
            for (var x = 0; x <= cells; x++)
                if (loaded.IsBorder(x, z)) loaded._heights[z * loaded.VertexCount + x] = MinHeight; // maps saved before the ocean existed
            var stored = new byte[loaded._heights.Length * fileLayers];
            deflate.ReadExactly(stored);
            Array.Clear(loaded._splat);
            for (var vertex = 0; vertex < loaded._heights.Length; vertex++)
                for (var layer = 0; layer < fileLayers; layer++) loaded._splat[vertex * LayerCount + layer] = stored[vertex * fileLayers + layer];
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

    /// <summary>State of every sculptable vertex a brush at this position and radius would touch.</summary>
    public TerrainPatch Capture(float worldX, float worldZ, float radius) => Capture(worldX, worldZ, radius, false, null);

    /// <summary>Like the other capture, for a round or square footprint and only the vertices the test lets through, so undo never touches what the person could not change.</summary>
    public TerrainPatch Capture(float worldX, float worldZ, float radius, bool square, Func<int, int, bool>? allowed)
    {
        var patch = new TerrainPatch();
        if (!float.IsFinite(worldX) || !float.IsFinite(worldZ) || !float.IsFinite(radius)) return patch;
        radius = Math.Clamp(radius, CellSize / 2f, MaxBrushRadius);
        var cx = (worldX + HalfExtent) / CellSize; var cz = (worldZ + HalfExtent) / CellSize; var span = radius / CellSize;
        for (var z = Math.Max(1, (int)MathF.Floor(cz - span)); z <= Math.Min(Cells - 1, (int)MathF.Ceiling(cz + span)); z++)
        for (var x = Math.Max(1, (int)MathF.Floor(cx - span)); x <= Math.Min(Cells - 1, (int)MathF.Ceiling(cx + span)); x++)
            if ((square ? MathF.Max(MathF.Abs(x - cx), MathF.Abs(z - cz)) : MathF.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz))) * CellSize <= radius && (allowed is null || allowed(x, z))) CaptureVertex(patch, z * VertexCount + x);
        return patch;
    }

    /// <summary>Sets the patch's vertices and returns a patch that reverts the change. Border vertices are never changed.</summary>
    public TerrainPatch Restore(TerrainPatch patch)
    {
        var inverse = new TerrainPatch();
        var changed = false;
        foreach (var (index, (height, splat)) in patch.Entries)
        {
            if (index < 0 || index >= _heights.Length || IsBorder(index % VertexCount, index / VertexCount)) continue;
            CaptureVertex(inverse, index);
            changed |= SetHeight(index, height);
            for (var layer = 0; layer < LayerCount; layer++)
                if (_splat[index * LayerCount + layer] != splat[layer]) { _splat[index * LayerCount + layer] = splat[layer]; changed = true; }
        }
        if (changed) Revision++;
        return inverse;
    }

    /// <summary>Heights as 16-bit little-endian samples (MinHeight..MaxHeight), row by row, the common "RAW 16" terrain format.</summary>
    public byte[] ExportHeights16()
    {
        var bytes = new byte[_heights.Length * 2];
        for (var i = 0; i < _heights.Length; i++)
        {
            var value = (ushort)Math.Clamp((int)MathF.Round((_heights[i] - MinHeight) / (MaxHeight - MinHeight) * ushort.MaxValue), 0, ushort.MaxValue);
            bytes[i * 2] = (byte)(value & 0xFF); bytes[i * 2 + 1] = (byte)(value >> 8);
        }
        return bytes;
    }

    /// <summary>Replaces the sculptable heights from RAW 16 samples. Returns the patch that undoes the import, or null if the data does not fit.</summary>
    public TerrainPatch? ImportHeights16(byte[] data)
    {
        if (data.Length != _heights.Length * 2) return null;
        var undo = new TerrainPatch();
        var changed = false;
        for (var z = 1; z < Cells; z++)
        for (var x = 1; x < Cells; x++)
        {
            var index = z * VertexCount + x;
            var value = data[index * 2] | (data[index * 2 + 1] << 8);
            var before = _heights[index];
            if (SetHeight(index, MinHeight + value / (float)ushort.MaxValue * (MaxHeight - MinHeight)))
            {
                var old = new byte[LayerCount];
                for (var layer = 0; layer < LayerCount; layer++) old[layer] = _splat[index * LayerCount + layer];
                undo.Add(index, before, old);
                changed = true;
            }
        }
        if (changed) Revision++;
        return undo;
    }

    private void CaptureVertex(TerrainPatch patch, int index)
    {
        var splat = new byte[LayerCount];
        for (var layer = 0; layer < LayerCount; layer++) splat[layer] = _splat[index * LayerCount + layer];
        patch.Add(index, _heights[index], splat);
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
        return StoreWeights(index, weights, layer);
    }

    /// <summary>Stores layer weights (any scale) as bytes that always add up to 255; the rounding leftover goes to <paramref name="main"/>.</summary>
    private bool StoreWeights(int index, float[] weights, int main)
    {
        var offset = index * LayerCount;
        var total = 0f; foreach (var w in weights) total += w;
        if (total <= 0f) { weights = new float[LayerCount]; weights[Math.Max(main, 0)] = 255f; total = 255f; }
        var scale = MathF.Abs(total - 255f) < .01f ? 1f : 255f / total;
        var result = new byte[LayerCount]; var sum = 0;
        for (var i = 0; i < LayerCount; i++) { result[i] = (byte)MathF.Round(weights[i] * scale); sum += result[i]; }
        if (main < 0) { main = 0; for (var i = 1; i < LayerCount; i++) if (result[i] > result[main]) main = i; } // the dominant layer absorbs the rounding
        result[main] = (byte)Math.Clamp(result[main] + (255 - sum), 0, 255);
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
