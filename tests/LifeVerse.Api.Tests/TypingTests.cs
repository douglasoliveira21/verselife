using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Gameplay;
using LifeVerse.Shared.Networking;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Typing in the chat: the avatar types for everybody. The state travels like waving does (a flag from the player, a state from the island), only standing still, and the clip it plays is the one the game ships.</summary>
public sealed class TypingTests
{
    private static NetworkMotionState Run(InputFlags flags, float x = 0, float z = 0, bool settle = true)
    {
        var state = new AuthoritativePlayerState(); var motion = NetworkMotionState.Idle;
        for (var i = 0; i < (settle ? 40 : 1); i++) motion = AuthoritativeMovement.Step(state, new MovementInput((uint)i + 1, x, z, flags), 1f / 60f, null, float.NegativeInfinity, float.PositiveInfinity, null);
        return motion;
    }

    [Fact]
    public void StandingStillWhileTypingIsTyping() => Assert.Equal(NetworkMotionState.Typing, Run(InputFlags.Typing));

    [Fact]
    public void WithoutTheFlagItIsIdle() => Assert.Equal(NetworkMotionState.Idle, Run(InputFlags.None));

    [Fact]
    public void WalkingOrRunningWinsOverTyping() { Assert.Equal(NetworkMotionState.Walk, Run(InputFlags.Typing, 0, 1)); Assert.Equal(NetworkMotionState.Run, Run(InputFlags.Typing | InputFlags.Run, 0, 1)); }

    [Fact]
    public void JumpingWinsOverTyping() => Assert.Equal(NetworkMotionState.Jump, Run(InputFlags.Typing | InputFlags.Jump, settle: false));

    [Fact]
    public void WavingAndDancingWinOverTyping() { Assert.Equal(NetworkMotionState.Wave, Run(InputFlags.Typing | InputFlags.Wave)); Assert.Equal(NetworkMotionState.Dance, Run(InputFlags.Typing | InputFlags.Dance)); }

    [Fact]
    public void TheNewNumbersWereAddedAtTheEnd()
    {
        Assert.Equal(128, (int)InputFlags.Typing); Assert.Equal(10, (int)NetworkMotionState.Typing); Assert.Equal((int)AvatarMotionState.SwimIdle + 1, (int)AvatarMotionState.Typing);
        Assert.Equal(NetworkMotionState.Swim, (NetworkMotionState)9); // nothing that was there moved
        Assert.Equal(InputFlags.Dance, (InputFlags)64);
    }

    [Fact]
    public void TheFlagFitsInTheByteTheServerKeeps() => Assert.Equal(255, (byte)(InputFlags.Run | InputFlags.Jump | InputFlags.Fly | InputFlags.Descend | InputFlags.Sit | InputFlags.Wave | InputFlags.Dance | InputFlags.Typing));

    [Theory]
    [InlineData(AvatarMotionState.Idle, true, AvatarMotionState.Typing)] [InlineData(AvatarMotionState.Idle, false, AvatarMotionState.Idle)] [InlineData(AvatarMotionState.Walk, true, AvatarMotionState.Walk)]
    [InlineData(AvatarMotionState.Run, true, AvatarMotionState.Run)] [InlineData(AvatarMotionState.Sit, true, AvatarMotionState.Sit)] [InlineData(AvatarMotionState.FlyIdle, true, AvatarMotionState.FlyIdle)]
    [InlineData(AvatarMotionState.Jump, true, AvatarMotionState.Jump)] [InlineData(AvatarMotionState.SwimIdle, true, AvatarMotionState.SwimIdle)] [InlineData(AvatarMotionState.Wave, true, AvatarMotionState.Wave)]
    public void OnlyAnAvatarThatStandsStillTypes(AvatarMotionState state, bool typing, AvatarMotionState expected) => Assert.Equal(expected, TypingState.Apply(state, typing));

    [Fact]
    public void TheClipTheGameShipsIsALoopOfSecondsWithTheBonesOfAPerson()
    {
        var dir = AppContext.BaseDirectory; string? path = null;
        for (var d = new DirectoryInfo(dir); d is not null && path is null; d = d.Parent) { var candidate = Path.Combine(d.FullName, "client", "Assets", "animations", "typing_animation.bin"); if (File.Exists(candidate)) path = candidate; }
        Assert.NotNull(path); var clip = SkeletonClip.Read(File.ReadAllBytes(path!), "Typing");
        Assert.NotNull(clip); Assert.InRange(clip!.Duration, 4f, 12f);
        foreach (var bone in new[] { "hips", "spine", "head", "upper_arm_l", "upper_arm_r", "lower_arm_l", "lower_arm_r", "hand_l", "hand_r" }) Assert.Contains(bone, clip.Bones);
        var start = clip.SampleDeltas(0f); var later = clip.SampleDeltas(clip.Duration / 2);
        Assert.Contains(later, p => !p.Value.Equals(start[p.Key]) && Math.Abs(System.Numerics.Quaternion.Dot(p.Value, start[p.Key])) < .9999f); // it really moves
        var wrap = clip.SampleDeltas(clip.Duration - .001f); foreach (var (bone, q) in start) Assert.True(Math.Abs(System.Numerics.Quaternion.Dot(q, wrap[bone])) > .98f, $"the loop closes: {bone}"); // the end is the start
    }
}
