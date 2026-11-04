using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LifeVerse.Shared.Avatar;

/// <summary>Maps the bone names that modelling tools produce (Blender, Mixamo, VRM, 3ds Max Biped, Unity) to the standard skeleton of the game.</summary>
public static partial class BoneMapper
{
    public static readonly string[] Standard =
    [
        "root", "hips", "spine", "chest", "neck", "head",
        "upper_arm_l", "lower_arm_l", "hand_l", "upper_arm_r", "lower_arm_r", "hand_r",
        "upper_leg_l", "lower_leg_l", "foot_l", "upper_leg_r", "lower_leg_r", "foot_r"
    ];

    /// <summary>The bones a model must have to be used as an avatar. "chest" is optional (the spine stands in), "root" too.</summary>
    public static readonly string[] Required =
    [
        "hips", "spine", "neck", "head", "upper_arm_l", "lower_arm_l", "hand_l", "upper_arm_r", "lower_arm_r", "hand_r",
        "upper_leg_l", "lower_leg_l", "foot_l", "upper_leg_r", "lower_leg_r", "foot_r"
    ];

    private static readonly HashSet<string> Noise = ["j", "bip", "bip01", "bip001", "mixamorig", "armature", "cc", "base", "def", "deform", "rig", "skel", "skeleton", "bone", "c"];

    [GeneratedRegex("[A-Z]?[a-z]+|[A-Z]+(?![a-z])|[0-9]+")]
    private static partial Regex Words();

    /// <summary>The standard bone this name stands for, or null when it is not one of the bones the game uses (fingers, toes, twist bones…).</summary>
    public static string? Map(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var side = ' '; var core = new List<string>();
        foreach (Match match in Words().Matches(name))
        {
            var word = match.Value.ToLowerInvariant();
            if (Noise.Contains(word)) continue;
            if (word is "left" or "l") { if (side == ' ') side = 'l'; continue; }
            if (word is "right" or "r") { if (side == ' ') side = 'r'; continue; }
            core.Add(word);
        }
        // a number glued to a word ("spine1") is a different bone from "spine"; "upper" and "arm" apart or together mean the same
        var text = string.Concat(core);
        string? Sided(string bone) => side == ' ' ? null : $"{bone}_{side}";
        switch (text)
        {
            case "hips" or "hip" or "pelvis": return "hips";
            case "root": return "root";
            case "spine" or "spine0" or "spine01" or "spine001" or "abdomen": return "spine";
            case "chest" or "spine1" or "spine02" or "spine002": return "chest";
            case "neck" or "neck1" or "neck01": return "neck";
            case "head": return "head";
            case "upperarm" or "uparm" or "arm" or "armupper": return Sided("upper_arm");
            case "lowerarm" or "forearm" or "foretarm" or "armlower": return Sided("lower_arm");
            case "hand" or "wrist": return Sided("hand");
            case "upperleg" or "upleg" or "thigh" or "legupper": return Sided("upper_leg");
            case "lowerleg" or "calf" or "shin" or "leg" or "leglower": return Sided("lower_leg");
            case "foot" or "ankle": return Sided("foot");
            default: return null;
        }
    }
}

public sealed record ModelIssue(string Code, string Message, bool IsError);

/// <summary>Everything the check of an avatar model found out: problems, the bone map and the numbers the game needs to scale it.</summary>
public sealed class AvatarModelReport
{
    public List<ModelIssue> Issues { get; } = [];
    public IEnumerable<ModelIssue> Errors => Issues.Where(i => i.IsError);
    public IEnumerable<ModelIssue> Warnings => Issues.Where(i => !i.IsError);
    public bool Ok => !Issues.Any(i => i.IsError);
    /// <summary>Standard bone name → the name of the node in the model.</summary>
    public Dictionary<string, string> BoneMap { get; } = [];
    public int SizeBytes { get; set; }
    public int Joints { get; set; }
    public int Vertices { get; set; }
    public int Triangles { get; set; }
    public int Materials { get; set; }
    public int Images { get; set; }
    /// <summary>Height of the model in the units of the file (metres when it was exported correctly).</summary>
    public float Height { get; set; }
    /// <summary>The factor that brings the model to the standard avatar height (1.8 m).</summary>
    public float ScaleToStandard { get; set; } = 1f;
    public float LegRatio { get; set; }
    public float ArmRatio { get; set; }
    public float ShoulderRatio { get; set; }

    public void Error(string code, string message) => Issues.Add(new ModelIssue(code, message, true));
    public void Warn(string code, string message) => Issues.Add(new ModelIssue(code, message, false));

    public string ToJson() => JsonSerializer.Serialize(new
    {
        ok = Ok, sizeBytes = SizeBytes, joints = Joints, vertices = Vertices, triangles = Triangles, materials = Materials, images = Images,
        height = Height, scaleToStandard = ScaleToStandard, legRatio = LegRatio, armRatio = ArmRatio, shoulderRatio = ShoulderRatio, boneMap = BoneMap,
        issues = Issues.Select(i => new { code = i.Code, message = i.Message, error = i.IsError })
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

/// <summary>
/// Decides whether a GLB or glTF file can be an avatar: a readable glTF 2.0 with everything inside it, one skin whose joints include the bones of a
/// humanoid in the right hierarchy, upright and with human proportions, every mesh skinned with valid joint indices and weights, and sizes within
/// the budgets that keep other players' computers fast. It never changes the file; it only reports.
/// </summary>
public static class AvatarModelValidator
{
    public const int MaxBytes = 20 * 1024 * 1024;
    public const int MaxTriangles = 60_000;
    public const int MaxVertices = 90_000;
    public const int MaxJoints = 128;
    public const int MaxMaterials = 16;
    public const int MaxImages = 8;
    public const float StandardHeight = 1.8f;

    private static readonly HashSet<string> AllowedRequired = ["KHR_materials_unlit", "KHR_materials_emissive_strength", "KHR_texture_transform", "KHR_materials_specular", "KHR_materials_ior", "KHR_materials_clearcoat", "KHR_texture_basisu"];

    public static AvatarModelReport Validate(byte[] bytes, bool binary)
    {
        var report = new AvatarModelReport { SizeBytes = bytes.Length };
        if (bytes.Length == 0) { report.Error("empty", "The file is empty."); return report; }
        if (bytes.Length > MaxBytes) { report.Error("too_big", $"The file is larger than {MaxBytes / 1024 / 1024} MB."); return report; }
        using var gltf = GltfReader.Read(bytes, binary, out var code, out var message);
        if (gltf is null) { report.Error(code, message); return report; }

        foreach (var extension in gltf.Root.TryGetProperty("extensionsRequired", out var required) && required.ValueKind == JsonValueKind.Array ? required.EnumerateArray().Select(e => e.GetString() ?? string.Empty) : [])
            if (!AllowedRequired.Contains(extension)) { report.Error("unsupported_extension", $"The model needs the extension {extension}, which the game does not support (for example compressed meshes)."); }
        foreach (var image in gltf.Items("images")) if (image.TryGetProperty("uri", out var uri) && !(uri.GetString() ?? string.Empty).StartsWith("data:", StringComparison.OrdinalIgnoreCase)) { report.Error("external_reference", "A texture points to another file. Export the model with everything embedded (a single .glb)."); break; }
        report.Materials = gltf.Count("materials"); report.Images = gltf.Count("images");
        if (report.Materials > MaxMaterials) report.Error("too_many_materials", $"The model has {report.Materials} materials; the limit is {MaxMaterials}.");
        if (report.Images > MaxImages) report.Error("too_many_textures", $"The model has {report.Images} textures; the limit is {MaxImages}.");
        if (gltf.Count("animations") > 0) report.Warn("animations_ignored", "The animations inside the model are ignored; the avatar uses the game's own animations.");

        if (gltf.Hierarchy() is not { } hierarchy) { report.Error("bad_hierarchy", "The nodes of the model are not a valid tree."); return report; }
        var (parents, world) = hierarchy;

        // ---- the skin and the bones
        var skinCount = gltf.Count("skins");
        if (skinCount == 0) { report.Error("no_skin", "The model has no skeleton (skin). Only rigged humanoids can be avatars."); return report; }
        var skinned = gltf.Items("nodes").Select((n, i) => (n, i)).Where(x => x.n.TryGetProperty("skin", out _) && x.n.TryGetProperty("mesh", out _)).ToList();
        var skinsUsed = skinned.Select(x => x.n.GetProperty("skin").GetInt32()).Distinct().ToList();
        if (skinsUsed.Count == 0) { report.Error("no_skinned_mesh", "No mesh uses the skeleton."); return report; }
        if (skinsUsed.Count > 1) { report.Error("multiple_skins", "The model uses more than one skeleton. Join them into one."); return report; }
        var skin = gltf.At("skins", skinsUsed[0])!.Value;
        var joints = skin.TryGetProperty("joints", out var jointList) && jointList.ValueKind == JsonValueKind.Array ? jointList.EnumerateArray().Select(j => j.GetInt32()).ToArray() : [];
        report.Joints = joints.Length;
        if (joints.Length == 0) { report.Error("no_joints", "The skeleton has no bones."); return report; }
        if (joints.Length > MaxJoints) report.Error("too_many_joints", $"The skeleton has {joints.Length} bones; the limit is {MaxJoints}.");
        if (joints.Any(j => j < 0 || j >= parents.Length)) { report.Error("bad_hierarchy", "A bone points to a node that does not exist."); return report; }

        var nameOf = (int node) => gltf.At("nodes", node)!.Value.TryGetProperty("name", out var n) ? n.GetString() ?? $"node{node}" : $"node{node}";
        var nodeOfBone = new Dictionary<string, int>();
        foreach (var joint in joints)
        {
            if (BoneMapper.Map(nameOf(joint)) is not { } bone) continue;
            if (nodeOfBone.ContainsKey(bone)) { report.Warn("duplicate_bone", $"Two bones look like “{bone}”; the first one is used ({nameOf(nodeOfBone[bone])})."); continue; }
            nodeOfBone[bone] = joint;
        }
        if (!nodeOfBone.ContainsKey("chest") && nodeOfBone.TryGetValue("spine", out var spineNode)) { nodeOfBone["chest"] = spineNode; report.Warn("no_chest", "There is no chest bone; the spine bone stands in for it."); }
        var missing = BoneMapper.Required.Where(b => !nodeOfBone.ContainsKey(b)).ToList();
        foreach (var pair in nodeOfBone) report.BoneMap[pair.Key] = nameOf(pair.Value);
        if (missing.Count > 0) { report.Error("missing_bones", $"The skeleton is missing these bones (or names the game cannot recognize): {string.Join(", ", missing)}."); return report; }

        // ---- the hierarchy of a humanoid
        bool Under(int node, int ancestor) { for (var p = parents[node]; p >= 0; p = parents[p]) if (p == ancestor) return true; return false; }
        void Chain(string bone, string above) { if (!Under(nodeOfBone[bone], nodeOfBone[above])) report.Error("bad_hierarchy", $"The bone “{report.BoneMap[bone]}” ({bone}) should be under “{report.BoneMap[above]}” ({above}) in the skeleton."); }
        Chain("spine", "hips"); if (nodeOfBone["chest"] != nodeOfBone["spine"]) Chain("chest", "spine"); Chain("neck", "chest"); Chain("head", "neck");
        foreach (var side in new[] { "l", "r" })
        {
            Chain($"upper_arm_{side}", "chest"); Chain($"lower_arm_{side}", $"upper_arm_{side}"); Chain($"hand_{side}", $"lower_arm_{side}");
            Chain($"upper_leg_{side}", "hips"); Chain($"lower_leg_{side}", $"upper_leg_{side}"); Chain($"foot_{side}", $"lower_leg_{side}");
        }
        if (!report.Ok) return report;

        // ---- the rest pose: upright, symmetric, human proportions
        Vector3 At(string bone) => world[nodeOfBone[bone]].Translation;
        if (!(At("head").Y > At("neck").Y && At("neck").Y > At("hips").Y && At("hips").Y > At("foot_l").Y && At("hips").Y > At("foot_r").Y))
            report.Error("not_upright", "The skeleton is not standing up along the Y axis (head above hips above feet). Export it Y-up.");

        // ---- meshes: skinning and budgets
        float minY = float.MaxValue, maxY = float.MinValue; long triangles = 0, vertices = 0; var skinnedMeshes = skinned.Select(x => x.n.GetProperty("mesh").GetInt32()).Distinct().ToHashSet();
        var meshIndex = 0;
        foreach (var mesh in gltf.Items("meshes"))
        {
            var isSkinned = skinnedMeshes.Contains(meshIndex++);
            if (!mesh.TryGetProperty("primitives", out var primitives) || primitives.ValueKind != JsonValueKind.Array) continue;
            foreach (var primitive in primitives.EnumerateArray())
            {
                var mode = primitive.TryGetProperty("mode", out var m) && m.TryGetInt32(out var md) ? md : 4; if (mode != 4) { report.Error("bad_primitive", "Only triangle meshes are supported."); continue; }
                if (!primitive.TryGetProperty("attributes", out var attributes) || !attributes.TryGetProperty("POSITION", out var positionIndex) || gltf.Accessor(positionIndex.GetInt32()) is not { } positionInfo) { report.Error("bad_primitive", "A mesh has no positions."); continue; }
                vertices += positionInfo.Count;
                long indices = primitive.TryGetProperty("indices", out var ix) && ix.TryGetInt32(out var ii) && gltf.Accessor(ii) is { } indexInfo ? indexInfo.Count : positionInfo.Count; triangles += indices / 3;
                if (!isSkinned) { report.Warn("rigid_mesh", "A mesh is not bound to the skeleton; it will not follow the body."); continue; }
                if (positionInfo.Min is { Length: 3 } lo && positionInfo.Max is { Length: 3 } hi) { minY = Math.Min(minY, lo[1]); maxY = Math.Max(maxY, hi[1]); }
                else if (gltf.ReadFloats(positionIndex.GetInt32()) is { } positions) for (var v = 1; v < positions.Length; v += 3) { minY = Math.Min(minY, positions[v]); maxY = Math.Max(maxY, positions[v]); }
                if (!attributes.TryGetProperty("JOINTS_0", out var jointsIndex) || !attributes.TryGetProperty("WEIGHTS_0", out var weightsIndex)) { report.Error("unskinned_mesh", "A mesh bound to the skeleton has no bone weights (JOINTS_0 and WEIGHTS_0)."); continue; }
                if (attributes.TryGetProperty("JOINTS_1", out _)) report.Warn("extra_influences", "Some vertices use more than 4 bones; only the 4 strongest are used.");
                if (positionInfo.Count > MaxVertices) continue; // the budget error below says it; do not read a huge mesh
                var jointValues = gltf.ReadFloats(jointsIndex.GetInt32()); var weightValues = gltf.ReadFloats(weightsIndex.GetInt32());
                if (jointValues is null || weightValues is null || jointValues.Length != positionInfo.Count * 4 || weightValues.Length != positionInfo.Count * 4) { report.Error("bad_skinning", "The bone indices or weights of a mesh cannot be read."); continue; }
                var badIndex = 0; var badWeights = 0;
                for (var v = 0; v < positionInfo.Count; v++)
                {
                    float sum = 0;
                    for (var k = 0; k < 4; k++)
                    {
                        var weight = weightValues[v * 4 + k]; sum += weight;
                        if (weight < -0.0001f || float.IsNaN(weight)) badWeights++;
                        if (weight > 0 && (jointValues[v * 4 + k] >= joints.Length || jointValues[v * 4 + k] < 0)) badIndex++;
                    }
                    if (Math.Abs(sum - 1f) > .03f) badWeights++;
                }
                if (badIndex > 0) report.Error("bad_joint_index", $"{badIndex} vertex weights point to a bone that is not in the skeleton.");
                if (badWeights > Math.Max(1, positionInfo.Count / 100)) report.Error("bad_weights", $"{badWeights} vertices have bone weights that do not add up to 1. Normalize the weights before exporting.");
                else if (badWeights > 0) report.Warn("loose_weights", $"{badWeights} vertices have bone weights that do not add up to 1.");
            }
        }
        report.Vertices = (int)Math.Min(vertices, int.MaxValue); report.Triangles = (int)Math.Min(triangles, int.MaxValue);
        if (report.Triangles > MaxTriangles) report.Error("too_many_triangles", $"The model has {report.Triangles:N0} triangles; the limit for an avatar is {MaxTriangles:N0}.");
        if (report.Vertices > MaxVertices) report.Error("too_many_vertices", $"The model has {report.Vertices:N0} vertices; the limit for an avatar is {MaxVertices:N0}.");
        if (report.Triangles == 0) report.Error("no_triangles", "The model has nothing to show.");

        if (minY < maxY)
        {
            report.Height = maxY - minY; report.ScaleToStandard = report.Height > 0.0001f ? StandardHeight / report.Height : 1f;
            if (report.Height is < .2f or > 6f) report.Warn("odd_size", $"The model is {report.Height:0.##} units tall (units may not be metres); it will be scaled to a standard height.");
            var legs = Math.Abs(At("hips").Y - Math.Min(At("foot_l").Y, At("foot_r").Y));
            var arm = (At("upper_arm_l") - At("hand_l")).Length();
            var shoulders = Math.Abs(At("upper_arm_l").X - At("upper_arm_r").X);
            report.LegRatio = legs / report.Height; report.ArmRatio = arm / report.Height; report.ShoulderRatio = shoulders / report.Height;
            if (report.LegRatio is < .25f or > .65f) report.Error("bad_proportions", $"The legs are {report.LegRatio:P0} of the height; a person is between 25% and 65%.");
            if (report.ArmRatio is < .15f or > .6f) report.Error("bad_proportions", $"The arms are {report.ArmRatio:P0} of the height; a person is between 15% and 60%.");
            if (report.ShoulderRatio is < .05f or > .5f) report.Error("bad_proportions", $"The shoulders are {report.ShoulderRatio:P0} of the height apart; a person is between 5% and 50%.");
            var mirror = Math.Abs(At("upper_arm_l").X + At("upper_arm_r").X); if (mirror > .25f * Math.Max(shoulders, .0001f)) report.Warn("asymmetric", "The left and right sides of the skeleton are not mirrored around the middle.");
        }
        else report.Error("no_bounds", "The size of the model could not be measured.");
        return report;
    }
}
