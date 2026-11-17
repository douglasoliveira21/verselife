using LifeVerse.Shared.Avatar;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The animations of the game for a custom body that come in files of their own: standing still on the ground, going up in a jump and coming down after it.</summary>
public sealed class DefaultClipsTests
{
    private static SkeletonClip Clip(string file, string name)
    {
        string? path = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null && path is null; d = d.Parent) { var candidate = Path.Combine(d.FullName, "client", "Assets", "animations", file); if (File.Exists(candidate)) path = candidate; }
        Assert.NotNull(path); var clip = SkeletonClip.Read(File.ReadAllBytes(path!), name);
        Assert.NotNull(clip); return clip!;
    }

    private static readonly string[] Body = ["hips", "spine", "head", "upper_arm_l", "upper_arm_r", "lower_arm_l", "lower_arm_r", "hand_l", "hand_r", "upper_leg_l", "upper_leg_r", "lower_leg_l", "lower_leg_r", "foot_l", "foot_r"];

    [Fact]
    public void StandingStillIsALongLoopThatNeverStops()
    {
        var clip = Clip("idle_animation.bin", "Idle");
        Assert.InRange(clip.Duration, 10f, 20f); foreach (var bone in Body) Assert.Contains(bone, clip.Bones);
        var start = clip.SampleDeltas(0f); var middle = clip.SampleDeltas(clip.Duration / 2);
        Assert.Contains(middle, p => System.Numerics.Quaternion.Dot(p.Value, start[p.Key]) is var dot && Math.Abs(dot) < .9999f);          // it moves
        var end = clip.SampleDeltas(clip.Duration - .001f); foreach (var (bone, q) in start) Assert.True(Math.Abs(System.Numerics.Quaternion.Dot(q, end[bone])) > .99f, $"the loop closes: {bone}");
        var again = clip.SampleDeltas(clip.Duration + .5f, loop: true); var soon = clip.SampleDeltas(.5f, loop: true);
        foreach (var (bone, q) in soon) Assert.True(Math.Abs(System.Numerics.Quaternion.Dot(q, again[bone])) > .9999f, $"after the end it goes on from the start: {bone}");
        Assert.True(clip.SampleHipsOffset(0f).Length() < 1.5f && clip.SampleHipsOffset(clip.Duration / 2).Length() < 1.5f);               // it stays in place
    }

    [Theory]
    [InlineData("jump_animation.bin", "Jump", .5f, 1.5f)]
    [InlineData("jumpdown_animation.bin", "JumpDown", .2f, 1.0f)]
    public void TheJumpClipsAreShortAndLeaveTheLiftToThePhysics(string file, string name, float minSeconds, float maxSeconds)
    {
        var clip = Clip(file, name);
        Assert.InRange(clip.Duration, minSeconds, maxSeconds); foreach (var bone in Body) Assert.Contains(bone, clip.Bones);
        var first = clip.SampleDeltas(0f); var last = clip.SampleDeltas(clip.Duration, loop: false);
        Assert.Contains(last, p => Math.Abs(System.Numerics.Quaternion.Dot(p.Value, first[p.Key])) < .9999f);                              // the pose changes along the clip
        for (var i = 0; i <= 10; i++) Assert.True(Math.Abs(clip.SampleHipsOffset(clip.Duration * i / 10f, loop: false).Y) < .02f, "the hips are not lifted twice: the game's physics already lifts the avatar");
    }
}
