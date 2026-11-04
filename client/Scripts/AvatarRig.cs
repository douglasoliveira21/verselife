using Godot;
using LifeVerse.Client.Models;
using LifeVerse.Shared.Gameplay;
using System.Text.Json;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

/// <summary>Original segmented humanoid placeholder with a standard named skeleton.</summary>
public partial class AvatarRig : Node3D
{
    private static readonly string[] BoneNames =
    [
        "root", "hips", "spine", "chest", "neck", "head",
        "upper_arm_l", "lower_arm_l", "hand_l", "upper_arm_r", "lower_arm_r", "hand_r",
        "upper_leg_l", "lower_leg_l", "foot_l", "upper_leg_r", "lower_leg_r", "foot_r"
    ];
    private Skeleton3D _skeleton = null!;
    private Node3D _visual = null!;
    private VoiceIndicator? _voice;
    private bool _voiceActive;
    private AnimationTree _tree = null!;
    private AnimationNodeStateMachinePlayback _playback = null!;
    private AvatarMotionState _state = AvatarMotionState.Idle;
    private AvatarMotionState? _overrideState;
    private double _overrideUntil;
    private int _overridePriority;

    public override void _Ready()
    {
        BuildSkeleton();
        BuildAnimationSystem();
        ApplyAppearance(AvatarAppearanceDataFallback());
    }

    /// The voice dot and rings above the name (null until an appearance has been applied).
    public VoiceIndicator? Voice => _voice is not null && IsInstanceValid(_voice) ? _voice : null;

    /// Shows the dot while voice is on for this avatar and feeds the rings with the current loudness.
    public void SetVoice(bool active, float level, float peak)
    {
        _voiceActive = active;
        if (Voice is not { } indicator) return;
        indicator.Active = active;
        indicator.SetSignal(level, peak);
    }

    public void ApplyAppearance(AvatarAppearanceData data)
    {
        if (_visual is not null && IsInstanceValid(_visual))
        {
            RemoveChild(_visual);
            _visual.QueueFree();
        }
        _visual = new Node3D { Name = "Visual" };
        AddChild(_visual);
        var body = AvatarRules.ReadSettings(data.BodySettingsJson, AvatarRules.BodyRanges);
        var face = AvatarRules.ReadSettings(data.FaceSettingsJson, AvatarRules.FaceRanges);
        var scaleFactor = (data.BaseModel == "human_b" ? new Vector3(1.06f, 1.04f, 1.06f) : Vector3.One) * new Vector3(body["shoulder"], body["height"], body["shoulder"]);
        _visual.Scale = scaleFactor;
        var skin = Material(data.SkinColor);
        var hair = Material(data.HairColor);
        var top = Material(data.Tint("top", "#3AAFC1"));
        var bottom = Material(data.Tint("bottom", "#263B56"));
        var shoes = Material(data.Tint("shoes", "#E7E9EE"));
        var topItem = data.Item("top", "tee_basic");
        var bottomItem = data.Item("bottom", "pants_basic");

        var hips = Part(_visual, "Hips", new Vector3(0, 0.92f, 0), new Vector3(0.5f, 0.24f, 0.3f), bottom);
        var torso = Part(hips, "Torso", new Vector3(0, 0.48f, 0), new Vector3(0.62f, 0.72f, 0.32f), top);
        var neck = Part(torso, "Neck", new Vector3(0, 0.47f, 0), new Vector3(0.18f, 0.2f, 0.18f), skin);
        var head = Sphere(neck, "Head", new Vector3(0, 0.28f, 0), new Vector3(0.28f, 0.34f, 0.27f), skin);
        // an own pair of eyes or own hair replaces the catalog ones, but only where own pieces are drawn (close enough); far away the catalog ones stay
        var worn = data.EquippedItems.Select(x => (x.Slot, x.ItemKey)).ToList(); Func<string, string, WearableItem?>? drawn = _detail >= AvatarDetail.Full ? data.CustomItem : null;
        if (AvatarRules.EyesVisible(worn, drawn)) AddFace(head, data, face);
        AddFaceFeatures(head, face, data.SkinColor);
        if (AvatarRules.HairVisible(worn, drawn)) AddHair(head, data.HairStyle, hair);
        AddBeard(head, data.BeardStyle, hair);
        AddAccessory(head, data.Item("accessory", "none"), data.Tint("accessory", "#FFFFFF"));
        AddAttachments(_visual, data.AttachmentsJson);

        Limb(torso, "LeftUpperArm", "LeftLowerArm", new Vector3(-0.43f, 0.26f, 0), new Vector3(0, -0.42f, 0), top, topItem == "jacket_basic" ? top : skin, skin, false);
        Limb(torso, "RightUpperArm", "RightLowerArm", new Vector3(0.43f, 0.26f, 0), new Vector3(0, -0.42f, 0), top, topItem == "jacket_basic" ? top : skin, skin, false);
        Limb(hips, "LeftUpperLeg", "LeftLowerLeg", new Vector3(-0.17f, -0.18f, 0), new Vector3(0, -0.55f, 0), bottom, bottomItem == "pants_basic" ? bottom : skin, shoes, true);
        Limb(hips, "RightUpperLeg", "RightLowerLeg", new Vector3(0.17f, -0.18f, 0), new Vector3(0, -0.55f, 0), bottom, bottomItem == "pants_basic" ? bottom : skin, shoes, true);

        ApplyBodyShape(hips, torso, head, body, face);
        AddWearables(hips, torso, head, data, face);
        var top3 = AlignFeetToGround(hips, scaleFactor);

        var voice = new VoiceIndicator { Name = "VoiceIndicator", Position = new Vector3(0, top3 + .42f, 0), Active = _voiceActive };
        _visual.AddChild(voice); _voice = voice;
        _visual.AddChild(new NamePlate(data.DisplayName, voice) { Name = "DisplayName", Position = new Vector3(0, top3 + .2f, 0) });
        if (_tree is not null) { _tree.Active = false; _tree.Active = true; _playback.Start(Effective(_state)); }
        RememberBuild(data);
        StartCustomBody(data);
        StartWearables(data);
        StartAnimations(data);
    }

    /// Height of the player capsule in Player.tscn; the visible body is scaled to match it.
    private const float CapsuleHeight = 1.8f;

    /// The rig origin is the avatar's feet. The procedural legs are longer than the leg length they were
    /// laid out with, so the shoe soles hang below the origin and the avatar stands knee-deep in the ground.
    /// Measure the lowest point of the body and lift the visual so the soles rest exactly on the origin.
    private float AlignFeetToGround(Node3D hips, Vector3 appearanceScale)
    {
        // Measure the unscaled body, then scale it to the collision capsule height and rest the soles on the origin.
        _visual.Position = Vector3.Zero; _visual.Scale = Vector3.One;
        var lowest = float.MaxValue; var highest = float.MinValue;
        void Visit(Node3D node, Transform3D parent)
        {
            var current = parent * node.Transform;
            if (node is MeshInstance3D { Mesh: not null } mesh)
            {
                var box = mesh.Mesh.GetAabb();
                for (var corner = 0; corner < 8; corner++)
                {
                    var y = (current * (box.Position + new Vector3((corner & 1) * box.Size.X, ((corner >> 1) & 1) * box.Size.Y, ((corner >> 2) & 1) * box.Size.Z))).Y;
                    lowest = Mathf.Min(lowest, y); highest = Mathf.Max(highest, y);
                }
            }
            foreach (var child in node.GetChildren()) if (child is Node3D childNode) Visit(childNode, current);
        }
        Visit(hips, Transform3D.Identity);
        if (lowest >= highest) { _visual.Scale = appearanceScale; return 2.5f; }
        _visual.Scale = appearanceScale * (CapsuleHeight / (highest - lowest));
        _visual.Position = new Vector3(0, -lowest * _visual.Scale.Y, 0);
        return highest;
    }

    public void SetMotionState(AvatarMotionState state, bool force = false)
    {
        if (_overrideState is not null && Time.GetTicksMsec() / 1000.0 < _overrideUntil && !force) return;
        if (_overrideState is not null && Time.GetTicksMsec() / 1000.0 >= _overrideUntil) { _overrideState = null; _overridePriority = 0; }
        if (!force && state == _state) return;
        _state = state;
        if (_playback is not null) _playback.Travel(Effective(state));
    }

    public bool SetAnimationOverride(AvatarMotionState state, float duration, int priority = 1)
    {
        if (duration <= 0 || priority < _overridePriority) return false;
        _overrideState = state; _overridePriority = priority; _overrideUntil = Time.GetTicksMsec() / 1000.0 + Math.Clamp(duration, .05f, 30f);
        _state = state; _playback?.Travel(Effective(state));
        return true;
    }

    private void BuildSkeleton()
    {
        _skeleton = new Skeleton3D { Name = "StandardSkeleton" };
        AddChild(_skeleton);
        var parents = new[] { -1, 0, 1, 2, 3, 4, 3, 6, 7, 3, 9, 10, 1, 12, 13, 1, 15, 16 };
        var origins = new[]
        {
            Vector3.Zero, new Vector3(0, .92f, 0), new Vector3(0, .25f, 0), new Vector3(0, .38f, 0), new Vector3(0, .28f, 0), new Vector3(0, .25f, 0),
            new Vector3(-.38f, .2f, 0), new Vector3(-.38f, 0, 0), new Vector3(-.32f, 0, 0), new Vector3(.38f, .2f, 0), new Vector3(.38f, 0, 0), new Vector3(.32f, 0, 0),
            new Vector3(-.17f, -.2f, 0), new Vector3(0, -.55f, 0), new Vector3(0, -.52f, .08f), new Vector3(.17f, -.2f, 0), new Vector3(0, -.55f, 0), new Vector3(0, -.52f, .08f)
        };
        for (var i = 0; i < BoneNames.Length; i++)
        {
            _skeleton.AddBone(BoneNames[i]);
            _skeleton.SetBoneParent(i, parents[i]);
            _skeleton.SetBoneRest(i, new Transform3D(Basis.Identity, origins[i]));
        }
    }

    private void BuildAnimationSystem()
    {
        var player = new AnimationPlayer { Name = "AnimationPlayer" };
        AddChild(player);
        var library = new AnimationLibrary();
        AddAnimations(library);
        player.AddAnimationLibrary("", library);

        _tree = new AnimationTree { Name = "AnimationTree", AnimPlayer = new NodePath("../AnimationPlayer") };
        AddChild(_tree);
        var machine = new AnimationNodeStateMachine();
        var names = Enum.GetValues<AvatarMotionState>().Select(StateName).Concat(AlternativeNames()).ToArray();
        foreach (var name in names) machine.AddNode(name, new AnimationNodeAnimation { Animation = name });
        foreach (var from in names)
        foreach (var to in names)
            if (from != to) machine.AddTransition(from, to, new AnimationNodeStateMachineTransition { XfadeTime = 0.16f });
        _tree.TreeRoot = machine;
        _tree.Active = true;
        _playback = (AnimationNodeStateMachinePlayback)_tree.Get("parameters/playback");
        _playback.Start("Idle");
    }

    private static void AddAnimations(AnimationLibrary library)
    {
        library.AddAnimation("Idle", Clip(1.8, true, ("Visual/Hips/Torso:rotation", new[] { V(0), V(.03f), V(0) })));
        library.AddAnimation("Walk", Gait(1.0, .48f));
        library.AddAnimation("Run", Gait(.62, .78f));
        library.AddAnimation("Jump", Pose(.45, ("Visual/Hips/LeftUpperLeg:rotation", V(-.45f)), ("Visual/Hips/RightUpperLeg:rotation", V(-.45f)), ("Visual/Hips/Torso/LeftUpperArm:rotation", V(-.6f)), ("Visual/Hips/Torso/RightUpperArm:rotation", V(-.6f))));
        library.AddAnimation("Fall", Pose(.6, ("Visual/Hips/LeftUpperLeg:rotation", V(.18f)), ("Visual/Hips/RightUpperLeg:rotation", V(.18f)), ("Visual/Hips/Torso/LeftUpperArm:rotation", new Vector3(0, 0, -.75f)), ("Visual/Hips/Torso/RightUpperArm:rotation", new Vector3(0, 0, .75f))));
        // Landing flexes the legs instead of animating Hips:position. A position track on Hips makes the AnimationTree
        // reset it to zero in every other state, which sinks the whole avatar into the ground up to the waist.
        library.AddAnimation("Landing", Clip(.35, false, ("Visual/Hips/Torso:rotation", new[] { V(0), V(.22f), V(0) }), ("Visual/Hips/LeftUpperLeg:rotation", new[] { V(0), V(-.5f), V(0) }), ("Visual/Hips/RightUpperLeg:rotation", new[] { V(0), V(-.5f), V(0) })));
        library.AddAnimation("FlyIdle", Pose(1, ("Visual/Hips/Torso/LeftUpperArm:rotation", new Vector3(0, 0, -1.35f)), ("Visual/Hips/Torso/RightUpperArm:rotation", new Vector3(0, 0, 1.35f))));
        library.AddAnimation("FlyMove", Pose(.8, ("Visual/Hips/Torso:rotation", V(-.35f)), ("Visual/Hips/Torso/LeftUpperArm:rotation", V(-2.3f)), ("Visual/Hips/Torso/RightUpperArm:rotation", V(-2.3f))));
        library.AddAnimation("Sit", Pose(.45, ("Visual/Hips/LeftUpperLeg:rotation", V(-1.45f)), ("Visual/Hips/RightUpperLeg:rotation", V(-1.45f)), ("Visual/Hips/LeftUpperLeg/LeftLowerLeg:rotation", V(1.45f)), ("Visual/Hips/RightUpperLeg/RightLowerLeg:rotation", V(1.45f))));
        library.AddAnimation("StandUp", Clip(.5, false, ("Visual/Hips:rotation", new[] { V(.15f), V(0), V(0) })));
        library.AddAnimation("Swim", Clip(1.1, true, ("Visual/Hips/Torso/LeftUpperArm:rotation", new[] { V(-2.5f), V(.4f), V(-2.5f) }), ("Visual/Hips/Torso/RightUpperArm:rotation", new[] { V(.4f), V(-2.5f), V(.4f) })));
        library.AddAnimation("Dance", Clip(1.0, true, ("Visual/Hips:rotation", new[] { new Vector3(0, 0, -.25f), new Vector3(0, 0, .25f), new Vector3(0, 0, -.25f) }), ("Visual/Hips/Torso/LeftUpperArm:rotation", new[] { V(-2.4f), V(-1.2f), V(-2.4f) }), ("Visual/Hips/Torso/RightUpperArm:rotation", new[] { V(-1.2f), V(-2.4f), V(-1.2f) })));
        library.AddAnimation("Wave", Clip(1.0, true, ("Visual/Hips/Torso/RightUpperArm:rotation", new[] { new Vector3(0, 0, 1.8f), new Vector3(0, 0, 1.8f), new Vector3(0, 0, 1.8f) }), ("Visual/Hips/Torso/RightUpperArm/RightLowerArm:rotation", new[] { V(-1.2f), V(-.35f), V(-1.2f) })));
        library.AddAnimation("Applaud", Clip(.8, true, ("Visual/Hips/Torso/LeftUpperArm:rotation", new[] { V(-1.3f), V(-.8f), V(-1.3f) }), ("Visual/Hips/Torso/RightUpperArm:rotation", new[] { V(-1.3f), V(-.8f), V(-1.3f) })));
        library.AddAnimation("Point", Pose(.8, ("Visual/Hips/Torso/RightUpperArm:rotation", new Vector3(0, 0, -1.5f))));
        library.AddAnimation("Greet", Clip(1.2, true, ("Visual/Hips/Torso/RightUpperArm:rotation", new[] { V(1.1f), V(1.7f), V(1.1f) })));
        library.AddAnimation("Laugh", Clip(1.0, true, ("Visual/Hips/Torso:rotation", new[] { V(.12f), V(-.12f), V(.12f) })));
        library.AddAnimation("SitFloor", Pose(.5, ("Visual/Hips/LeftUpperLeg:rotation", V(-1.1f)), ("Visual/Hips/RightUpperLeg:rotation", V(-1.1f))));
        AddAlternativeAnimations(library);
    }

    private static Animation Gait(double length, float amount) => Clip(length, true,
        ("Visual/Hips/LeftUpperLeg:rotation", new[] { V(amount), V(-amount), V(amount) }),
        ("Visual/Hips/RightUpperLeg:rotation", new[] { V(-amount), V(amount), V(-amount) }),
        ("Visual/Hips/Torso/LeftUpperArm:rotation", new[] { V(-amount * .75f), V(amount * .75f), V(-amount * .75f) }),
        ("Visual/Hips/Torso/RightUpperArm:rotation", new[] { V(amount * .75f), V(-amount * .75f), V(amount * .75f) }));

    private static Animation Pose(double length, params (string Path, Vector3 Value)[] tracks) =>
        Clip(length, false, tracks.Select(x => (x.Path, new[] { x.Value, x.Value, x.Value })).ToArray());

    private static Animation Clip(double length, bool loop, params (string Path, Vector3[] Values)[] tracks)
    {
        var animation = new Animation { Length = length, LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None };
        foreach (var (path, values) in tracks)
        {
            var track = animation.AddTrack(Animation.TrackType.Value);
            animation.TrackSetPath(track, path);
            animation.TrackSetInterpolationType(track, Animation.InterpolationType.Linear);
            animation.TrackInsertKey(track, 0, values[0]);
            animation.TrackInsertKey(track, length / 2, values[1]);
            animation.TrackInsertKey(track, length, values[2]);
        }
        return animation;
    }

    private static Vector3 V(float x) => new(x, 0, 0);
    private static string StateName(AvatarMotionState state) => state.ToString();

    private static Node3D Part(Node parent, string name, Vector3 position, Vector3 size, Material material)
    {
        var node = new Node3D { Name = name, Position = position };
        parent.AddChild(node);
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material };
        node.AddChild(mesh);
        return node;
    }

    private static Node3D Sphere(Node parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        var node = new Node3D { Name = name, Position = position };
        parent.AddChild(node);
        var mesh = new MeshInstance3D { Mesh = new SphereMesh { Radius = .5f, Height = 1f }, Scale = scale * 2f, MaterialOverride = material };
        node.AddChild(mesh);
        return node;
    }

    private static void Limb(Node3D parent, string upperName, string lowerName, Vector3 upperPosition, Vector3 lowerPosition, Material upperMaterial, Material lowerMaterial, Material endMaterial, bool leg)
    {
        var upper = Part(parent, upperName, upperPosition, leg ? new Vector3(.25f, .58f, .28f) : new Vector3(.2f, .48f, .2f), upperMaterial);
        var lower = Part(upper, lowerName, lowerPosition, leg ? new Vector3(.22f, .56f, .25f) : new Vector3(.18f, .44f, .18f), lowerMaterial);
        if (leg) Part(lower, lowerName.Replace("LowerLeg", "Foot"), new Vector3(0, -.34f, -.08f), new Vector3(.24f, .16f, .42f), endMaterial);
        else Sphere(lower, lowerName.Replace("LowerArm", "Hand"), new Vector3(0, -.28f, 0), new Vector3(.12f, .15f, .1f), endMaterial);
    }

    private static void AddHair(Node3D head, string style, Material material)
    {
        if (style == "bald") return;
        var size = style switch { "mohawk" => new Vector3(.12f, .24f, .42f), "bob" => new Vector3(.58f, .28f, .52f), "long" => new Vector3(.58f, .52f, .5f), "ponytail" => new Vector3(.5f, .18f, .5f), _ => new Vector3(.5f, .14f, .48f) };
        Part(head, "Hair", new Vector3(0, style == "mohawk" ? .3f : .24f, style == "ponytail" ? .08f : 0), size, material);
        if (style == "ponytail") Part(head, "Ponytail", new Vector3(0, -.08f, .26f), new Vector3(.18f, .42f, .18f), material);
    }

    private static void AddFace(Node3D head, AvatarAppearanceData data, IReadOnlyDictionary<string, float> face)
    {
        var eye = Material(data.EyeColor);
        var eyeScale = face["eyeScale"]; var spacing = face["eyeSpacing"];
        Sphere(head, "EyeLeft", new Vector3(-.105f * spacing, .05f, -.255f), new Vector3(.055f, .055f, .025f) * eyeScale, eye);
        Sphere(head, "EyeRight", new Vector3(.105f * spacing, .05f, -.255f), new Vector3(.055f, .055f, .025f) * eyeScale, eye);
    }

    private static void AddBeard(Node3D head, string style, Material material)
    {
        if (style == "none") return;
        var size = style switch { "full" => new Vector3(.32f, .24f, .16f), "short" => new Vector3(.27f, .16f, .12f), _ => new Vector3(.25f, .08f, .1f) };
        Part(head, "Beard", new Vector3(0, -.16f, -.22f), size, material);
    }

    private static void AddAttachments(Node3D root, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return;
            foreach (var item in document.RootElement.EnumerateArray().Take(8))
            {
                var key = item.TryGetProperty("itemKey", out var value) ? value.GetString() : null;
                if (string.IsNullOrWhiteSpace(key)) continue;
                var slot = item.TryGetProperty("slot", out var slotValue) ? slotValue.GetString() : "chest";
                var position = slot switch { "head" => new Vector3(0, 2.8f, 0), "back" => new Vector3(0, 1.35f, .3f), "hand" => new Vector3(.65f, .9f, 0), _ => new Vector3(0, 1.45f, 0) };
                var tint = item.TryGetProperty("tintColor", out var tintValue) ? tintValue.GetString() ?? "#FFFFFF" : "#FFFFFF";
                Part(root, $"Attachment_{key}", position, new Vector3(.16f, .16f, .16f), Material(tint));
            }
        }
        catch (JsonException) { }
    }

    private static float JsonNumber(string json, string name, float fallback, float min, float max)
    {
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.TryGetProperty(name, out var value) && value.TryGetSingle(out var number) ? Mathf.Clamp(number, min, max) : fallback; }
        catch (JsonException) { return fallback; }
    }

    private static void AddAccessory(Node3D head, string item, string tint)
    {
        if (item == "none") return;
        var material = Material(tint);
        if (item == "hat_basic") Part(head, "Accessory", new Vector3(0, .38f, 0), new Vector3(.56f, .12f, .56f), material);
        else
        {
            Part(head, "GlassesLeft", new Vector3(-.14f, .05f, -.27f), new Vector3(.2f, .12f, .03f), material);
            Part(head, "GlassesRight", new Vector3(.14f, .05f, -.27f), new Vector3(.2f, .12f, .03f), material);
        }
    }

    private static StandardMaterial3D Material(string html) => new() { AlbedoColor = Color.FromHtml(html), Roughness = .72f };
    private static AvatarAppearanceData AvatarAppearanceDataFallback() => AvatarAppearanceDataFactory.Create();

    private static class AvatarAppearanceDataFactory
    {
        public static AvatarAppearanceData Create() => new()
        {
            EquippedItems =
            [
                new EquippedItemData { Slot = "top", ItemKey = "tee_basic", TintColor = "#3AAFC1" },
                new EquippedItemData { Slot = "bottom", ItemKey = "pants_basic", TintColor = "#263B56" },
                new EquippedItemData { Slot = "shoes", ItemKey = "sneakers_basic", TintColor = "#E7E9EE" },
                new EquippedItemData { Slot = "accessory", ItemKey = "none", TintColor = "#FFFFFF" }
            ]
        };
    }
}
