using System.Text.RegularExpressions;
using Godot;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The fingers of a custom body. The animations of the game move the arms and the hands, but a body whose hands have finger bones (the man of LifeVerse, any model with fingers) would keep them straight as boards, so after the
/// animation has posed the body the fingers are curled toward the palm: loosely when relaxed, open when waving, curled to type, a fist for the one that points, and closed on what the hand holds. The curl blends from one shape to the
/// next. Bodies without finger bones are left as they are.
/// </summary>
public sealed partial class AvatarRig
{
    private enum Finger { Thumb, Index, Middle, Ring, Pinky }

    /// <summary>How much of a fist the fingers make: 0 is straight, 1 is closed.</summary>
    private readonly record struct HandShape(float Thumb, float Index, float Middle, float Ring, float Pinky)
    {
        public float Of(Finger f) => f switch { Finger.Thumb => Thumb, Finger.Index => Index, Finger.Middle => Middle, Finger.Ring => Ring, _ => Pinky };
        public static readonly HandShape Relaxed = new(.2f, .22f, .28f, .34f, .4f), Open = new(.05f, .06f, .06f, .06f, .08f), Typing = new(.18f, .42f, .44f, .44f, .46f), Loose = new(.35f, .5f, .55f, .6f, .65f), Grip = new(.55f, .6f, .62f, .64f, .66f), Pointing = new(.6f, .02f, .95f, .95f, .95f);
    }

    /// <summary>The angles, in degrees, that each of the three joints of a finger turns at a full fist.</summary>
    private static readonly float[][] FistDegrees = [[28f, 38f, 48f], [82f, 100f, 68f], [84f, 104f, 70f], [84f, 104f, 70f], [80f, 100f, 68f]];

    private static readonly Regex UnrealFinger = new(@"^(thumb|index|middle|ring|pinky)_?0?([1-3])_?([lr])$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MixamoFinger = new(@"(left|right)hand(thumb|index|middle|ring|pinky)([1-3])$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Dictionary<(char Side, Finger Finger, int Segment), int> _fingerBones = [];
    private readonly Dictionary<char, Vector3> _palmLocal = [];
    private readonly Dictionary<(char, Finger), float> _fingerShown = [];
    private double _frameDelta = 1.0 / 60;

    /// <summary>Tests: whether the body has fingers the game moves, and the curl each finger shows now (0 to 1).</summary>
    public bool HasFingersForTesting => _fingerBones.Count > 0;
    public float FingerCurlForTesting(char side, int finger) => _fingerShown.GetValueOrDefault((side, (Finger)finger));

    public Vector3? FingerBonePositionForTesting(char side, int finger, int segment) => _customSkeleton is { } k && _fingerBones.TryGetValue((side, (Finger)finger, segment), out var b) ? k.GlobalTransform * k.GetBoneGlobalPose(b).Origin : null;
    /// <summary>Tests: how bent a finger is, in degrees: the angle between its first and its last segment.</summary>
    public float FingerBendForTesting(char side, int finger)
    {
        if (FingerBonePositionForTesting(side, finger, 1) is not { } a || FingerBonePositionForTesting(side, finger, 2) is not { } b || FingerBonePositionForTesting(side, finger, 3) is not { } c) return 0;
        return Mathf.RadToDeg((b - a).AngleTo(c - b));
    }

    private static (char Side, Finger Finger, int Segment)? ParseFinger(string name)
    {
        var m = UnrealFinger.Match(name);
        if (m.Success) return (char.ToLowerInvariant(m.Groups[3].Value[0]), Enum.Parse<Finger>(m.Groups[1].Value, true), int.Parse(m.Groups[2].Value));
        m = MixamoFinger.Match(name);
        if (m.Success) return (char.ToLowerInvariant(m.Groups[1].Value[0]), Enum.Parse<Finger>(m.Groups[2].Value, true), int.Parse(m.Groups[3].Value));
        return null;
    }

    /// <summary>Finds the finger bones of a body and works out, for each hand, which way its palm faces (in the frame of the hand, so it holds whatever the arm does).</summary>
    private void PrepareFingers(Skeleton3D skeleton)
    {
        _fingerBones.Clear(); _palmLocal.Clear(); _fingerShown.Clear();
        for (var bone = 0; bone < skeleton.GetBoneCount(); bone++) if (ParseFinger(skeleton.GetBoneName(bone)) is { } key) _fingerBones.TryAdd(key, bone);
        foreach (var side in new[] { 'l', 'r' })
        {
            if (!_standardBones.TryGetValue($"hand_{side}", out var hand)) continue;
            if (!_fingerBones.TryGetValue((side, Finger.Index, 1), out var index) || !_fingerBones.TryGetValue((side, Finger.Pinky, 1), out var pinky) || !_fingerBones.TryGetValue((side, Finger.Middle, 1), out var middle)) continue;
            var origin = skeleton.GetBoneGlobalRest(hand).Origin;
            var across = skeleton.GetBoneGlobalRest(index).Origin - skeleton.GetBoneGlobalRest(pinky).Origin; var along = skeleton.GetBoneGlobalRest(middle).Origin - origin;
            var palm = across.Cross(along); if (palm.LengthSquared() < 1e-12f) continue; palm = palm.Normalized();
            // in the pose the model is made in (arms out, palms down) the palm looks down; with the arms down along the body it looks at the body
            if (Mathf.Abs(palm.Y) > .35f ? palm.Y > 0 : palm.X * Mathf.Sign(origin.X == 0 ? 1 : origin.X) > 0) palm = -palm;
            _palmLocal[side] = skeleton.GetBoneGlobalRest(hand).Basis.Orthonormalized().Inverse() * palm;
        }
    }

    /// <summary>What shape the hand takes now: closed on what it holds, a fist with one finger out to point, open to wave, curled to type, loose when running, relaxed otherwise.</summary>
    private HandShape ShapeOfHand(char side)
    {
        var holds = _attached.Values.Any(w => IsInstanceValid(w.Holder) && AttachmentPoints.Find(w.Data.Point)?.Bone is { } bone && bone == $"hand_{side}");
        if (_state == AvatarMotionState.Point && side == 'r') return HandShape.Pointing;
        if (holds) return HandShape.Grip;
        return _state switch
        {
            AvatarMotionState.Wave or AvatarMotionState.Greet or AvatarMotionState.Applaud or AvatarMotionState.Dance or AvatarMotionState.Swim => HandShape.Open,
            AvatarMotionState.Typing => HandShape.Typing,
            AvatarMotionState.Run => HandShape.Loose,
            _ => HandShape.Relaxed
        };
    }

    /// <summary>After the animation: curls every finger toward the palm by the current shape of its hand.</summary>
    private void ApplyFingers(Skeleton3D skeleton)
    {
        if (_fingerBones.Count == 0) return;
        var blend = 1f - Mathf.Exp(-12f * (float)_frameDelta);
        foreach (var (side, palmLocal) in _palmLocal)
        {
            if (!_standardBones.TryGetValue($"hand_{side}", out var hand)) continue;
            var shape = ShapeOfHand(side);
            var palm = (skeleton.GetBoneGlobalPose(hand).Basis.Orthonormalized() * palmLocal).Normalized();
            foreach (var finger in Enum.GetValues<Finger>())
            {
                var shown = _fingerShown.TryGetValue((side, finger), out var s) ? s : shape.Of(finger);
                shown = _fingerShown[(side, finger)] = Mathf.Abs(shown - shape.Of(finger)) < .004f ? shape.Of(finger) : Mathf.Lerp(shown, shape.Of(finger), blend);
                if (shown < .002f) continue;
                for (var segment = 1; segment <= 3; segment++)
                {
                    if (!_fingerBones.TryGetValue((side, finger, segment), out var bone)) continue;
                    var pose = skeleton.GetBoneGlobalPose(bone); var here = pose.Origin;
                    Vector3 direction;
                    if (_fingerBones.TryGetValue((side, finger, segment + 1), out var next)) direction = skeleton.GetBoneGlobalPose(next).Origin - here;
                    else if (_fingerBones.TryGetValue((side, finger, segment - 1), out var before)) direction = here - skeleton.GetBoneGlobalPose(before).Origin;
                    else direction = pose.Basis.Y;
                    if (direction.LengthSquared() < 1e-10f) continue;
                    direction = direction.Normalized();
                    var inPlane = palm - direction * palm.Dot(direction); if (inPlane.LengthSquared() < 1e-8f) continue;
                    var axis = direction.Cross(inPlane.Normalized()).Normalized();
                    var turned = new Quaternion(axis, Mathf.DegToRad(FistDegrees[(int)finger][segment - 1] * shown)) * pose.Basis.Orthonormalized().GetRotationQuaternion();
                    var parent = skeleton.GetBoneParent(bone); var parentRotation = parent < 0 ? Quaternion.Identity : skeleton.GetBoneGlobalPose(parent).Basis.Orthonormalized().GetRotationQuaternion();
                    skeleton.SetBonePoseRotation(bone, parentRotation.Inverse() * turned);
                }
            }
        }
    }
}
