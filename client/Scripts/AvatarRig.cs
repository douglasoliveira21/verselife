using Godot;
using LifeVerse.Client.Models;
using LifeVerse.Shared.Gameplay;

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
    private AnimationTree _tree = null!;
    private AnimationNodeStateMachinePlayback _playback = null!;
    private AvatarMotionState _state = AvatarMotionState.Idle;

    public override void _Ready()
    {
        BuildSkeleton();
        BuildAnimationSystem();
        ApplyAppearance(AvatarAppearanceDataFallback());
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
        var scaleFactor = data.BaseModel == "human_b" ? new Vector3(1.06f, 1.04f, 1.06f) : Vector3.One;
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
        AddHair(head, data.HairStyle, hair);
        AddAccessory(head, data.Item("accessory", "none"), data.Tint("accessory", "#FFFFFF"));

        Limb(torso, "LeftUpperArm", "LeftLowerArm", new Vector3(-0.43f, 0.26f, 0), new Vector3(0, -0.42f, 0), top, topItem == "jacket_basic" ? top : skin, skin, false);
        Limb(torso, "RightUpperArm", "RightLowerArm", new Vector3(0.43f, 0.26f, 0), new Vector3(0, -0.42f, 0), top, topItem == "jacket_basic" ? top : skin, skin, false);
        Limb(hips, "LeftUpperLeg", "LeftLowerLeg", new Vector3(-0.17f, -0.18f, 0), new Vector3(0, -0.55f, 0), bottom, bottomItem == "pants_basic" ? bottom : skin, shoes, true);
        Limb(hips, "RightUpperLeg", "RightLowerLeg", new Vector3(0.17f, -0.18f, 0), new Vector3(0, -0.55f, 0), bottom, bottomItem == "pants_basic" ? bottom : skin, shoes, true);

        var name = new Label3D { Name = "DisplayName", Text = data.DisplayName, Position = new Vector3(0, 2.55f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 32, OutlineSize = 6 };
        _visual.AddChild(name);
        if (_tree is not null) { _tree.Active = false; _tree.Active = true; _playback.Start(StateName(_state)); }
    }

    public void SetMotionState(AvatarMotionState state, bool force = false)
    {
        if (!force && state == _state) return;
        _state = state;
        if (_playback is not null) _playback.Travel(StateName(state));
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
        var names = Enum.GetValues<AvatarMotionState>().Select(StateName).ToArray();
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
        library.AddAnimation("Landing", Clip(.35, false, ("Visual/Hips:position", new[] { new Vector3(0, .92f, 0), new Vector3(0, .72f, 0), new Vector3(0, .92f, 0) })));
        library.AddAnimation("FlyIdle", Pose(1, ("Visual/Hips/Torso/LeftUpperArm:rotation", new Vector3(0, 0, -1.35f)), ("Visual/Hips/Torso/RightUpperArm:rotation", new Vector3(0, 0, 1.35f))));
        library.AddAnimation("FlyMove", Pose(.8, ("Visual/Hips/Torso:rotation", V(-.35f)), ("Visual/Hips/Torso/LeftUpperArm:rotation", V(-2.3f)), ("Visual/Hips/Torso/RightUpperArm:rotation", V(-2.3f))));
        library.AddAnimation("Sit", Pose(.45, ("Visual/Hips/LeftUpperLeg:rotation", V(-1.45f)), ("Visual/Hips/RightUpperLeg:rotation", V(-1.45f)), ("Visual/Hips/LeftUpperLeg/LeftLowerLeg:rotation", V(1.45f)), ("Visual/Hips/RightUpperLeg/RightLowerLeg:rotation", V(1.45f))));
        library.AddAnimation("StandUp", Clip(.5, false, ("Visual/Hips:rotation", new[] { V(.15f), V(0), V(0) })));
        library.AddAnimation("Swim", Clip(1.1, true, ("Visual/Hips/Torso/LeftUpperArm:rotation", new[] { V(-2.5f), V(.4f), V(-2.5f) }), ("Visual/Hips/Torso/RightUpperArm:rotation", new[] { V(.4f), V(-2.5f), V(.4f) })));
        library.AddAnimation("Dance", Clip(1.0, true, ("Visual/Hips:rotation", new[] { new Vector3(0, 0, -.25f), new Vector3(0, 0, .25f), new Vector3(0, 0, -.25f) }), ("Visual/Hips/Torso/LeftUpperArm:rotation", new[] { V(-2.4f), V(-1.2f), V(-2.4f) }), ("Visual/Hips/Torso/RightUpperArm:rotation", new[] { V(-1.2f), V(-2.4f), V(-1.2f) })));
        library.AddAnimation("Wave", Clip(1.0, true, ("Visual/Hips/Torso/RightUpperArm:rotation", new[] { new Vector3(0, 0, 1.8f), new Vector3(0, 0, 1.8f), new Vector3(0, 0, 1.8f) }), ("Visual/Hips/Torso/RightUpperArm/RightLowerArm:rotation", new[] { V(-1.2f), V(-.35f), V(-1.2f) })));
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
        var size = style switch { "mohawk" => new Vector3(.12f, .24f, .42f), "bob" => new Vector3(.58f, .28f, .52f), _ => new Vector3(.5f, .14f, .48f) };
        Part(head, "Hair", new Vector3(0, style == "mohawk" ? .3f : .24f, 0), size, material);
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
