using Godot;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// First person, "true first person": the camera is at the eyes of the avatar's own head and the whole body is drawn, so looking down shows chest, belly, arms and hands, legs, knees and feet with the clothes and the
/// objects worn. Only the head (and the neck that carries it, and what is worn there) is left out of what the owner sees; everybody else still sees it, because this only changes how this one avatar is drawn for its own camera.
/// </summary>
public sealed partial class AvatarRig
{
    /// <summary>True while the camera of the person who owns this avatar is inside its head.</summary>
    public bool FirstPersonView { get; private set; }

    /// <summary>Bones whose worn objects are left out of the view (they would fill the screen from inside the head).</summary>
    private static bool HiddenInFirstPerson(string bone) => bone is "head" or "neck";

    /// <summary>A bone cannot be switched off, so the head is shrunk to a speck where it stands (not exactly nothing: what follows it is measured from its pose).</summary>
    private const float HiddenBoneScale = .001f;

    /// <summary>Where the eyes are relative to the head bone (the base of the head), for a body that is as tall as the standard one: up and forward, in metres.</summary>
    public const float EyesAboveHeadBone = .10f, EyesForwardOfHeadBone = .09f;

    public void SetFirstPersonView(bool on)
    {
        if (FirstPersonView == on) return;
        FirstPersonView = on; ApplyFirstPersonView();
    }

    private void ApplyFirstPersonView()
    {
        ApplyStandardFirstPerson();
        if (FirstPersonView) PlanFirstPersonBones(); else RestoreFirstPersonBones();
        RefreshFirstPersonHolders();
    }

    // ---- where the camera goes -----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The point of the world where the eyes are: at the head of the body as it is posed right now (so crouching, sitting, leaning and the bob of a step carry the camera), a little in front of the neck.
    /// <paramref name="forward"/> is where the avatar faces. Null when there is no head to follow.
    /// </summary>
    public Vector3? EyeWorldPosition(Vector3 forward)
    {
        forward = new Vector3(forward.X, 0, forward.Z); forward = forward.LengthSquared() < 1e-6f ? Vector3.Forward : forward.Normalized();
        if (CustomBodyActive && _customSkeleton is { } skeleton && IsInstanceValid(skeleton) && _standardBones.TryGetValue("head", out var headBone))
        {
            var head = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(headBone).Origin;
            var k = Mathf.Clamp((head.Y - GlobalPosition.Y) / 1.55f, .5f, 2f);          // taller or shorter bodies carry their eyes proportionally
            return head + Vector3.Up * (EyesAboveHeadBone * k) + forward * (EyesForwardOfHeadBone * k);
        }
        if (_visual?.GetNodeOrNull<Node3D>("Hips/Torso/Neck/Head") is { } sphere) return sphere.GlobalPosition + Vector3.Up * .03f + forward * .12f;   // the middle of the standard head; the face is in front of it
        return null;
    }

    /// <summary>The eyes' height above the feet (null with no head).</summary>
    public float? EyeHeight => EyeWorldPosition(Vector3.Forward) is { } eyes ? eyes.Y - GlobalPosition.Y : null;

    // ---- the standard body: its parts are plain nodes -----------------------------------------------------------------------------------

    private void ApplyStandardFirstPerson()
    {
        if (_visual?.GetNodeOrNull<Node3D>("Hips/Torso/Neck") is { } neck) neck.Visible = !FirstPersonView;    // the neck, the head and all it carries
    }

    // ---- a custom body: bones ----------------------------------------------------------------------------------------------------------

    private Skeleton3D? _fpSkeleton;
    private int[] _fpHidden = [];
    private readonly Dictionary<int, (Vector3 Position, Vector3 Scale)> _fpSaved = [];

    /// <summary>Which bones go: the neck and everything under it (head, eyes, jaw, hair).</summary>
    private void PlanFirstPersonBones()
    {
        _fpSaved.Clear(); _fpHidden = []; _fpSkeleton = null;
        if (_customSkeleton is not { } skeleton || !IsInstanceValid(skeleton)) return;
        if (!_standardBones.TryGetValue("neck", out var top) && !_standardBones.TryGetValue("head", out top)) return;
        var hidden = new List<int>();
        void Walk(int bone) { hidden.Add(bone); foreach (var child in skeleton.GetBoneChildren(bone)) Walk(child); }
        Walk(top);
        int Depth(int bone) { var d = 0; for (var p = skeleton.GetBoneParent(bone); p >= 0; p = skeleton.GetBoneParent(p)) d++; return d; }
        _fpSkeleton = skeleton; _fpHidden = [.. hidden.OrderBy(Depth)];                            // parents first
        foreach (var bone in _fpHidden) _fpSaved[bone] = (skeleton.GetBonePosePosition(bone), skeleton.GetBonePoseScale(bone));
    }

    /// <summary>Puts the pose of the bones that were shrunk back as it was (each frame before the animation, and when the person steps out of first person).</summary>
    private void RestoreFirstPersonBones()
    {
        if (_fpSkeleton is { } skeleton && IsInstanceValid(skeleton))
            foreach (var (bone, saved) in _fpSaved) { skeleton.SetBonePosePosition(bone, saved.Position); skeleton.SetBonePoseScale(bone, saved.Scale); }
        if (!FirstPersonView) { _fpSaved.Clear(); _fpHidden = []; _fpSkeleton = null; }
    }

    /// <summary>Called when the animation has posed the bones: the head shrinks where it stands.</summary>
    private void CollapseHead(Skeleton3D skeleton)
    {
        if (skeleton != _fpSkeleton) PlanFirstPersonBones();
        if (_fpHidden.Length == 0) return;
        var wanted = new Dictionary<int, Transform3D>();
        foreach (var bone in _fpHidden) { var now = skeleton.GetBoneGlobalPose(bone); wanted[bone] = new Transform3D(now.Basis * Basis.FromScale(Vector3.One * HiddenBoneScale), now.Origin); }
        foreach (var bone in _fpHidden) skeleton.SetBoneGlobalPose(bone, wanted[bone]);
    }

    /// <summary>Every frame in first person: a look applied again rebuilds the standard body, which must stay out of the way.</summary>
    private void KeepFirstPersonView() => ApplyStandardFirstPerson();

    /// <summary>Shows or hides what is worn on the head and the neck, and shows everything else (also what has just been put on).</summary>
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
    public bool StandardPartShownForTesting(string path) => _visual?.GetNodeOrNull<Node3D>(path) is { } part && part.IsVisibleInTree();
    /// <summary>Tests: the size of the part of a custom body that a bone moves (1 = as it is, tiny = not drawn).</summary>
    public float CustomBoneSizeForTesting(string standardBone) => _customSkeleton is { } k && _standardBones.TryGetValue(standardBone, out var b) ? k.GetBoneGlobalPose(b).Basis.Scale.X : 1f;
    public Vector3? CustomBonePositionForTesting(string standardBone) => _customSkeleton is { } k && _standardBones.TryGetValue(standardBone, out var b) ? k.GlobalTransform * k.GetBoneGlobalPose(b).Origin : null;
    public bool AttachmentShownForTesting(Guid id) => AttachmentHolder(id) is { Visible: true };
}
