using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace LifeVerse.Shared.Avatar;

/// <summary>
/// A small reader for the parts of a glTF 2.0 file the avatar checks need: the nodes and their transforms, the skins, and the numbers inside
/// accessors (positions' bounds, joint indices, weights). It reads GLB files and .gltf files whose buffers are embedded; it never loads
/// geometry into an engine and never follows a link to another file.
/// </summary>
public sealed class GltfReader : IDisposable
{
    private readonly JsonDocument _document;
    private readonly byte[][] _buffers;

    public JsonElement Root => _document.RootElement;
    public int FileSize { get; }

    private GltfReader(JsonDocument document, byte[][] buffers, int size) { _document = document; _buffers = buffers; FileSize = size; }

    public void Dispose() => _document.Dispose();

    /// <summary>Reads the file; null with the reason (a stable code and a sentence) when it is not a usable glTF 2.0.</summary>
    public static GltfReader? Read(byte[] bytes, bool binary, out string code, out string message)
    {
        code = string.Empty; message = string.Empty;
        string json; byte[]? chunk = null;
        try
        {
            if (binary)
            {
                if (bytes.Length < 20 || bytes[0] != 'g' || bytes[1] != 'l' || bytes[2] != 'T' || bytes[3] != 'F') { code = "bad_glb"; message = "The file is not a valid GLB (the header is wrong)."; return null; }
                if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) != 2) { code = "bad_version"; message = "Only glTF 2.0 models are supported."; return null; }
                var total = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
                if (total > bytes.Length) { code = "bad_glb"; message = "The GLB file is cut off."; return null; }
                var offset = 12; string? text = null;
                while (offset + 8 <= total)
                {
                    var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)); var type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4));
                    if (length > total || offset + 8L + length > total) { code = "bad_glb"; message = "The GLB file is damaged (a part runs past the end)."; return null; }
                    if (type == 0x4E4F534A && text is null) text = Encoding.UTF8.GetString(bytes, offset + 8, (int)length);
                    else if (type == 0x004E4942 && chunk is null) chunk = bytes.AsSpan(offset + 8, (int)length).ToArray();
                    offset += 8 + (int)((length + 3) & ~3u);
                }
                if (text is null) { code = "bad_glb"; message = "The GLB file has no JSON part."; return null; }
                json = text;
            }
            else json = Encoding.UTF8.GetString(bytes);
            var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("asset", out var asset) || !asset.TryGetProperty("version", out var version) || !(version.GetString() ?? string.Empty).StartsWith('2')) { document.Dispose(); code = "bad_version"; message = "Only glTF 2.0 models are supported."; return null; }
            var buffers = new List<byte[]>();
            if (root.TryGetProperty("buffers", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var buffer in list.EnumerateArray())
                {
                    if (buffer.TryGetProperty("uri", out var uri))
                    {
                        var text = uri.GetString() ?? string.Empty;
                        if (!text.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || text.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase) < 0) { document.Dispose(); code = "external_reference"; message = "The model points to another file. Export it with everything embedded (a single .glb)."; return null; }
                        buffers.Add(Convert.FromBase64String(text[(text.IndexOf(',') + 1)..]));
                    }
                    else buffers.Add(chunk ?? []);
                }
            return new GltfReader(document, [.. buffers], bytes.Length);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException) { code = "bad_json"; message = "The model could not be read (its JSON is not valid)."; return null; }
    }

    public IEnumerable<JsonElement> Items(string name) => Root.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray() : [];
    public int Count(string name) => Root.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array ? list.GetArrayLength() : 0;
    public JsonElement? At(string name, int index) => Root.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array && index >= 0 && index < list.GetArrayLength() ? list[index] : null;

    // ---- nodes ---------------------------------------------------------------------------------------------------------------------

    /// <summary>The local transform of a node (its matrix, or translation, rotation and scale).</summary>
    public Matrix4x4 LocalTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out var m) && m.ValueKind == JsonValueKind.Array && m.GetArrayLength() == 16)
        {
            var v = m.EnumerateArray().Select(x => x.GetSingle()).ToArray();
            return new Matrix4x4(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11], v[12], v[13], v[14], v[15]);
        }
        var t = Vector3.Zero; var s = Vector3.One; var r = Quaternion.Identity;
        if (node.TryGetProperty("translation", out var tr) && tr.GetArrayLength() == 3) t = new Vector3(tr[0].GetSingle(), tr[1].GetSingle(), tr[2].GetSingle());
        if (node.TryGetProperty("scale", out var sc) && sc.GetArrayLength() == 3) s = new Vector3(sc[0].GetSingle(), sc[1].GetSingle(), sc[2].GetSingle());
        if (node.TryGetProperty("rotation", out var ro) && ro.GetArrayLength() == 4) r = Quaternion.Normalize(new Quaternion(ro[0].GetSingle(), ro[1].GetSingle(), ro[2].GetSingle(), ro[3].GetSingle()));
        return Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
    }

    /// <summary>For every node: its parent (or -1) and its world transform, computed from the scene graph. A cycle or a bad child index is reported as null.</summary>
    public (int[] Parents, Matrix4x4[] World)? Hierarchy()
    {
        var count = Count("nodes"); var parents = Enumerable.Repeat(-1, count).ToArray(); var world = new Matrix4x4[count];
        for (var i = 0; i < count; i++)
            if (At("nodes", i) is { } node && node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
                foreach (var child in children.EnumerateArray())
                {
                    if (!child.TryGetInt32(out var index) || index < 0 || index >= count || parents[index] != -1 || index == i) return null;
                    parents[index] = i;
                }
        var state = new int[count];
        bool Compute(int i)
        {
            if (state[i] == 2) return true; if (state[i] == 1) return false; state[i] = 1;
            var local = LocalTransform(At("nodes", i)!.Value);
            if (parents[i] >= 0) { if (!Compute(parents[i])) return false; world[i] = local * world[parents[i]]; } else world[i] = local;
            state[i] = 2; return true;
        }
        for (var i = 0; i < count; i++) if (!Compute(i)) return null;
        return (parents, world);
    }

    // ---- accessors -----------------------------------------------------------------------------------------------------------------

    public sealed record AccessorInfo(int Count, int Components, int ComponentType, bool Normalized, float[]? Min, float[]? Max);

    public AccessorInfo? Accessor(int index)
    {
        if (At("accessors", index) is not { } a) return null;
        var type = a.TryGetProperty("type", out var t) ? t.GetString() : null;
        var components = type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, "MAT3" => 9, "MAT2" => 4, _ => 0 };
        if (components == 0 || !a.TryGetProperty("count", out var c) || !c.TryGetInt32(out var count) || !a.TryGetProperty("componentType", out var ct) || !ct.TryGetInt32(out var componentType)) return null;
        float[]? Bound(string name) => a.TryGetProperty(name, out var b) && b.ValueKind == JsonValueKind.Array ? [.. b.EnumerateArray().Select(x => x.GetSingle())] : null;
        return new AccessorInfo(count, components, componentType, a.TryGetProperty("normalized", out var n) && n.ValueKind == JsonValueKind.True, Bound("min"), Bound("max"));
    }

    /// <summary>Reads an accessor as floats (integers are converted; normalized integers are mapped into 0..1 or -1..1). Null when it points outside its buffer or is sparse.</summary>
    public float[]? ReadFloats(int index, int maxElements = 4_000_000)
    {
        if (Accessor(index) is not { } info || At("accessors", index) is not { } accessor) return null;
        if (accessor.TryGetProperty("sparse", out _)) return null;
        if ((long)info.Count * info.Components > maxElements) return null;
        var size = ComponentSize(info.ComponentType); if (size == 0) return null;
        var result = new float[info.Count * info.Components];
        if (!accessor.TryGetProperty("bufferView", out var viewIndex) || !viewIndex.TryGetInt32(out var vi)) return result; // no view means all zeros
        if (At("bufferViews", vi) is not { } view || !view.TryGetProperty("buffer", out var bi) || !bi.TryGetInt32(out var bufferIndex) || bufferIndex < 0 || bufferIndex >= _buffers.Length) return null;
        var buffer = _buffers[bufferIndex];
        var viewOffset = view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt64() : 0; var accessorOffset = accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt64() : 0;
        var stride = view.TryGetProperty("byteStride", out var bs) && bs.GetInt32() > 0 ? bs.GetInt32() : size * info.Components;
        for (var i = 0; i < info.Count; i++)
            for (var c = 0; c < info.Components; c++)
            {
                var at = viewOffset + accessorOffset + (long)i * stride + (long)c * size;
                if (at < 0 || at + size > buffer.Length) return null;
                result[i * info.Components + c] = ReadComponent(buffer, (int)at, info.ComponentType, info.Normalized);
            }
        return result;
    }

    private static int ComponentSize(int type) => type switch { 5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4, _ => 0 };

    private static float ReadComponent(byte[] b, int at, int type, bool normalized) => type switch
    {
        5126 => BitConverter.ToSingle(b, at),
        5121 => normalized ? b[at] / 255f : b[at],
        5120 => normalized ? Math.Max((sbyte)b[at] / 127f, -1f) : (sbyte)b[at],
        5123 => normalized ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at)) / 65535f : BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at)),
        5122 => normalized ? Math.Max(BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(at)) / 32767f, -1f) : BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(at)),
        5125 => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at)),
        _ => 0
    };
}
