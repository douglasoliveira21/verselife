using System.Text;
using System.Text.Json;

namespace LifeVerse.Shared.Avatar;

public enum BoneNaming { Blender, Mixamo, Standard }
public enum SkinningFault { None, NoWeights, BadSum, BadIndex }

/// <summary>One animation to put in a template: for each track a standard bone turns about an axis (0 x, 1 y, 2 z) by an angle (radians) at the middle of the clip and back; the hips can also bob.</summary>
public sealed record AnimationSpec(string Name, float Duration, (string Bone, int Axis, float Angle)[] Tracks, float HipsBob = 0f, float HipsTravel = 0f);

/// <summary>What a template should look like; the defaults are a clean model every check passes.</summary>
public sealed record TemplateOptions
{
    public BoneNaming Naming { get; init; } = BoneNaming.Blender;
    /// <summary>Multiplies every length (100 makes a model in centimetres).</summary>
    public float Scale { get; init; } = 1f;
    /// <summary>A standard bone that is left out of the skeleton (its box follows the parent).</summary>
    public string? OmitBone { get; init; }
    public SkinningFault Fault { get; init; } = SkinningFault.None;
    /// <summary>Turns the model upside down (head below the feet).</summary>
    public bool UpsideDown { get; init; }
    /// <summary>How many copies of the body boxes to put in the mesh (to go over the triangle budget).</summary>
    public int Copies { get; init; } = 1;
    /// <summary>When set, only the boxes of these bones are in the mesh (a garment covers part of the body); the skeleton stays complete.</summary>
    public string[]? BoxBones { get; init; }
    /// <summary>Raw JSON stored in the asset extras (where garments say which slot they are for).</summary>
    public string? Extras { get; init; }
    /// <summary>Adds a texture that points to another file.</summary>
    public bool ExternalTexture { get; init; }
    /// <summary>Adds an (empty) animation, which the game ignores.</summary>
    public bool WithAnimation { get; init; }
    /// <summary>Real animations (keyframes on the bones) to put in the file.</summary>
    public IReadOnlyList<AnimationSpec>? Animations { get; init; }
    /// <summary>Adds a required extension the game does not support.</summary>
    public string? RequiredExtension { get; init; }
    /// <summary>Makes the arms very long (to break the proportions).</summary>
    public float ArmStretch { get; init; } = 1f;
    /// <summary>Adds a second mesh that is not bound to the skeleton.</summary>
    public bool RigidMesh { get; init; }
    /// <summary>Puts the left hand under the right arm (a broken hierarchy).</summary>
    public bool BrokenArm { get; init; }
}

/// <summary>
/// A small valid humanoid in GLB form: the standard skeleton in a T-pose, 1.78 m tall, with a box around every bone weighted to it. It is the
/// reference for people who make avatars (it shows the bones, the pose and the proportions the game expects) and the sample the tests and the
/// client smoke use. The options bend it in one specific way at a time so every check has a model to fail on.
/// </summary>
public static class HumanoidTemplate
{
    private sealed record Bone(string Standard, string? Parent, float X, float Y, float Z);

    private static readonly Bone[] Bones =
    [
        new("hips", null, 0, .95f, 0), new("spine", "hips", 0, .12f, 0), new("chest", "spine", 0, .2f, 0), new("neck", "chest", 0, .18f, 0), new("head", "neck", 0, .1f, 0),
        new("upper_arm_l", "chest", .18f, .1f, 0), new("lower_arm_l", "upper_arm_l", .28f, 0, 0), new("hand_l", "lower_arm_l", .25f, 0, 0),
        new("upper_arm_r", "chest", -.18f, .1f, 0), new("lower_arm_r", "upper_arm_r", -.28f, 0, 0), new("hand_r", "lower_arm_r", -.25f, 0, 0),
        new("upper_leg_l", "hips", .09f, -.05f, 0), new("lower_leg_l", "upper_leg_l", 0, -.42f, 0), new("foot_l", "lower_leg_l", 0, -.42f, 0),
        new("upper_leg_r", "hips", -.09f, -.05f, 0), new("lower_leg_r", "upper_leg_r", 0, -.42f, 0), new("foot_r", "lower_leg_r", 0, -.42f, 0)
    ];

    // centre and half size of the box around each bone, in metres at rest
    private static readonly Dictionary<string, (float Cx, float Cy, float Cz, float Hx, float Hy, float Hz)> Boxes = new()
    {
        ["hips"] = (0, .95f, 0, .17f, .09f, .1f), ["spine"] = (0, 1.1f, 0, .15f, .08f, .09f), ["chest"] = (0, 1.3f, 0, .19f, .12f, .1f), ["neck"] = (0, 1.47f, 0, .05f, .05f, .05f), ["head"] = (0, 1.65f, 0, .1f, .13f, .1f),
        ["upper_arm_l"] = (.32f, 1.37f, 0, .14f, .05f, .05f), ["lower_arm_l"] = (.585f, 1.37f, 0, .125f, .04f, .04f), ["hand_l"] = (.78f, 1.37f, 0, .07f, .04f, .04f),
        ["upper_arm_r"] = (-.32f, 1.37f, 0, .14f, .05f, .05f), ["lower_arm_r"] = (-.585f, 1.37f, 0, .125f, .04f, .04f), ["hand_r"] = (-.78f, 1.37f, 0, .07f, .04f, .04f),
        ["upper_leg_l"] = (.09f, .69f, 0, .07f, .21f, .07f), ["lower_leg_l"] = (.09f, .27f, 0, .06f, .21f, .06f), ["foot_l"] = (.09f, .03f, .05f, .05f, .03f, .12f),
        ["upper_leg_r"] = (-.09f, .69f, 0, .07f, .21f, .07f), ["lower_leg_r"] = (-.09f, .27f, 0, .06f, .21f, .06f), ["foot_r"] = (-.09f, .03f, .05f, .05f, .03f, .12f)
    };

    public static string NameOf(string standard, BoneNaming naming) => naming switch
    {
        BoneNaming.Standard => standard,
        BoneNaming.Mixamo => "mixamorig:" + standard switch
        {
            "hips" => "Hips", "spine" => "Spine", "chest" => "Spine1", "neck" => "Neck", "head" => "Head",
            "upper_arm_l" => "LeftArm", "lower_arm_l" => "LeftForeArm", "hand_l" => "LeftHand", "upper_arm_r" => "RightArm", "lower_arm_r" => "RightForeArm", "hand_r" => "RightHand",
            "upper_leg_l" => "LeftUpLeg", "lower_leg_l" => "LeftLeg", "foot_l" => "LeftFoot", "upper_leg_r" => "RightUpLeg", "lower_leg_r" => "RightLeg", _ => "RightFoot"
        },
        _ => standard switch
        {
            "hips" => "Hips", "spine" => "Spine", "chest" => "Chest", "neck" => "Neck", "head" => "Head",
            "upper_arm_l" => "UpperArm.L", "lower_arm_l" => "LowerArm.L", "hand_l" => "Hand.L", "upper_arm_r" => "UpperArm.R", "lower_arm_r" => "LowerArm.R", "hand_r" => "Hand.R",
            "upper_leg_l" => "UpperLeg.L", "lower_leg_l" => "LowerLeg.L", "foot_l" => "Foot.L", "upper_leg_r" => "UpperLeg.R", "lower_leg_r" => "LowerLeg.R", _ => "Foot.R"
        }
    };

    public static byte[] Build(TemplateOptions? options = null)
    {
        options ??= new TemplateOptions();
        var scale = options.Scale; var flip = options.UpsideDown ? -1f : 1f;
        var bones = Bones.Where(b => b.Standard != options.OmitBone).ToList();
        var indexOf = bones.Select((b, i) => (b.Standard, i)).ToDictionary(x => x.Standard, x => x.i);
        // node 0 is the armature, then one node per bone (so joint i is node i + 1), then the mesh nodes
        var world = new Dictionary<string, (float X, float Y, float Z)>();
        foreach (var bone in bones)
        {
            var parent = bone.Parent is not null && world.TryGetValue(bone.Parent, out var p) ? p : (0, 0, 0);
            var stretch = bone.Standard is "lower_arm_l" or "hand_l" or "lower_arm_r" or "hand_r" ? options.ArmStretch : 1f;
            world[bone.Standard] = (parent.X + bone.X * scale * stretch, parent.Y + bone.Y * scale * flip, parent.Z + bone.Z * scale);
        }

        // ---- geometry: a box per bone, weighted to the bone (or to the parent when the bone was left out)
        var positions = new List<float>(); var joints = new List<byte>(); var weights = new List<float>(); var indices = new List<ushort>();
        string Owner(string standard) { var s = standard; while (!indexOf.ContainsKey(s)) s = Bones.First(b => b.Standard == s).Parent!; return s; }
        for (var copy = 0; copy < Math.Max(1, options.Copies); copy++)
            foreach (var (standard, box) in Boxes.Where(b => options.BoxBones is null || options.BoxBones.Contains(b.Key)))
            {
                var owner = Owner(standard); var j = (byte)indexOf[owner]; var baseIndex = positions.Count / 3;
                var stretchX = standard is "lower_arm_l" or "hand_l" or "lower_arm_r" or "hand_r" ? options.ArmStretch : 1f;
                for (var corner = 0; corner < 8; corner++)
                {
                    var sx = (corner & 1) == 0 ? -1 : 1; var sy = (corner & 2) == 0 ? -1 : 1; var sz = (corner & 4) == 0 ? -1 : 1;
                    var cx = box.Cx * (stretchX > 1 ? stretchX : 1) ;
                    positions.Add((cx + sx * box.Hx) * scale); positions.Add((box.Cy + sy * box.Hy) * scale * flip); positions.Add((box.Cz + sz * box.Hz) * scale);
                    var weight = options.Fault == SkinningFault.BadSum ? .5f : 1f;
                    if (options.Fault == SkinningFault.NoWeights) { joints.AddRange([0, 0, 0, 0]); weights.AddRange([0f, 0f, 0f, 0f]); }
                    else { joints.AddRange([options.Fault == SkinningFault.BadIndex ? (byte)200 : j, 0, 0, 0]); weights.AddRange([weight, 0f, 0f, 0f]); }
                }
                foreach (var face in new[] { 0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3 }) indices.Add((ushort)(baseIndex + face));
            }

        // ---- binary buffer
        var bin = new List<byte>(); var views = new List<object>(); var accessors = new List<object>();
        int AddView(byte[] data, int? target = null) { while (bin.Count % 4 != 0) bin.Add(0); var offset = bin.Count; bin.AddRange(data); views.Add(target is null ? new { buffer = 0, byteOffset = offset, byteLength = data.Length } : new { buffer = 0, byteOffset = offset, byteLength = data.Length, target }); return views.Count - 1; }
        int AddAccessor(int view, int componentType, int count, string type, float[]? min = null, float[]? max = null, bool normalized = false)
        {
            accessors.Add(min is null ? new { bufferView = view, componentType, count, type, normalized } : new { bufferView = view, componentType, count, type, min, max, normalized }); return accessors.Count - 1;
        }
        byte[] Bytes(IEnumerable<float> values) => [.. values.SelectMany(BitConverter.GetBytes)];
        var vertexCount = positions.Count / 3;
        float[] Min(int axis) => [positions.Where((_, i) => i % 3 == axis).Min()]; float[] Max(int axis) => [positions.Where((_, i) => i % 3 == axis).Max()];
        var accessorPosition = AddAccessor(AddView(Bytes(positions), 34962), 5126, vertexCount, "VEC3", [Min(0)[0], Min(1)[0], Min(2)[0]], [Max(0)[0], Max(1)[0], Max(2)[0]]);
        var accessorJoints = AddAccessor(AddView([.. joints], 34962), 5121, vertexCount, "VEC4");
        var accessorWeights = AddAccessor(AddView(Bytes(weights), 34962), 5126, vertexCount, "VEC4");
        var accessorIndices = AddAccessor(AddView([.. indices.SelectMany(BitConverter.GetBytes)], 34963), 5123, indices.Count, "SCALAR");
        var inverse = new List<float>();
        foreach (var bone in bones) { var w = world[bone.Standard]; inverse.AddRange([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -w.X, -w.Y, -w.Z, 1]); }
        var accessorInverse = AddAccessor(AddView(Bytes(inverse)), 5126, bones.Count, "MAT4");
        var animationList = new List<object>();
        foreach (var spec in options.Animations ?? [])
        {
            var times = new[] { 0f, spec.Duration / 2, spec.Duration }; var timeAccessor = AddAccessor(AddView(Bytes(times)), 5126, 3, "SCALAR", [0f], [spec.Duration]);
            var samplers = new List<object>(); var channels = new List<object>();
            foreach (var (bone, axis, angle) in spec.Tracks)
            {
                if (!indexOf.TryGetValue(bone, out var boneIndex)) continue;
                var values = new List<float>();
                foreach (var a in new[] { 0f, angle, 0f }) { var half = a / 2; var q = new float[] { axis == 0 ? MathF.Sin(half) : 0, axis == 1 ? MathF.Sin(half) : 0, axis == 2 ? MathF.Sin(half) : 0, MathF.Cos(half) }; values.AddRange(q); }
                samplers.Add(new { input = timeAccessor, output = AddAccessor(AddView(Bytes(values)), 5126, 3, "VEC4"), interpolation = "LINEAR" });
                channels.Add(new { sampler = samplers.Count - 1, target = new { node = boneIndex + 1, path = "rotation" } });
            }
            if (spec.HipsBob != 0 || spec.HipsTravel != 0)
            {
                var hips = world["hips"]; var values = new List<float>();
                foreach (var f in new[] { 0f, 1f, 0f }) values.AddRange([hips.X, hips.Y + spec.HipsBob * f, hips.Z + spec.HipsTravel * f]);
                samplers.Add(new { input = timeAccessor, output = AddAccessor(AddView(Bytes(values)), 5126, 3, "VEC3"), interpolation = "LINEAR" });
                channels.Add(new { sampler = samplers.Count - 1, target = new { node = indexOf["hips"] + 1, path = "translation" } });
            }
            animationList.Add(new { name = spec.Name, channels, samplers });
        }
        int accessorRigid = -1, accessorRigidIndices = -1;
        if (options.RigidMesh)
        {
            float[] quad = [-.2f, 0, 0, .2f, 0, 0, 0, .3f, 0];
            accessorRigid = AddAccessor(AddView(Bytes(quad), 34962), 5126, 3, "VEC3", [-.2f, 0, 0], [.2f, .3f, 0]);
            accessorRigidIndices = AddAccessor(AddView([.. new ushort[] { 0, 1, 2 }.SelectMany(BitConverter.GetBytes)], 34963), 5123, 3, "SCALAR");
        }
        while (bin.Count % 4 != 0) bin.Add(0);

        // ---- nodes: armature, one per bone, the skinned mesh and the optional rigid mesh
        var nodes = new List<object> { new { name = "Armature", children = new[] { 1 } } };
        var nodeIndex = bones.Select((b, i) => (b.Standard, i)).ToDictionary(x => x.Standard, x => x.i + 1);
        foreach (var bone in bones)
        {
            var parent = bone.Parent is not null && world.TryGetValue(bone.Parent, out var p) ? p : (0, 0, 0); var w = world[bone.Standard];
            var kids = bones.Where(b => b.Parent == bone.Standard).Select(b => nodeIndex[b.Standard]).ToList();
            if (options.BrokenArm && bone.Standard == "hand_l") { }
            var children = kids.Count == 0 ? null : kids.ToArray();
            nodes.Add(new Dictionary<string, object?> { ["name"] = NameOf(bone.Standard, options.Naming), ["translation"] = new[] { w.X - parent.X, w.Y - parent.Y, w.Z - parent.Z }, ["children"] = children });
        }
        if (options.BrokenArm) // move the left hand to the right arm: hand_l becomes a child of lower_arm_r
        {
            var left = (Dictionary<string, object?>)nodes[nodeIndex["hand_l"]]; var oldParent = (Dictionary<string, object?>)nodes[nodeIndex["lower_arm_l"]]; var newParent = (Dictionary<string, object?>)nodes[nodeIndex["lower_arm_r"]];
            oldParent["children"] = null; newParent["children"] = new[] { nodeIndex["hand_r"], nodeIndex["hand_l"] };
        }
        var meshNode = nodes.Count; nodes.Add(new { name = "Body", mesh = 0, skin = 0 });
        var rootChildren = new List<int> { 1, meshNode };
        if (options.RigidMesh) { rootChildren.Add(nodes.Count); nodes.Add(new { name = "Prop", mesh = 1 }); }
        nodes[0] = new { name = "Armature", children = rootChildren.ToArray() };

        var meshes = new List<object> { new { name = "Body", primitives = new[] { new { attributes = new Dictionary<string, int> { ["POSITION"] = accessorPosition, ["JOINTS_0"] = accessorJoints, ["WEIGHTS_0"] = accessorWeights }, indices = accessorIndices, material = 0 } } } };
        if (options.RigidMesh) meshes.Add(new { name = "Prop", primitives = new[] { new { attributes = new Dictionary<string, int> { ["POSITION"] = accessorRigid }, indices = accessorRigidIndices, material = 0 } } });
        if (options.Fault == SkinningFault.NoWeights) { /* the attributes stay, but all weights are zero: the check catches the sums */ }

        var root = new Dictionary<string, object?>
        {
            ["asset"] = new Dictionary<string, object?> { ["version"] = "2.0", ["generator"] = "LifeVerse humanoid template", ["extras"] = options.Extras is null ? null : JsonSerializer.Deserialize<JsonElement>(options.Extras) },
            ["scene"] = 0, ["scenes"] = new[] { new { nodes = new[] { 0 } } }, ["nodes"] = nodes, ["meshes"] = meshes,
            ["skins"] = new[] { new { name = "Skeleton", joints = bones.Select(b => nodeIndex[b.Standard]).ToArray(), inverseBindMatrices = accessorInverse, skeleton = 1 } },
            ["materials"] = new[] { new { name = "Skin", pbrMetallicRoughness = new { baseColorFactor = new[] { .8f, .6f, .5f, 1f }, metallicFactor = 0f, roughnessFactor = .8f } } },
            ["accessors"] = accessors, ["bufferViews"] = views, ["buffers"] = new[] { new { byteLength = bin.Count } }
        };
        if (options.ExternalTexture) { root["images"] = new[] { new { uri = "http://example.com/skin.png" } }; }
        if (animationList.Count > 0) root["animations"] = animationList;
        else if (options.WithAnimation) root["animations"] = new[] { new { name = "Idle", channels = Array.Empty<object>(), samplers = Array.Empty<object>() } };
        if (options.RequiredExtension is { } extension) { root["extensionsUsed"] = new[] { extension }; root["extensionsRequired"] = new[] { extension }; }

        var json = JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        var jsonPadded = Pad(json, (byte)' '); var binPadded = Pad([.. bin], 0);
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write(0x46546C67u); writer.Write(2u); writer.Write((uint)(12 + 8 + jsonPadded.Length + 8 + binPadded.Length));
        writer.Write((uint)jsonPadded.Length); writer.Write(0x4E4F534Au); writer.Write(jsonPadded);
        writer.Write((uint)binPadded.Length); writer.Write(0x004E4942u); writer.Write(binPadded);
        return output.ToArray();
    }

    private static byte[] Pad(byte[] data, byte fill) { var padded = new byte[(data.Length + 3) & ~3]; Array.Fill(padded, fill); data.CopyTo(padded, 0); return padded; }
}
