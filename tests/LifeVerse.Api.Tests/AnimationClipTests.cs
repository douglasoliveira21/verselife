using System.Numerics;
using LifeVerse.Shared.Avatar;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Avatar 2.0, fourth part: animation files that can be played on any humanoid, and the states they can replace.</summary>
public sealed class AnimationClipTests
{
    private static readonly (string, int, float)[] WalkTracks = [("upper_leg_l", 0, .6f), ("upper_leg_r", 0, -.6f), ("lower_leg_l", 0, .4f), ("lower_leg_r", 0, .4f), ("upper_arm_l", 0, -.4f), ("upper_arm_r", 0, .4f), ("spine", 1, .1f), ("head", 1, -.1f), ("chest", 1, .1f)];

    private static byte[] File(BoneNaming naming = BoneNaming.Blender, params AnimationSpec[] clips) =>
        HumanoidTemplate.Build(new TemplateOptions { Naming = naming, Animations = clips.Length > 0 ? clips : [new AnimationSpec("Passeio", 1f, WalkTracks, HipsBob: .04f)] });

    private static string[] Codes(AnimationReport report) => [.. report.Errors.Select(e => e.Code).Distinct()];

    [Theory]
    [InlineData(BoneNaming.Blender)] [InlineData(BoneNaming.Mixamo)] [InlineData(BoneNaming.Standard)]
    public void AGoodFileIsAcceptedWhateverToolNamedTheBones(BoneNaming naming)
    {
        var report = AnimationValidator.Validate(File(naming));
        Assert.True(report.Ok, string.Join("; ", report.Errors.Select(e => e.Message)));
        var clip = Assert.Single(report.Clips); Assert.Equal("Passeio", clip.Name); Assert.Equal(1f, clip.Duration); Assert.Equal(9, clip.Bones); Assert.True(clip.Loopable); Assert.False(clip.RootMotion);
        Assert.Equal(["Passeio"], SkeletonClip.Names(File(naming)));
    }

    [Fact]
    public void SeveralAnimationsInOneFile()
    {
        var bytes = File(BoneNaming.Blender, new AnimationSpec("Andar", 1f, WalkTracks), new AnimationSpec("Correr", .6f, WalkTracks), new AnimationSpec("Acenar", 2f, [("upper_arm_r", 2, 1.5f), ("lower_arm_r", 0, -1f), ("chest", 1, .1f), ("spine", 1, .1f), ("head", 1, .2f), ("hips", 1, .1f), ("upper_arm_l", 2, -.3f), ("lower_arm_l", 0, .2f)]));
        var report = AnimationValidator.Validate(bytes);
        Assert.True(report.Ok, string.Join("; ", report.Errors.Select(e => e.Message))); Assert.Equal(["Andar", "Correr", "Acenar"], report.Clips.Select(c => c.Name));
        Assert.Equal(.6f, SkeletonClip.Read(bytes, "Correr")!.Duration); Assert.Equal("Andar", SkeletonClip.Read(bytes, null)!.Name); Assert.Null(SkeletonClip.Read(bytes, "Nao existe"));
    }

    [Fact]
    public void TheClipSaysHowFarEveryBoneHasTurned()
    {
        var clip = SkeletonClip.Read(File(), "Passeio")!;
        var rest = clip.SampleDeltas(0); Assert.All(rest.Values, q => Assert.True(Math.Abs(q.W) > .9999f));
        var mid = clip.SampleDeltas(.5f);
        Assert.InRange(Angle(mid["upper_leg_l"]), .59f, .61f); Assert.InRange(Angle(mid["upper_leg_r"]), .59f, .61f);
        Assert.True(mid["upper_leg_l"].X > 0 && mid["upper_leg_r"].X < 0, "the legs turn opposite ways");
        Assert.InRange(Angle(mid["head"]), .0f, .35f); // the head turns by its own track and by what it inherits from the spine
        var quarter = clip.SampleDeltas(.25f); Assert.InRange(Angle(quarter["upper_leg_l"]), .29f, .31f); // halfway up
        Assert.InRange(Angle(clip.SampleDeltas(1f)["upper_leg_l"]), 0f, .001f);
        Assert.InRange(Angle(clip.SampleDeltas(1.25f)["upper_leg_l"]), .29f, .31f); // the clip loops
        Assert.InRange(Angle(clip.SampleDeltas(5f, loop: false)["upper_leg_l"]), 0f, .001f); // one-shot clips hold their last pose
    }

    [Fact]
    public void ABoneBelowAnAnimatedBoneInheritsItsTurn()
    {
        var clip = SkeletonClip.Read(File(BoneNaming.Blender, new AnimationSpec("Cintura", 1f, [("hips", 1, .8f), ("spine", 1, 0f), ("chest", 1, 0f), ("head", 1, 0f), ("upper_arm_l", 1, 0f), ("upper_arm_r", 1, 0f), ("upper_leg_l", 1, 0f), ("upper_leg_r", 1, 0f)])), null)!;
        var mid = clip.SampleDeltas(.5f);
        Assert.InRange(Angle(mid["hand_l"]), .79f, .81f); Assert.InRange(Angle(mid["foot_r"]), .79f, .81f); // everything under the hips turned with them
    }

    [Fact]
    public void TheHipsBobIsSampled()
    {
        var clip = SkeletonClip.Read(File(), null)!;
        Assert.Equal(0f, clip.SampleHipsOffset(0).Y, 3); Assert.Equal(.04f, clip.SampleHipsOffset(.5f).Y, 3); Assert.Equal(.02f, clip.SampleHipsOffset(.25f).Y, 3);
        Assert.Equal(Vector3.Zero, SkeletonClip.Read(File(BoneNaming.Blender, new AnimationSpec("Parado", 1f, WalkTracks)), null)!.SampleHipsOffset(.5f));
    }

    private static float Angle(Quaternion q) => 2 * MathF.Acos(Math.Clamp(Math.Abs(q.W), 0, 1));

    // ---- files that are refused -----------------------------------------------------------------------------------------------------

    [Fact]
    public void TooFewBonesIsRefused() => Assert.Contains("too_few_bones", Codes(AnimationValidator.Validate(File(BoneNaming.Blender, new AnimationSpec("Pouco", 1f, [("upper_leg_l", 0, .5f), ("upper_leg_r", 0, .5f)])))));

    [Theory]
    [InlineData(.1f)] [InlineData(60f)]
    public void WrongDurationsAreRefused(float seconds) => Assert.Contains("bad_duration", Codes(AnimationValidator.Validate(File(BoneNaming.Blender, new AnimationSpec("Tempo", seconds, WalkTracks)))));

    [Fact]
    public void TwoAnimationsWithTheSameNameAreRefused() => Assert.Contains("duplicate_name", Codes(AnimationValidator.Validate(File(BoneNaming.Blender, new AnimationSpec("Igual", 1f, WalkTracks), new AnimationSpec("Igual", 2f, WalkTracks)))));

    [Fact]
    public void RootMotionIsWarnedAbout() => Assert.Contains(AnimationValidator.Validate(File(BoneNaming.Blender, new AnimationSpec("Anda", 1f, WalkTracks, HipsTravel: 2f))).Warnings, w => w.Code == "root_motion");

    [Fact]
    public void BrokenFilesAreExplained()
    {
        Assert.Equal("empty", Codes(AnimationValidator.Validate([]))[0]);
        Assert.Equal("bad_glb", Codes(AnimationValidator.Validate("not a glb ........................"u8.ToArray()))[0]);
        Assert.Equal("no_animations", Codes(AnimationValidator.Validate(HumanoidTemplate.Build()))[0]);
        Assert.Equal("too_big", Codes(AnimationValidator.Validate(new byte[AnimationValidator.MaxBytes + 1]))[0]);
        Assert.Contains("too_many_clips", Codes(AnimationValidator.Validate(File(BoneNaming.Blender, [.. Enumerable.Range(0, 21).Select(i => new AnimationSpec($"a{i}", 1f, WalkTracks))]))));
    }

    [Fact]
    public void TheValidatorNeverThrowsOnRandomDamage()
    {
        var glb = File(); var random = new Random(23);
        for (var run = 0; run < 200; run++)
        {
            var copy = (byte[])glb.Clone();
            for (var hits = 0; hits < 1 + run % 6; hits++) copy[random.Next(copy.Length)] = (byte)random.Next(256);
            Assert.NotNull(AnimationValidator.Validate(copy));
            SkeletonClip.Read(copy, null)?.SampleDeltas(.3f); // sampling a damaged clip must not throw either
        }
    }

    [Fact]
    public void ReportsCanBeStoredAsJson()
    {
        using var json = System.Text.Json.JsonDocument.Parse(AnimationValidator.Validate(File()).ToJson());
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean()); Assert.Equal("Passeio", json.RootElement.GetProperty("clips")[0].GetProperty("name").GetString());
    }

    // ---- the states ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void EveryStateOfTheAvatarCanBeReplaced()
    {
        foreach (var state in Enum.GetNames<LifeVerse.Shared.Gameplay.AvatarMotionState>().Where(s => s is not ("WalkBack" or "FlatImpact" or "SwimIdle"))) Assert.True(AnimationStates.All.ContainsKey(state), $"{state} has no entry");      // WalkBack is only on the player's own avatar: not a state the editor or the network carries
        Assert.True(AnimationStates.All["Walk"]); Assert.False(AnimationStates.All["Jump"]);
    }

    [Theory]
    [InlineData("custom:5f1b2c1e-1d4a-4a54-a1cf-0a6a2b3c4d5e#Passeio", true, "Passeio")]
    [InlineData("custom:5f1b2c1e-1d4a-4a54-a1cf-0a6a2b3c4d5e#", false, "")]
    [InlineData("custom:nope#Passeio", false, "")]
    [InlineData("builtin:relaxed", false, "")]
    [InlineData("", false, "")]
    public void CustomSourcesAreParsed(string source, bool ok, string animation)
    {
        Assert.Equal(ok, AnimationStates.TryParseCustom(source, out _, out var name)); Assert.Equal(animation, name);
    }
}
