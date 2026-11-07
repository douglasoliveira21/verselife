using System.Buffers.Binary;
using System.Text.Json;

namespace LifeVerse.Api.Modules.Assets;

/// <summary>What a 3D model contains, read from the JSON part of a GLB or glTF file without loading any geometry.</summary>
public sealed record GltfStats(int Meshes, int Triangles, int Materials, int Textures, int Images, int Nodes, int Animations);

/// <summary>
/// Limits for imported models, so one file cannot make every player's computer crawl. The numbers come from the file's own
/// accessor counts (triangles from the index counts, or from the vertex counts when a primitive has no indices).
/// </summary>
public static class GltfLimits
{
    public const int MaxTriangles = 300_000;
    public const int MaxMaterials = 64;
    public const int MaxTextures = 32;
    public const int MaxNodes = 5_000;

    /// <summary>Statistics of the file, or null with the reason when it cannot be read.</summary>
    public static GltfStats? Inspect(byte[] bytes, bool binary, out string? error)
    {
        error = null;
        string json;
        if (binary)
        {
            if (bytes.Length < 20 || bytes[0] != 'g' || bytes[1] != 'l' || bytes[2] != 'T' || bytes[3] != 'F') { error = "Invalid GLB header."; return null; }
            var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
            var total = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
            var type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16));
            if (version != 2) { error = "Only glTF 2.0 models are supported."; return null; }
            if (total > bytes.Length || type != 0x4E4F534A || length == 0 || 20L + length > bytes.Length) { error = "The GLB file is damaged (its JSON part is missing or cut off)."; return null; }
            json = System.Text.Encoding.UTF8.GetString(bytes, 20, (int)length);
        }
        else json = DecodeJson(bytes);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = "The model is not a glTF document."; return null; }
            if (!root.TryGetProperty("asset", out var asset) || !asset.TryGetProperty("version", out var v) || !(v.GetString() ?? "").StartsWith("2", StringComparison.Ordinal)) { error = "Only glTF 2.0 models are supported."; return null; }
            var accessors = root.TryGetProperty("accessors", out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.TryGetProperty("count", out var c) && c.TryGetInt64(out var n) ? n : 0L).ToArray() : [];
            long triangles = 0; var meshes = 0;
            if (root.TryGetProperty("meshes", out var meshArray) && meshArray.ValueKind == JsonValueKind.Array)
                foreach (var mesh in meshArray.EnumerateArray())
                {
                    meshes++;
                    if (!mesh.TryGetProperty("primitives", out var primitives) || primitives.ValueKind != JsonValueKind.Array) continue;
                    foreach (var primitive in primitives.EnumerateArray())
                    {
                        var mode = primitive.TryGetProperty("mode", out var m) && m.TryGetInt32(out var modeValue) ? modeValue : 4;
                        if (mode != 4 && mode != 5 && mode != 6) continue; // points and lines are not triangles
                        long count = 0;
                        if (primitive.TryGetProperty("indices", out var idx) && idx.TryGetInt32(out var iIdx) && iIdx >= 0 && iIdx < accessors.Length) count = accessors[iIdx];
                        else if (primitive.TryGetProperty("attributes", out var attrs) && attrs.TryGetProperty("POSITION", out var pos) && pos.TryGetInt32(out var pIdx) && pIdx >= 0 && pIdx < accessors.Length) count = accessors[pIdx];
                        triangles += mode == 4 ? count / 3 : Math.Max(0, count - 2);
                    }
                }
            int Count(string name) => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Array ? e.GetArrayLength() : 0;
            return new GltfStats(meshes, (int)Math.Min(triangles, int.MaxValue), Count("materials"), Count("textures"), Count("images"), Count("nodes"), Count("animations"));
        }
        catch (JsonException) { error = "The model's JSON is not valid."; return null; }
    }

    private static string DecodeJson(byte[] bytes)
    {
        var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return System.Text.Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
    }

    /// <summary>Null when the model is within the limits, otherwise a message the person can act on.</summary>
    public static string? Check(GltfStats stats)
    {
        if (stats.Meshes == 0 || stats.Triangles == 0) return "The model has no triangles to show.";
        if (stats.Triangles > MaxTriangles) return $"The model has {stats.Triangles:N0} triangles; the limit is {MaxTriangles:N0}. Simplify it before importing.";
        if (stats.Materials > MaxMaterials) return $"The model has {stats.Materials} materials; the limit is {MaxMaterials}.";
        if (stats.Textures > MaxTextures || stats.Images > MaxTextures) return $"The model uses more than {MaxTextures} textures.";
        if (stats.Nodes > MaxNodes) return $"The model has {stats.Nodes:N0} parts; the limit is {MaxNodes:N0}.";
        return null;
    }
}
