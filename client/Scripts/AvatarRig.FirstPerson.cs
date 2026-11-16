using Godot;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// First person: the camera sits where the head is, so the person sees only their own body from the waist down and the arms from the elbows to the hands (what is worn on the legs and the hands, and its effects,
/// included). The head, the chest and the upper arms are not drawn: they would be in the way of the camera.
/// </summary>
public sealed partial class AvatarRig
{
    /// <summary>True while the camera of the person who owns this avatar is inside its head.</summary>
    public bool FirstPersonView { get; private set; }

    /// <summary>Bones whose worn objects are left out of the view (everything above the waist except the forearms and hands).</summary>
    private static bool HiddenInFirstPerson(string bone) => bone is "head" or "neck" or "chest" or "spine" or "upper_arm_l" or "upper_arm_r";

    /// <summary>The part of a bone that is left out: a bone cannot be switched off, so it is shrunk to a speck where it stands (not exactly nothing: what follows it is measured from its pose).</summary>
    private const float HiddenBoneScale = .001f;

    public void SetFirstPersonView(bool on)
    {
        if (FirstPersonView == on) return;
        FirstPersonView = on; ApplyFirstPersonView();
    }

    private void ApplyFirstPersonView()
    {
        ApplyStandardFirstPerson();
        if (FirstPersonView) PlanFirstPersonBones(); else RestoreFirstPersonBones(clear: true);
        RefreshFirstPersonHolders();
    }

    // ---- the standard body: its parts are plain nodes -----------------------------------------------------------------------------------

    private static MeshInstance3D? OwnMesh(Node3D? part) => part?.GetChildren().OfType<MeshInstance3D>().FirstOrDefault();

    private void ApplyStandardFirstPerson()
    {
        var hide = FirstPersonView;
        if (_visual?.GetNodeOrNull<Node3D>("Hips/Torso") is not { } torso) return;
        if (OwnMesh(torso) is { } chest) chest.Visible = !hide;                       // the chest (its own box; the arms and the neck are other nodes)
        if (torso.GetNodeOrNull<Node3D>("Neck") is { } neck) neck.Visible = !hide;    // the neck, the head and all it carries
        foreach (var arm in new[] { "LeftUpperArm", "RightUpperArm" }) if (OwnMesh(torso.GetNodeOrNull<Node3D>(arm)) is { } upper) upper.Visible = !hide;   // the upper arm; the forearm hangs from it as another node
    }

    // ---- a custom body: bones ----------------------------------------------------------------------------------------------------------

    private Skeleton3D? _fpSkeleton;
    private int[] _fpHidden = [], _fpShown = [], _fpOrder = [];
    private readonly Dictionary<int, (Vector3 Position, Vector3 Scale)> _fpSaved = [];

    /// <summary>Which bones go and which stay: the spine and everything above it go, except the forearms and what hangs from them.</summary>
    private void PlanFirstPersonBones()
    {
        _fpSaved.Clear(); _fpHidden = []; _fpShown = []; _fpOrder = []; _fpSkeleton = null;
        if (_customSkeleton is not { } skeleton || !IsInstanceValid(skeleton)) return;
        if (!_standardBones.TryGetValue("spine", out var spine)) { HideCustomHead(); return; }      // no spine to count from: only the head can be told apart
        var forearms = new[] { "lower_arm_l", "lower_arm_r" }.Where(_standardBones.ContainsKey).Select(n => _standardBones[n]).ToArray();
        var hidden = new List<int>();
        void Walk(int bone) { if (forearms.Contains(bone)) return; hidden.Add(bone); foreach (var child in skeleton.GetBoneChildren(bone)) Walk(child); }
        Walk(spine);
        _fpSkeleton = skeleton; _fpHidden = [.. hidden]; _fpShown = forearms;
        int Depth(int bone) { var d = 0; for (var p = skeleton.GetBoneParent(bone); p >= 0; p = skeleton.GetBoneParent(p)) d++; return d; }
        _fpOrder = [.. _fpHidden.Concat(_fpShown).OrderBy(Depth)];                              // parents first
        foreach (var bone in _fpOrder) _fpSaved[bone] = (skeleton.GetBonePosePosition(bone), skeleton.GetBonePoseScale(bone));
    }

    /// <summary>Puts the pose of the bones that were shrunk back as it was (each frame before the animation, and when the person steps out of first person).</summary>
    private void RestoreFirstPersonBones(bool clear = false)
    {
        if (_fpSkeleton is { } skeleton && IsInstanceValid(skeleton))
            foreach (var (bone, saved) in _fpSaved) { skeleton.SetBonePosePosition(bone, saved.Position); skeleton.SetBonePoseScale(bone, saved.Scale); }
        if (clear) { _fpSaved.Clear(); _fpHidden = []; _fpShown = []; _fpOrder = []; _fpSkeleton = null; if (_customSkeleton is { } head && _standardBones.TryGetValue("head", out _)) ApplyCustomShape(); }
    }

    /// <summary>Called when the animation has posed the bones: the hidden ones shrink where they stand, and the forearms keep exactly the place they had.</summary>
    private void CollapseUpperBody(Skeleton3D skeleton)
    {
        if (_fpOrder.Length == 0 || skeleton != _fpSkeleton) { if (skeleton != _fpSkeleton) PlanFirstPersonBones(); if (_fpOrder.Length == 0) return; }
        var wanted = new Dictionary<int, Transform3D>(); var hidden = _fpHidden.ToHashSet();
        foreach (var bone in _fpOrder)
        {
            var now = skeleton.GetBoneGlobalPose(bone);
            wanted[bone] = hidden.Contains(bone) ? new Transform3D(now.Basis * Basis.FromScale(Vector3.One * HiddenBoneScale), now.Origin) : now;
        }
        foreach (var bone in _fpOrder) skeleton.SetBoneGlobalPose(bone, wanted[bone]);
    }

    private void HideCustomHead()
    {
        if (_customSkeleton is { } skeleton && IsInstanceValid(skeleton) && _standardBones.TryGetValue("head", out var headBone)) skeleton.SetBonePoseScale(headBone, Vector3.One * HiddenBoneScale);
    }

    /// <summary>Where the eyes are, in metres above the feet, for a custom body (the camera sits there in first person, whatever the height of the body); null for the standard body, whose camera height is already right.</summary>
    public float? EyeHeight => CustomBodyActive && _customSkeleton is { } skeleton && IsInstanceValid(skeleton) && _standardBones.TryGetValue("head", out var headBone)
        ? (skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(headBone).Origin).Y - GlobalPosition.Y + EyesAboveHeadBone : null;

    /// <summary>The eyes are a little above where the head bone starts (the base of the head).</summary>
    public const float EyesAboveHeadBone = .1f;

    /// <summary>Every frame in first person: a look applied again rebuilds the standard body, which must stay out of the way.</summary>
    private void KeepFirstPersonView() => ApplyStandardFirstPerson();

    /// <summary>Shows or hides what is worn above the waist, and shows everything else (also what has just been put on).</summary>
    private void RefreshFirstPersonHolders()
    {
        foreach (var follower in _followers) if (IsInstanceValid(follower.Item)) follower.Item.Visible = !(FirstPersonView && HiddenInFirstPerson(follower.BoneName));
        foreach (var (id, worn) in _attached)
        {
            if (!IsInstanceValid(worn.Holder)) continue;
            var bone = AttachmentPoints.Find(worn.Data.Point)?.Bone ?? string.Empty;
            worn.Holder.Visible = (_soloAttachment is null || _soloAttachment == id) && !(FirstPersonView && HiddenInFirstPerson(bone));
        }
    }

    // ---- tests ---------------------------------------------------------------------------------------------------------------------------

    /// <summary>Tests: which parts of the standard body are drawn.</summary>
    public bool StandardPartShownForTesting(string path) => _visual?.GetNodeOrNull<Node3D>(path) is { } part && part.IsVisibleInTree() && (OwnMesh(part) is not { } mesh || mesh.Visible);
    public bool StandardHeadShownForTesting => StandardPartShownForTesting("Hips/Torso/Neck/Head");
    public bool StandardChestShownForTesting => OwnMesh(_visual?.GetNodeOrNull<Node3D>("Hips/Torso")) is { Visible: true };
    /// <summary>Tests: the size of the part of a custom body that a bone moves (1 = as it is, tiny = not drawn).</summary>
    public float CustomBoneSizeForTesting(string standardBone) => _customSkeleton is { } k && _standardBones.TryGetValue(standardBone, out var b) ? k.GetBoneGlobalPose(b).Basis.Scale.X : 1f;
    public Vector3? CustomBonePositionForTesting(string standardBone) => _customSkeleton is { } k && _standardBones.TryGetValue(standardBone, out var b) ? k.GlobalTransform * k.GetBoneGlobalPose(b).Origin : null;
    public bool AttachmentShownForTesting(Guid id) => AttachmentHolder(id) is { Visible: true };
}
