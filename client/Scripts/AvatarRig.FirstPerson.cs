using Godot;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// First person: the camera sits where the head is, so the head (and what is worn on the head and the neck, which would fill the screen from inside) is not drawn; the rest of the body, the objects worn on it and
/// their effects are, so the person sees their own hands, feet and what they carry.
/// </summary>
public sealed partial class AvatarRig
{
    /// <summary>True while the camera of the person who owns this avatar is inside its head.</summary>
    public bool FirstPersonView { get; private set; }

    /// <summary>Bones whose objects are left out of the view from inside the head.</summary>
    /// <summary>Not exactly nothing: what follows the bone is measured from its pose, which must stay solvable.</summary>
    private const float HiddenHeadScale = .001f;

    private static bool NearCamera(string bone) => bone is "head" or "neck";

    public void SetFirstPersonView(bool on)
    {
        if (FirstPersonView == on) return;
        FirstPersonView = on; ApplyFirstPersonView();
    }

    private void ApplyFirstPersonView()
    {
        // the standard body: the head node carries the face, hair, beard and glasses
        if (_visual?.GetNodeOrNull<Node3D>("Hips/Torso/Neck/Head") is { } head) head.Visible = !FirstPersonView;
        // a custom body: the head bone shrinks to nothing, and the skin it moves goes with it (put back when the person steps out)
        if (FirstPersonView) HideCustomHead(); else ApplyCustomShape();
        RefreshFirstPersonHolders();
    }

    /// <summary>Every frame in first person: a look applied again rebuilds the head and the skin, which must stay out of the way.</summary>
    private void KeepFirstPersonView()
    {
        if (_visual?.GetNodeOrNull<Node3D>("Hips/Torso/Neck/Head") is { Visible: true } head) head.Visible = false;
        HideCustomHead();
    }

    private void HideCustomHead()
    {
        if (_customSkeleton is { } skeleton && IsInstanceValid(skeleton) && _standardBones.TryGetValue("head", out var headBone)) skeleton.SetBonePoseScale(headBone, Vector3.One * HiddenHeadScale);
    }

    /// <summary>Shows or hides what is worn on the head and the neck, and shows everything else (also what has just been put on).</summary>
    private void RefreshFirstPersonHolders()
    {
        foreach (var follower in _followers) if (IsInstanceValid(follower.Item)) follower.Item.Visible = !(FirstPersonView && NearCamera(follower.BoneName));
        foreach (var (id, worn) in _attached)
        {
            if (!IsInstanceValid(worn.Holder)) continue;
            var bone = AttachmentPoints.Find(worn.Data.Point)?.Bone ?? string.Empty;
            worn.Holder.Visible = (_soloAttachment is null || _soloAttachment == id) && !(FirstPersonView && NearCamera(bone));
        }
    }

    /// <summary>Tests: whether the body shows its head, and whether an object worn is shown.</summary>
    public bool StandardHeadShownForTesting => _visual?.GetNodeOrNull<Node3D>("Hips/Torso/Neck/Head") is { Visible: true };
    public bool CustomHeadScaleZeroForTesting => _customSkeleton is { } k && _standardBones.TryGetValue("head", out var b) && k.GetBonePoseScale(b).LengthSquared() < 1e-4f;
    public bool AttachmentShownForTesting(Guid id) => AttachmentHolder(id) is { Visible: true };
}
