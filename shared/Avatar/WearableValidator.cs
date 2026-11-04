using System.Numerics;
using System.Text.Json;

namespace LifeVerse.Shared.Avatar;

/// <summary>What a worn item is: rigged clothing that bends with the body, or a rigid piece fixed to one bone.</summary>
public enum WearableKind { Clothing, Attachment }

public sealed class WearableReport
{
    public List<ModelIssue> Issues { get; } = [];
    public IEnumerable<ModelIssue> Errors => Issues.Where(i => i.IsError);
    public IEnumerable<ModelIssue> Warnings => Issues.Where(i => !i.IsError);
    public bool Ok => !Issues.Any(i => i.IsError);
    public WearableKind Kind { get; set; }
    public string Slot { get; set; } = string.Empty;
    public string Layer { get; set; } = "base";
    public string[] Covers { get; set; } = [];
    public bool HidesHair { get; set; }
    /// <summary>For a piece of hair or eyes: which colour of the avatar tints it ("hair" or "eyes"); empty when the file keeps its own colours.</summary>
    public string Tint { get; set; } = string.Empty;
    /// <summary>For an attachment: the standard bone it is fixed to, and where and how big it sits there.</summary>
    public string AttachBone { get; set; } = string.Empty;
    public float[] Offset { get; set; } = [0, 0, 0];
    public float[] Rotation { get; set; } = [0, 0, 0];
    public float Scale { get; set; } = 1f;
    public int SizeBytes { get; set; }
    public int Triangles { get; set; }
    public int Vertices { get; set; }
    public int Joints { get; set; }
    /// <summary>Clothing: how far (in metres after fitting its scale) the bones of the garment sit from the bones of the standard skeleton.</summary>
    public float FitDeviation { get; set; }
    /// <summary>Clothing: the standard bones the garment is weighted to.</summary>
    public List<string> Bones { get; } = [];

    public void Error(string code, string message) => Issues.Add(new ModelIssue(code, message, true));
    public void Warn(string code, string message) => Issues.Add(new ModelIssue(code, message, false));

    public string ToJson() => JsonSerializer.Serialize(new
    {
        ok = Ok, kind = Kind.ToString().ToLowerInvariant(), slot = Slot, layer = Layer, covers = Covers, hidesHair = HidesHair, tint = Tint, attachBone = AttachBone, offset = Offset, rotation = Rotation, scale = Scale,
        sizeBytes = SizeBytes, triangles = Triangles, vertices = Vertices, joints = Joints, fitDeviation = FitDeviation, bones = Bones,
        issues = Issues.Select(i => new { code = i.Code, message = i.Message, error = i.IsError })
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

/// <summary>
/// Decides whether a GLB can be worn. The file says what it is in its asset extras ("lifeverse": slot, layer, what it covers, the bone a rigid
/// piece is fixed to). Clothing must be skinned to bones of the standard humanoid (any naming convention), only to the region its slot allows,
/// laid out like the standard skeleton; a rigid piece must be a sensible size and name a real bone. Budgets keep outfits light.
/// </summary>
public static class WearableValidator
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public const int MaxTriangles = 20_000;
    public const int MaxVertices = 30_000;
    public const int MaxJoints = 128;
    public const int MaxMaterials = 4;
    public const int MaxImages = 4;
    public const float WarnDeviation = .08f, MaxDeviation = .25f;

    public static readonly string[] ClothingSlots = ["top", "jacket", "bottom", "shoes", "gloves"];
    public static readonly string[] AttachmentSlots = ["hat", "glasses", "neck", "ears", "back", "accessory", "hair", "eyes"];

    /// <summary>The slots of a person's own hair and eyes: rigid pieces that go on the head and replace the ones of the catalog.</summary>
    public static bool IsFace(string slot) => slot is "hair" or "eyes";

    private static readonly string[] Arms = ["upper_arm_l", "lower_arm_l", "hand_l", "upper_arm_r", "lower_arm_r", "hand_r"];
    private static readonly string[] Legs = ["upper_leg_l", "lower_leg_l", "foot_l", "upper_leg_r", "lower_leg_r", "foot_r"];

    /// <summary>The bones a garment of this slot may be weighted to; weight outside this region means the garment was made for another part of the body.</summary>
    public static IReadOnlySet<string> Region(string slot) => slot switch
    {
        "top" or "jacket" => new HashSet<string>(["hips", "spine", "chest", "neck", .. Arms]),
        "bottom" => new HashSet<string>(["hips", "spine", .. Legs]),
        "shoes" => new HashSet<string>(["foot_l", "foot_r", "lower_leg_l", "lower_leg_r"]),
        "gloves" => new HashSet<string>(["hand_l", "hand_r", "lower_arm_l", "lower_arm_r"]),
        _ => new HashSet<string>()
    };

    /// <summary>The bone a rigid piece of this slot is fixed to when the file does not say.</summary>
    public static string DefaultBone(string slot) => slot switch { "neck" => "neck", "back" => "chest", _ => "head" };

    public static WearableReport Validate(byte[] bytes)
    {
        var report = new WearableReport { SizeBytes = bytes.Length };
        if (bytes.Length == 0) { report.Error("empty", "The file is empty."); return report; }
        if (bytes.Length > MaxBytes) { report.Error("too_big", $"The file is larger than {MaxBytes / 1024 / 1024} MB."); return report; }
        using var gltf = GltfReader.Read(bytes, binary: true, out var code, out var message);
        if (gltf is null) { report.Error(code, message); return report; }

        if (gltf.Root.TryGetProperty("extensionsRequired", out var required) && required.ValueKind == JsonValueKind.Array)
            foreach (var extension in required.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(e => !e.StartsWith("KHR_materials_") && e != "KHR_texture_transform"))
                report.Error("unsupported_extension", $"The model needs the extension {extension}, which the game does not support.");
        foreach (var image in gltf.Items("images")) if (image.TryGetProperty("uri", out var uri) && !(uri.GetString() ?? string.Empty).StartsWith("data:", StringComparison.OrdinalIgnoreCase)) { report.Error("external_reference", "A texture points to another file. Export a single .glb with everything inside."); break; }
        if (gltf.Count("materials") > MaxMaterials) report.Error("too_many_materials", $"The item has {gltf.Count("materials")} materials; the limit is {MaxMaterials}.");
        if (gltf.Count("images") > MaxImages) report.Error("too_many_textures", $"The item has {gltf.Count("images")} textures; the limit is {MaxImages}.");

        // ---- what the file says it is
        var meta = Metadata(gltf);
        if (meta is null) { report.Error("no_metadata", "The file does not say what it is. Add the extras {\"lifeverse\": {\"slot\": \"top\"}} to the asset (for a rigid piece also \"bone\": \"head\")."); return report; }
        var slot = Text(meta.Value, "slot"); report.Slot = slot;
        if (!AvatarRules.Slots.Contains(slot)) report.Error("bad_slot", $"“{slot}” is not a slot of the avatar. The slots are: {string.Join(", ", AvatarRules.Slots)}.");
        report.Layer = Text(meta.Value, "layer") is { Length: > 0 } layer ? layer : slot == "jacket" ? "outer" : "base";
        if (report.Layer is not ("base" or "outer")) report.Error("bad_layer", "The layer is “base” or “outer”.");
        report.HidesHair = slot == "hair" || meta.Value.TryGetProperty("hidesHair", out var hides) && hides.ValueKind == JsonValueKind.True; // own hair always replaces the catalog hair
        report.Tint = Text(meta.Value, "tint");
        if (report.Tint.Length > 0 && report.Tint != slot) report.Error("bad_tint", slot is "hair" or "eyes" ? $"“tint” of a piece for {slot} can only be “{slot}” (the colour chosen in the editor) or left out." : "“tint” is only for pieces of hair and eyes.");
        report.Covers = meta.Value.TryGetProperty("covers", out var covers) && covers.ValueKind == JsonValueKind.Array ? [.. covers.EnumerateArray().Select(c => c.GetString() ?? string.Empty)] : [];
        foreach (var covered in report.Covers) if (!AvatarRules.Slots.Contains(covered) || covered == slot) report.Error("bad_covers", $"“{covered}” cannot be covered by an item of slot {slot}.");

        var skinned = gltf.Items("nodes").Any(n => n.TryGetProperty("skin", out _) && n.TryGetProperty("mesh", out _));
        report.Kind = skinned ? WearableKind.Clothing : WearableKind.Attachment;
        if (Text(meta.Value, "kind") is { Length: > 0 } declared && !string.Equals(declared, report.Kind.ToString(), StringComparison.OrdinalIgnoreCase)) report.Error("kind_mismatch", $"The file says it is {declared}, but it {(skinned ? "has a skeleton, so it is clothing" : "has no skeleton, so it is a rigid piece")}.");
        if (report.Ok || report.Slot.Length > 0)
        {
            if (skinned && !ClothingSlots.Contains(slot) && AvatarRules.Slots.Contains(slot)) report.Error("slot_kind", $"Rigged clothing can be for: {string.Join(", ", ClothingSlots)}. For “{slot}” use a rigid piece (no skeleton) fixed to a bone.");
            if (!skinned && !AttachmentSlots.Contains(slot) && AvatarRules.Slots.Contains(slot)) report.Error("slot_kind", $"A rigid piece can be for: {string.Join(", ", AttachmentSlots)}. For “{slot}” the item must be rigged to the skeleton.");
        }
        if (!report.Ok) return report;

        if (gltf.Hierarchy() is not { } hierarchy) { report.Error("bad_hierarchy", "The nodes of the model are not a valid tree."); return report; }
        return skinned ? Clothing(gltf, report, hierarchy.Parents, hierarchy.World) : Attachment(gltf, report, meta.Value, hierarchy.World);
    }

    private static JsonElement? Metadata(GltfReader gltf) =>
        gltf.Root.TryGetProperty("asset", out var asset) && asset.TryGetProperty("extras", out var extras) && extras.ValueKind == JsonValueKind.Object && extras.TryGetProperty("lifeverse", out var meta) && meta.ValueKind == JsonValueKind.Object ? meta : null;

    private static string Text(JsonElement element, string name) => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static WearableReport Attachment(GltfReader gltf, WearableReport report, JsonElement meta, Matrix4x4[] world)
    {
        var bone = Text(meta, "bone"); if (bone.Length == 0) bone = DefaultBone(report.Slot);
        if (!BoneMapper.Standard.Contains(bone)) report.Error("bad_bone", $"“{bone}” is not a bone of the standard skeleton. Use one of: {string.Join(", ", BoneMapper.Standard)}.");
        report.AttachBone = bone;
        if (IsFace(report.Slot) && bone != "head") report.Error("wrong_bone", $"Hair and eyes are fixed to the head, not to “{bone}”.");
        float[] Vec(string name, float limit)
        {
            if (!meta.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return [0, 0, 0];
            var values = v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetSingle() : float.NaN).ToArray();
            if (values.Length != 3 || values.Any(x => !float.IsFinite(x) || Math.Abs(x) > limit)) { report.Error("bad_placement", $"“{name}” must be three numbers within ±{limit}."); return [0, 0, 0]; }
            return values;
        }
        report.Offset = Vec("offset", 2f); report.Rotation = Vec("rotation", 360f);
        if (meta.TryGetProperty("scale", out var scale)) { if (scale.ValueKind == JsonValueKind.Number && scale.GetSingle() is >= .1f and <= 5f) report.Scale = scale.GetSingle(); else report.Error("bad_placement", "“scale” must be a number between 0.1 and 5."); }

        // size: the box of all the geometry, in its own units
        Vector3 min = new(float.MaxValue), max = new(float.MinValue); long triangles = 0, vertices = 0; var nodeIndex = 0;
        foreach (var node in gltf.Items("nodes"))
        {
            var current = nodeIndex++;
            if (!node.TryGetProperty("mesh", out var meshIndex) || gltf.At("meshes", meshIndex.GetInt32()) is not { } mesh || !mesh.TryGetProperty("primitives", out var primitives)) continue;
            foreach (var primitive in primitives.EnumerateArray())
            {
                if (!primitive.TryGetProperty("attributes", out var attributes) || !attributes.TryGetProperty("POSITION", out var positionIndex) || gltf.Accessor(positionIndex.GetInt32()) is not { } info) { report.Error("bad_primitive", "A mesh has no positions."); continue; }
                vertices += info.Count; long indices = primitive.TryGetProperty("indices", out var ix) && gltf.Accessor(ix.GetInt32()) is { } ii ? ii.Count : info.Count; triangles += indices / 3;
                if (info.Min is { Length: 3 } lo && info.Max is { Length: 3 } hi)
                    for (var corner = 0; corner < 8; corner++)
                    {
                        var p = Vector3.Transform(new Vector3((corner & 1) == 0 ? lo[0] : hi[0], (corner & 2) == 0 ? lo[1] : hi[1], (corner & 4) == 0 ? lo[2] : hi[2]), world[current]);
                        min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                    }
                else report.Error("no_bounds", "A mesh has no position bounds (min and max), which are required by glTF.");
            }
        }
        report.Triangles = (int)Math.Min(triangles, int.MaxValue); report.Vertices = (int)Math.Min(vertices, int.MaxValue);
        Budgets(report);
        if (report.Triangles == 0) report.Error("no_triangles", "The item has nothing to show.");
        else if (min.X <= max.X)
        {
            var size = (max - min) * report.Scale; var largest = Math.Max(size.X, Math.Max(size.Y, size.Z));
            if (report.Slot == "eyes" && largest > .4f) report.Error("too_large", $"A pair of eyes is {largest:0.##} units across; it must be under 0.4 (metres, after its scale).");
            else if (largest > 1.5f) report.Error("too_large", $"The piece is {largest:0.##} units across; a worn piece must be under 1.5 (metres, after its scale).");
            else if (largest < .005f) report.Error("too_small", "The piece is smaller than half a centimetre.");
            if (gltf.Count("animations") > 0) report.Warn("animations_ignored", "The animations inside the file are ignored.");
        }
        return report;
    }

    private static void Budgets(WearableReport report)
    {
        if (report.Triangles > MaxTriangles) report.Error("too_many_triangles", $"The item has {report.Triangles:N0} triangles; the limit for a worn item is {MaxTriangles:N0}.");
        if (report.Vertices > MaxVertices) report.Error("too_many_vertices", $"The item has {report.Vertices:N0} vertices; the limit for a worn item is {MaxVertices:N0}.");
    }

    /// <summary>Where a bone of the standard skeleton sits at rest, in metres, for an avatar 1.78 m tall (what garments are measured against and what rigid pieces are placed from).</summary>
    public static float[] ReferencePosition(string bone) => Reference.TryGetValue(bone, out var p) ? [p.X, p.Y, p.Z] : [0, 0, 0];

    // positions of the bones in the standard template, used to judge whether a garment was made for this skeleton
    private static readonly Dictionary<string, Vector3> Reference = new()
    {
        ["hips"] = new(0, .95f, 0), ["spine"] = new(0, 1.07f, 0), ["chest"] = new(0, 1.27f, 0), ["neck"] = new(0, 1.45f, 0), ["head"] = new(0, 1.55f, 0),
        ["upper_arm_l"] = new(.18f, 1.37f, 0), ["lower_arm_l"] = new(.46f, 1.37f, 0), ["hand_l"] = new(.71f, 1.37f, 0),
        ["upper_arm_r"] = new(-.18f, 1.37f, 0), ["lower_arm_r"] = new(-.46f, 1.37f, 0), ["hand_r"] = new(-.71f, 1.37f, 0),
        ["upper_leg_l"] = new(.09f, .9f, 0), ["lower_leg_l"] = new(.09f, .48f, 0), ["foot_l"] = new(.09f, .06f, 0),
        ["upper_leg_r"] = new(-.09f, .9f, 0), ["lower_leg_r"] = new(-.09f, .48f, 0), ["foot_r"] = new(-.09f, .06f, 0)
    };

    private static WearableReport Clothing(GltfReader gltf, WearableReport report, int[] parents, Matrix4x4[] world)
    {
        var skinned = gltf.Items("nodes").Select((n, i) => (n, i)).Where(x => x.n.TryGetProperty("skin", out _) && x.n.TryGetProperty("mesh", out _)).ToList();
        var skinsUsed = skinned.Select(x => x.n.GetProperty("skin").GetInt32()).Distinct().ToList();
        if (skinsUsed.Count > 1) { report.Error("multiple_skins", "The garment uses more than one skeleton. Join them into one."); return report; }
        var skin = gltf.At("skins", skinsUsed[0])!.Value;
        var joints = skin.TryGetProperty("joints", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(j => j.GetInt32()).ToArray() : [];
        report.Joints = joints.Length;
        if (joints.Length is 0) { report.Error("no_joints", "The skeleton of the garment has no bones."); return report; }
        if (joints.Length > MaxJoints) report.Error("too_many_joints", $"The garment has {joints.Length} bones; the limit is {MaxJoints}.");
        if (joints.Any(j => j < 0 || j >= parents.Length)) { report.Error("bad_hierarchy", "A bone points to a node that does not exist."); return report; }
        string NameOf(int node) => gltf.At("nodes", node)!.Value.TryGetProperty("name", out var n) ? n.GetString() ?? $"node{node}" : $"node{node}";
        var standardOf = joints.Select(j => BoneMapper.Map(NameOf(j))).ToArray();

        // ---- weights: which bones really move the cloth
        var used = new double[joints.Length]; long weightedVertices = 0; var meshes = skinned.Select(x => x.n.GetProperty("mesh").GetInt32()).Distinct().ToHashSet(); var meshNumber = 0;
        long triangles = 0, vertices = 0;
        foreach (var mesh in gltf.Items("meshes"))
        {
            var isSkinned = meshes.Contains(meshNumber++);
            if (!mesh.TryGetProperty("primitives", out var primitives)) continue;
            foreach (var primitive in primitives.EnumerateArray())
            {
                if (!primitive.TryGetProperty("attributes", out var attributes) || !attributes.TryGetProperty("POSITION", out var positionIndex) || gltf.Accessor(positionIndex.GetInt32()) is not { } info) { report.Error("bad_primitive", "A mesh has no positions."); continue; }
                vertices += info.Count; long indices = primitive.TryGetProperty("indices", out var ix) && gltf.Accessor(ix.GetInt32()) is { } ii ? ii.Count : info.Count; triangles += indices / 3;
                if (!isSkinned) { report.Warn("rigid_mesh", "A part of the garment is not bound to the skeleton; it will not bend with the body."); continue; }
                if (!attributes.TryGetProperty("JOINTS_0", out var jointsIndex) || !attributes.TryGetProperty("WEIGHTS_0", out var weightsIndex)) { report.Error("unskinned_mesh", "A garment mesh has no bone weights (JOINTS_0 and WEIGHTS_0)."); continue; }
                if (info.Count > MaxVertices) continue;
                var jv = gltf.ReadFloats(jointsIndex.GetInt32()); var wv = gltf.ReadFloats(weightsIndex.GetInt32());
                if (jv is null || wv is null || jv.Length != info.Count * 4 || wv.Length != info.Count * 4) { report.Error("bad_skinning", "The bone indices or weights cannot be read."); continue; }
                var badSum = 0; var badIndex = 0;
                for (var v = 0; v < info.Count; v++)
                {
                    float sum = 0;
                    for (var k = 0; k < 4; k++)
                    {
                        var w = wv[v * 4 + k]; sum += w; if (w <= 0) continue;
                        var j = (int)jv[v * 4 + k]; if (j < 0 || j >= joints.Length) { badIndex++; continue; }
                        used[j] += w;
                    }
                    if (Math.Abs(sum - 1f) > .03f) badSum++;
                    weightedVertices++;
                }
                if (badIndex > 0) report.Error("bad_joint_index", $"{badIndex} vertex weights point to a bone that is not in the skeleton.");
                if (badSum > Math.Max(1, info.Count / 100)) report.Error("bad_weights", $"{badSum} vertices have bone weights that do not add up to 1.");
            }
        }
        report.Triangles = (int)Math.Min(triangles, int.MaxValue); report.Vertices = (int)Math.Min(vertices, int.MaxValue);
        Budgets(report);
        if (report.Triangles == 0) report.Error("no_triangles", "The garment has nothing to show.");
        if (!report.Ok) return report;

        var total = used.Sum(); if (total <= 0) { report.Error("bad_weights", "The garment has no bone weights at all."); return report; }
        var unknown = Enumerable.Range(0, joints.Length).Where(i => standardOf[i] is null && used[i] > total * .001).Select(i => NameOf(joints[i])).ToList();
        if (unknown.Count > 0) report.Error("incompatible_bones", $"The garment is weighted to bones that are not in the game's skeleton: {string.Join(", ", unknown.Take(8))}{(unknown.Count > 8 ? "…" : string.Empty)}. Weight it to the standard humanoid bones only.");
        var outside = 0d; var region = new HashSet<string>(Region(report.Slot)); foreach (var covered in report.Covers) region.UnionWith(Region(covered)); // a dress that covers the legs is weighted to them
        for (var i = 0; i < joints.Length; i++)
        {
            if (standardOf[i] is not { } standard) continue;
            if (used[i] > 0 && !report.Bones.Contains(standard)) report.Bones.Add(standard);
            if (!region.Contains(standard)) outside += used[i];
        }
        if (outside > total * .05) report.Error("wrong_region", $"{outside / total:P0} of the weight is on bones outside the area of a “{report.Slot}” ({string.Join(", ", report.Bones.Where(b => !region.Contains(b)))}). The garment looks made for another part of the body.");

        // ---- is the skeleton of the garment laid out like ours?
        var present = Enumerable.Range(0, joints.Length).Where(i => standardOf[i] is not null).GroupBy(i => standardOf[i]!).Select(g => (Bone: g.Key, Node: joints[g.First()])).ToList();
        if (present.Count >= 2)
        {
            var factors = new List<float>();
            for (var a = 0; a < present.Count; a++) for (var b = a + 1; b < present.Count; b++)
                { var mine = (world[present[a].Node].Translation - world[present[b].Node].Translation).Length(); var theirs = (Reference[present[a].Bone] - Reference[present[b].Bone]).Length(); if (theirs > .15f && mine > 1e-5f) factors.Add(mine / theirs); }
            if (factors.Count > 0)
            {
                factors.Sort(); var scale = factors[factors.Count / 2]; var anchor = present.First(p => p.Bone == "hips" || p.Bone == "spine" || p.Bone == "chest" || p.Bone == "head" || p.Bone == "neck");
                var origin = world[anchor.Node].Translation - Reference[anchor.Bone] * scale; var worst = 0f;
                foreach (var (bone, node) in present) worst = Math.Max(worst, (world[node].Translation - (origin + Reference[bone] * scale)).Length() / scale);
                report.FitDeviation = worst;
                if (Math.Abs(scale - 1f) > .2f) report.Warn("scaled_skeleton", $"The skeleton of the garment is {scale:0.##}× the size of the standard one; it will be scaled to fit.");
                if (worst > MaxDeviation) report.Error("skeleton_mismatch", $"The skeleton of the garment is laid out very differently from the game's humanoid (bones up to {worst:0.00} m off). It was made for another body.");
                else if (worst > WarnDeviation) report.Warn("loose_fit", $"The bones of the garment are up to {worst:0.00} m from the standard ones; it may fit some bodies badly.");
            }
        }
        // the chain of the bones that exist must be a humanoid chain
        bool Under(int node, int ancestor) { for (var p = parents[node]; p >= 0; p = parents[p]) if (p == ancestor) return true; return false; }
        var nodeOf = present.ToDictionary(p => p.Bone, p => p.Node);
        foreach (var (child, above) in new[] { ("spine", "hips"), ("chest", "spine"), ("neck", "chest"), ("head", "neck"), ("lower_arm_l", "upper_arm_l"), ("hand_l", "lower_arm_l"), ("lower_arm_r", "upper_arm_r"), ("hand_r", "lower_arm_r"), ("lower_leg_l", "upper_leg_l"), ("foot_l", "lower_leg_l"), ("lower_leg_r", "upper_leg_r"), ("foot_r", "lower_leg_r") })
            if (nodeOf.TryGetValue(child, out var c) && nodeOf.TryGetValue(above, out var a) && !Under(c, a)) report.Error("bad_hierarchy", $"The bone “{NameOf(c)}” ({child}) should be under “{NameOf(a)}” ({above}).");
        if (gltf.Count("animations") > 0) report.Warn("animations_ignored", "The animations inside the file are ignored.");
        return report;
    }
}

/// <summary>Small valid garments and rigid pieces for the tests and as examples for the people who make them.</summary>
public static class WearableTemplate
{
    public static string Extras(string slot, string? layer = null, string[]? covers = null, string? bone = null, bool hidesHair = false, float[]? offset = null, float scale = 1f, string? tint = null)
    {
        var parts = new List<string> { $"\"slot\":\"{slot}\"" };
        if (layer is not null) parts.Add($"\"layer\":\"{layer}\"");
        if (covers is not null) parts.Add("\"covers\":[" + string.Join(",", covers.Select(c => $"\"{c}\"")) + "]");
        if (bone is not null) parts.Add($"\"bone\":\"{bone}\"");
        if (hidesHair) parts.Add("\"hidesHair\":true");
        if (tint is not null) parts.Add($"\"tint\":\"{tint}\"");
        if (offset is not null) parts.Add("\"offset\":[" + string.Join(",", offset.Select(o => o.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]");
        if (scale != 1f) parts.Add($"\"scale\":{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        return "{\"lifeverse\":{" + string.Join(",", parts) + "}}";
    }

    /// <summary>Rigged clothing: the standard skeleton, with boxes only around the given bones, weighted to them.</summary>
    public static byte[] Clothing(string slot, string[] boneBoxes, string? extras = null, TemplateOptions? options = null) =>
        HumanoidTemplate.Build((options ?? new TemplateOptions()) with { BoxBones = boneBoxes, Extras = extras ?? Extras(slot) });

    public static readonly string[] ShirtBones = ["spine", "chest", "upper_arm_l", "lower_arm_l", "upper_arm_r", "lower_arm_r"];
    public static readonly string[] TrouserBones = ["hips", "upper_leg_l", "lower_leg_l", "upper_leg_r", "lower_leg_r"];

    /// <summary>A rigid piece: one box, no skeleton, with the extras saying where it goes.</summary>
    public static byte[] Attachment(string extras, float size = .2f, float? declaredHalf = null)
    {
        var h = declaredHalf ?? size / 2;
        float[] positions = [-h, -h, -h, h, -h, -h, -h, h, -h, h, h, -h, -h, -h, h, h, -h, h, -h, h, h, h, h, h];
        ushort[] indices = [0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3];
        var bin = new List<byte>(); foreach (var f in positions) bin.AddRange(BitConverter.GetBytes(f)); var indexOffset = bin.Count; foreach (var i in indices) bin.AddRange(BitConverter.GetBytes(i)); while (bin.Count % 4 != 0) bin.Add(0);
        var root = new Dictionary<string, object?>
        {
            ["asset"] = new Dictionary<string, object?> { ["version"] = "2.0", ["extras"] = JsonSerializer.Deserialize<JsonElement>(extras) },
            ["scene"] = 0, ["scenes"] = new[] { new { nodes = new[] { 0 } } }, ["nodes"] = new[] { new { name = "Piece", mesh = 0 } },
            ["meshes"] = new[] { new { primitives = new[] { new { attributes = new Dictionary<string, int> { ["POSITION"] = 0 }, indices = 1 } } } },
            ["accessors"] = new object[] { new { bufferView = 0, componentType = 5126, count = 8, type = "VEC3", min = new[] { -h, -h, -h }, max = new[] { h, h, h } }, new { bufferView = 1, componentType = 5123, count = indices.Length, type = "SCALAR" } },
            ["bufferViews"] = new object[] { new { buffer = 0, byteOffset = 0, byteLength = 96 }, new { buffer = 0, byteOffset = indexOffset, byteLength = indices.Length * 2 } },
            ["buffers"] = new[] { new { byteLength = bin.Count } }
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(root); var jsonPadded = new byte[(json.Length + 3) & ~3]; Array.Fill(jsonPadded, (byte)' '); json.CopyTo(jsonPadded, 0);
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write(0x46546C67u); writer.Write(2u); writer.Write((uint)(12 + 8 + jsonPadded.Length + 8 + bin.Count));
        writer.Write((uint)jsonPadded.Length); writer.Write(0x4E4F534Au); writer.Write(jsonPadded); writer.Write((uint)bin.Count); writer.Write(0x004E4942u); writer.Write(bin.ToArray());
        return output.ToArray();
    }
}
