using System.Numerics;

namespace LifeVerse.Shared.Avatar;

/// <summary>
/// A bare skeleton of the standard humanoid (the bones every animation file is mapped onto) posed by a clip: what the inventory's animation preview draws.
/// Each bone sits where its parent's turn carries it, so the lengths of the limbs never change, whatever the clip does.
/// </summary>
public static class StickFigure
{
    public sealed record Joint(string Bone, string? Parent, Vector3 Offset);

    /// <summary>The standard bones, parents before children, with where each one sits from its parent at rest (metres, a person about 1.75 m tall).</summary>
    public static readonly IReadOnlyList<Joint> Joints =
    [
        new("hips", null, new(0, .95f, 0)), new("spine", "hips", new(0, .12f, 0)), new("chest", "spine", new(0, .2f, 0)), new("neck", "chest", new(0, .18f, 0)), new("head", "neck", new(0, .1f, 0)),
        new("upper_arm_l", "chest", new(.18f, .1f, 0)), new("lower_arm_l", "upper_arm_l", new(.28f, 0, 0)), new("hand_l", "lower_arm_l", new(.25f, 0, 0)),
        new("upper_arm_r", "chest", new(-.18f, .1f, 0)), new("lower_arm_r", "upper_arm_r", new(-.28f, 0, 0)), new("hand_r", "lower_arm_r", new(-.25f, 0, 0)),
        new("upper_leg_l", "hips", new(.09f, -.05f, 0)), new("lower_leg_l", "upper_leg_l", new(0, -.42f, 0)), new("foot_l", "lower_leg_l", new(0, -.42f, 0)),
        new("upper_leg_r", "hips", new(-.09f, -.05f, 0)), new("lower_leg_r", "upper_leg_r", new(0, -.42f, 0)), new("foot_r", "lower_leg_r", new(0, -.42f, 0))
    ];

    /// <summary>Where every bone is when the figure is posed by <paramref name="deltas"/> (how far each bone has turned from rest, as <see cref="SkeletonClip.SampleDeltas"/> gives them) and the hips are lifted by <paramref name="hipsLift"/>. Null deltas: the rest pose.</summary>
    public static Dictionary<string, Vector3> Pose(IReadOnlyDictionary<string, Quaternion>? deltas, float hipsLift = 0)
    {
        var positions = new Dictionary<string, Vector3>();
        foreach (var joint in Joints)
        {
            if (joint.Parent is null) { positions[joint.Bone] = joint.Offset + new Vector3(0, hipsLift, 0); continue; }
            var turn = deltas is not null && deltas.TryGetValue(joint.Parent, out var q) ? q : Quaternion.Identity;
            positions[joint.Bone] = positions[joint.Parent] + Vector3.Transform(joint.Offset, turn);
        }
        return positions;
    }

    /// <summary>The figure at a moment of a clip.</summary>
    public static Dictionary<string, Vector3> Pose(SkeletonClip clip, float time, bool loop = true) => Pose(clip.SampleDeltas(time, loop), clip.SampleHipsOffset(time, loop).Y);

    /// <summary>The lines to draw: each bone from its parent.</summary>
    public static IEnumerable<(string From, string To)> Lines() => Joints.Where(j => j.Parent is not null).Select(j => (j.Parent!, j.Bone));
}
