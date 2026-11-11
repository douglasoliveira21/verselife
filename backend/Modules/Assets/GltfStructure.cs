using System.Buffers.Binary;
using System.Text.Json;

namespace LifeVerse.Api.Modules.Assets;

/// <summary>
/// Checks that the pieces of a glTF point at data that really exists: the binary part is there, every buffer view and every accessor fits inside it, and a skin or an animation only mentions parts of
/// the model that exist. A model that passes the counts of <see cref="GltfLimits"/> but is cut off would otherwise be accepted and then fail on every player computer.
/// </summary>
public static class GltfStructure
{
    public const int MaxJointsPerSkin = 512;

    private static int ComponentSize(int type) => type switch { 5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4, _ => 0 };
    private static int Components(string? type) => type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT2" => 4, "MAT3" => 9, "MAT4" => 16, _ => 0 };

    /// <summary>Null when the model is consistent, otherwise a message the person can act on.</summary>
    public static string? Check(byte[] bytes, bool binary)
    {
        try
        {
            string json; long binLength = -1;
            if (binary)
            {
                var jsonLength = (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
                json = System.Text.Encoding.UTF8.GetString(bytes, 20, (int)jsonLength);
                var next = 20 + ((jsonLength + 3) & ~3L);
                if (next + 8 <= bytes.Length && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)next + 4)) == 0x004E4942)
                {
                    binLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)next));
                    if (next + 8 + binLength > bytes.Length) return "The data of the model is cut off (the file is incomplete). Export it again.";
                }
            }
            else json = System.Text.Encoding.UTF8.GetString(bytes);
            using var document = JsonDocument.Parse(json.TrimStart('﻿')); var root = document.RootElement;

            var bufferLengths = new List<long>();
            if (root.TryGetProperty("buffers", out var buffers) && buffers.ValueKind == JsonValueKind.Array)
                foreach (var buffer in buffers.EnumerateArray())
                {
                    var length = buffer.TryGetProperty("byteLength", out var l) && l.TryGetInt64(out var n) ? n : 0;
                    if (length <= 0) return "A data block of the model is empty.";
                    var embedded = buffer.TryGetProperty("uri", out var uri) && uri.ValueKind == JsonValueKind.String;
                    if (!embedded && bufferLengths.Count == 0 && binary && binLength < 0) return "The model has no binary data (the GLB is incomplete). Export it again.";
                    if (!embedded && bufferLengths.Count == 0 && binary && binLength < length) return "The data of the model is shorter than it says (the GLB is cut off). Export it again.";
                    bufferLengths.Add(length);
                }
            var views = new List<(int Buffer, long Offset, long Length, int Stride)>();
            if (root.TryGetProperty("bufferViews", out var viewArray) && viewArray.ValueKind == JsonValueKind.Array)
                foreach (var view in viewArray.EnumerateArray())
                {
                    var buffer = view.TryGetProperty("buffer", out var b) && b.TryGetInt32(out var bi) ? bi : -1;
                    var offset = view.TryGetProperty("byteOffset", out var o) && o.TryGetInt64(out var oi) ? oi : 0;
                    var length = view.TryGetProperty("byteLength", out var l) && l.TryGetInt64(out var li) ? li : 0;
                    var stride = view.TryGetProperty("byteStride", out var s) && s.TryGetInt32(out var si) ? si : 0;
                    if (buffer < 0 || buffer >= bufferLengths.Count) return "A part of the model points at data that does not exist.";
                    if (offset < 0 || length <= 0 || offset + length > bufferLengths[buffer]) return "A part of the model reaches past the end of its data (the file is damaged).";
                    views.Add((buffer, offset, length, stride));
                }
            if (root.TryGetProperty("accessors", out var accessors) && accessors.ValueKind == JsonValueKind.Array)
                foreach (var accessor in accessors.EnumerateArray())
                {
                    if (accessor.TryGetProperty("sparse", out _) || !accessor.TryGetProperty("bufferView", out var bv) || !bv.TryGetInt32(out var viewIndex)) continue;
                    if (viewIndex < 0 || viewIndex >= views.Count) return "A part of the model points at data that does not exist.";
                    var count = accessor.TryGetProperty("count", out var c) && c.TryGetInt64(out var ci) ? ci : 0;
                    var element = ComponentSize(accessor.TryGetProperty("componentType", out var ct) && ct.TryGetInt32(out var cti) ? cti : 0) * Components(accessor.TryGetProperty("type", out var t) ? t.GetString() : null);
                    if (count <= 0 || element <= 0) return "A part of the model has an invalid data description.";
                    var offset = accessor.TryGetProperty("byteOffset", out var ao) && ao.TryGetInt64(out var aoi) ? aoi : 0;
                    var view = views[viewIndex]; var stride = view.Stride > 0 ? view.Stride : element;
                    if (offset + (count - 1) * stride + element > view.Length) return "A part of the model reaches past the end of its data (the file is damaged).";
                }
            var nodeCount = root.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array ? nodes.GetArrayLength() : 0;
            if (root.TryGetProperty("skins", out var skins) && skins.ValueKind == JsonValueKind.Array)
                foreach (var skin in skins.EnumerateArray())
                {
                    if (!skin.TryGetProperty("joints", out var joints) || joints.ValueKind != JsonValueKind.Array || joints.GetArrayLength() == 0) return "A skeleton of the model has no bones.";
                    if (joints.GetArrayLength() > MaxJointsPerSkin) return $"A skeleton has {joints.GetArrayLength()} bones; the limit is {MaxJointsPerSkin}.";
                    foreach (var joint in joints.EnumerateArray()) if (!joint.TryGetInt32(out var ji) || ji < 0 || ji >= nodeCount) return "A skeleton bone points at a part that does not exist.";
                }
            if (root.TryGetProperty("animations", out var animations) && animations.ValueKind == JsonValueKind.Array)
                foreach (var animation in animations.EnumerateArray())
                {
                    var samplers = animation.TryGetProperty("samplers", out var sa) && sa.ValueKind == JsonValueKind.Array ? sa.GetArrayLength() : 0;
                    if (!animation.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Array || channels.GetArrayLength() == 0) return "An animation of the model has no movement in it.";
                    foreach (var channel in channels.EnumerateArray())
                    {
                        if (!channel.TryGetProperty("sampler", out var sp) || !sp.TryGetInt32(out var spi) || spi < 0 || spi >= samplers) return "An animation points at movement data that does not exist.";
                        if (channel.TryGetProperty("target", out var target) && target.TryGetProperty("node", out var tn) && tn.TryGetInt32(out var tni) && (tni < 0 || tni >= nodeCount)) return "An animation moves a part that does not exist.";
                    }
                }
            return null;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or IndexOutOfRangeException or OverflowException) { return "The model could not be read (the file is damaged)."; }
    }
}
