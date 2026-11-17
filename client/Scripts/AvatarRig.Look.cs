using Godot;
using LifeVerse.Shared.Networking;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// Where the avatar looks, up or down. Somebody in first person tells the island how far they look up or down; the avatars of the others lean the head that far (some of it through the neck), smoothly, whatever the
/// animation is doing. The standard body leans its neck node; a custom body turns the neck and head bones after the animation has posed them, around the axis that points to the avatar's right.
/// </summary>
public sealed partial class AvatarRig
{
    private float _lookTarget, _lookShown; private bool _lookApplied;

    /// <summary>Positive looks up, negative looks down, in radians (limited to what a head can do).</summary>
    public float LookPitch => _lookTarget;
    public float LookPitchShown => _lookShown;

    public void SetLookPitch(float pitch) => _lookTarget = HeadLook.Drawn(pitch);

    /// <summary>How much of the angle the neck takes; the head takes the rest.</summary>
    private const float NeckShare = .4f;

    private void StepLook(double delta)
    {
        if (_lookShown != _lookTarget) _lookShown = Mathf.Abs(_lookTarget - _lookShown) < .002f ? _lookTarget : Mathf.Lerp(_lookShown, _lookTarget, 1f - Mathf.Exp(-12f * (float)delta));
        if (_lookShown != 0f || _lookApplied) { ApplyStandardLook(); _lookApplied = _lookShown != 0f; }       // also after the look was applied again (a rebuilt body starts straight)
    }

    private void ApplyStandardLook()
    {
        // the neck of the standard body is not driven by any animation: it carries the whole lean
        if (_visual?.GetNodeOrNull<Node3D>("Hips/Torso/Neck") is { } neck) neck.Rotation = new Vector3(_lookShown, 0, 0);
    }

    /// <summary>A custom body: after the animation has posed the bones, the neck and the head are turned about the avatar's right axis (the rotation is made in the space of the avatar and brought into that of the skeleton).</summary>
    private void ApplyLookToCustom(Skeleton3D skeleton)
    {
        if (Mathf.Abs(_lookShown) < .002f || !_standardBones.TryGetValue("head", out var head)) return;
        var toSkeleton = (GlobalBasis.Inverse() * skeleton.GlobalBasis).Orthonormalized().GetRotationQuaternion();
        Quaternion Turn(float angle) => toSkeleton.Inverse() * new Quaternion(Vector3.Right, angle) * toSkeleton;
        var neckBone = _standardBones.TryGetValue("neck", out var n) ? n : -1;
        var headGlobal = _globalNow[head]; var parentOfHead = skeleton.GetBoneParent(head);
        var parentGlobal = parentOfHead < 0 ? Quaternion.Identity : _globalNow[parentOfHead];
        if (neckBone >= 0 && neckBone == parentOfHead)
        {
            var neckGlobal = Turn(_lookShown * NeckShare) * _globalNow[neckBone];
            skeleton.SetBonePoseRotation(neckBone, (skeleton.GetBoneParent(neckBone) < 0 ? Quaternion.Identity : _globalNow[skeleton.GetBoneParent(neckBone)]).Inverse() * neckGlobal);
            parentGlobal = neckGlobal; headGlobal = Turn(_lookShown * NeckShare) * headGlobal;      // the head goes with the neck
            skeleton.SetBonePoseRotation(head, parentGlobal.Inverse() * (Turn(_lookShown * (1f - NeckShare)) * headGlobal));
        }
        else skeleton.SetBonePoseRotation(head, parentGlobal.Inverse() * (Turn(_lookShown) * headGlobal));
    }

    /// <summary>Tests: where the head bone points in the world (its own orientation), to see how far it leans.</summary>
    public Basis? CustomHeadWorldBasisForTesting => _customSkeleton is { } k && _standardBones.TryGetValue("head", out var b) ? k.GlobalBasis * k.GetBoneGlobalPose(b).Basis : null;
    public float StandardNeckPitchForTesting => _visual?.GetNodeOrNull<Node3D>("Hips/Torso/Neck")?.Rotation.X ?? 0f;
}
