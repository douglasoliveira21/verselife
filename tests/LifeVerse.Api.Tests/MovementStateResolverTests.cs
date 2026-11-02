using LifeVerse.Shared.Gameplay;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class MovementStateResolverTests
{
    [Theory]
    [InlineData(false, false, AvatarMotionState.Idle)]
    [InlineData(true, false, AvatarMotionState.Walk)]
    [InlineData(true, true, AvatarMotionState.Run)]
    public void Ground_states_follow_input(bool moving, bool running, AvatarMotionState expected) =>
        Assert.Equal(expected, MovementStateResolver.Resolve(new MovementSnapshot(false, false, false, true, false, false, moving, running)));

    [Fact]
    public void Air_state_switches_from_jump_to_fall()
    {
        Assert.Equal(AvatarMotionState.Jump, MovementStateResolver.Resolve(new MovementSnapshot(false, false, false, false, true, false, true, false)));
        Assert.Equal(AvatarMotionState.Fall, MovementStateResolver.Resolve(new MovementSnapshot(false, false, false, false, false, false, true, false)));
    }

    [Fact]
    public void Special_modes_have_priority()
    {
        Assert.Equal(AvatarMotionState.Sit, MovementStateResolver.Resolve(new MovementSnapshot(true, true, true, false, true, false, true, true)));
        Assert.Equal(AvatarMotionState.Swim, MovementStateResolver.Resolve(new MovementSnapshot(false, true, true, false, true, false, true, true)));
        Assert.Equal(AvatarMotionState.FlyMove, MovementStateResolver.Resolve(new MovementSnapshot(false, false, true, false, true, false, true, true)));
        Assert.Equal(AvatarMotionState.Landing, MovementStateResolver.Resolve(new MovementSnapshot(false, true, true, true, false, true, true, true)));
    }
}
