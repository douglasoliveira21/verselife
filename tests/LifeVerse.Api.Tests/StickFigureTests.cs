using System.Numerics;
using LifeVerse.Shared.Avatar;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The bare skeleton the inventory draws to preview an animation.</summary>
public sealed class StickFigureTests
{
    private static SkeletonClip Clip(params (string Bone, int Axis, float Angle)[] tracks) =>
        SkeletonClip.Read(HumanoidTemplate.Build(new TemplateOptions { Animations = [new AnimationSpec("Teste", 1f, tracks, HipsBob: .05f)] }), "Teste")!;

    private static float Length(IReadOnlyDictionary<string, Vector3> pose, string a, string b) => Vector3.Distance(pose[a], pose[b]);

    [Fact]
    public void TheRestPoseIsAPersonAboutAsTallAsAPersonWithTheFeetOnTheGround()
    {
        var pose = StickFigure.Pose(null); Assert.Equal(StickFigure.Joints.Count, pose.Count); Assert.Equal(17, pose.Count);
        Assert.InRange(pose["head"].Y, 1.4f, 1.9f); Assert.InRange(pose["foot_l"].Y, -.01f, .12f); Assert.InRange(pose["foot_r"].Y, -.01f, .12f);
        Assert.True(pose["hand_l"].X > pose["chest"].X && pose["hand_r"].X < pose["chest"].X); // the left hand is on the left
        Assert.Equal(16, StickFigure.Lines().Count()); Assert.All(StickFigure.Lines(), l => Assert.True(pose.ContainsKey(l.From) && pose.ContainsKey(l.To)));
        Assert.Equal(StickFigure.Joints.Select(j => j.Bone).Distinct().Count(), StickFigure.Joints.Count);
    }

    [Fact]
    public void LimbsKeepTheirLengthWhateverTheTurn()
    {
        var rest = StickFigure.Pose(null); var rng = new Random(7); var deltas = new Dictionary<string, Quaternion>();
        for (var round = 0; round < 40; round++)
        {
            deltas.Clear(); foreach (var joint in StickFigure.Joints) deltas[joint.Bone] = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll((float)(rng.NextDouble() * 6), (float)(rng.NextDouble() * 6), (float)(rng.NextDouble() * 6)));
            var pose = StickFigure.Pose(deltas, (float)rng.NextDouble());
            foreach (var (from, to) in StickFigure.Lines()) Assert.InRange(Length(pose, from, to), Length(rest, from, to) - 1e-4f, Length(rest, from, to) + 1e-4f);
        }
    }

    [Fact]
    public void ATurnedBoneMovesWhatHangsFromItAndNothingElse()
    {
        var rest = StickFigure.Pose(null); var up = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2); // the right arm swings up
        var pose = StickFigure.Pose(new Dictionary<string, Quaternion> { ["upper_arm_r"] = up });
        Assert.Equal(rest["upper_arm_r"], pose["upper_arm_r"]); // the shoulder stays
        Assert.True(Vector3.Distance(rest["lower_arm_r"], pose["lower_arm_r"]) > .2f); Assert.True(pose["lower_arm_r"].Y > rest["lower_arm_r"].Y + .2f); Assert.True(Vector3.Distance(rest["hand_r"], pose["hand_r"]) > .3f);
        foreach (var bone in new[] { "hips", "head", "hand_l", "foot_l", "foot_r", "upper_arm_l" }) Assert.Equal(rest[bone], pose[bone]);
    }

    [Fact]
    public void TheHipsLiftCarriesTheWholeFigure()
    {
        var rest = StickFigure.Pose(null); var lifted = StickFigure.Pose(null, .3f);
        foreach (var bone in rest.Keys) Assert.Equal(rest[bone].Y + .3f, lifted[bone].Y, 4);
    }

    [Fact]
    public void AClipPosesTheFigureOverTimeAndLoops()
    {
        var clip = Clip(("upper_leg_l", 0, .8f), ("upper_leg_r", 0, -.8f), ("upper_arm_l", 0, -.5f));
        var start = StickFigure.Pose(clip, 0); var middle = StickFigure.Pose(clip, clip.Duration / 2); var end = StickFigure.Pose(clip, clip.Duration);
        Assert.True(Vector3.Distance(start["foot_l"], middle["foot_l"]) > .05f, "the leg moves during the clip");
        var again = StickFigure.Pose(clip, clip.Duration * 3 + clip.Duration / 2, loop: true); Assert.True(Vector3.Distance(middle["foot_l"], again["foot_l"]) < 1e-3f); // time wraps around
        foreach (var (from, to) in StickFigure.Lines()) Assert.InRange(Length(middle, from, to), Length(StickFigure.Pose(null), from, to) - 1e-3f, Length(StickFigure.Pose(null), from, to) + 1e-3f);
    }
}
