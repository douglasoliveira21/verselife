using System.Numerics;
using LifeVerse.Shared.Networking;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Jumping while already up high (on top of something, or on a hill) must lift the avatar as it does on the ground.</summary>
public sealed class HighJumpTests
{
    private const float Dt = 1f / 60f;

    private static float PeakAfterJump(ObstacleField? field, Func<float, float, float>? terrain, float startY)
    {
        var state = new AuthoritativePlayerState { X = 0, Y = startY, Z = 0 };
        for (var i = 0; i < 30; i++) AuthoritativeMovement.Step(state, new MovementInput((uint)i, 0, 0, InputFlags.None), Dt, terrain, float.NegativeInfinity, float.PositiveInfinity, field);
        var rest = state.Y; var peak = rest;
        for (var i = 0; i < 90; i++) { AuthoritativeMovement.Step(state, new MovementInput((uint)(100 + i), 0, 0, i < 2 ? InputFlags.Jump : InputFlags.None), Dt, terrain, float.NegativeInfinity, float.PositiveInfinity, field); peak = Math.Max(peak, state.Y); }
        return peak - rest;
    }

    [Theory]
    [InlineData("cube")]
    [InlineData("cylinder")]
    [InlineData("cone")]
    [InlineData("hemisphere")]
    public void JumpingFromTheTopOfATallObjectLiftsTheAvatar(string type)
    {
        var field = new ObstacleField(); field.Set(Guid.NewGuid(), ObstacleField.For(type, new Vector3(0, 5, 0), Vector3.Zero, new Vector3(4, 10, 4)));
        // The hemisphere occupies only half of the normalized height (-.25 to +.25).
        // Starting at 10 leaves it airborne after the 30 settling ticks, so Jump is
        // correctly ignored. Begin on the actual surface, as for the other shapes.
        var top = type == "hemisphere" ? 7.5f : 10f;
        var lift = PeakAfterJump(field, null, top);
        Assert.True(lift > 1.0f, $"{type}: jumped only {lift:0.00} m from the top");
    }

    [Fact]
    public void JumpingOnAHighHillLiftsTheAvatar()
    {
        var lift = PeakAfterJump(null, (_, _) => 40f, 40f);
        Assert.True(lift > 1.0f, $"jumped only {lift:0.00} m on a hill");
    }

    [Fact]
    public void JumpingWhileStandingOnAnObjectOnAHillLiftsTheAvatar()
    {
        var field = new ObstacleField(); field.Set(Guid.NewGuid(), ObstacleField.For("cube", new Vector3(0, 42, 0), Vector3.Zero, new Vector3(4, 4, 4)));
        var lift = PeakAfterJump(field, (_, _) => 40f, 44f);
        Assert.True(lift > 1.0f, $"jumped only {lift:0.00} m on top of an object on a hill");
    }
}

/// <summary>An object raised above the ground must not stop someone walking underneath it.</summary>
public sealed class UnderRaisedObjectTests
{
    private const float Dt = 1f / 60f;

    [Theory]
    [InlineData("cube", 0f)] [InlineData("cube", 45f)] [InlineData("sphere", 0f)] [InlineData("cylinder", 0f)] [InlineData("cone", 0f)] [InlineData("hemisphere", 0f)] [InlineData("plane", 0f)] [InlineData("torus", 0f)] [InlineData("tree", 0f)]
    public void WalkingUnderAnObjectWhoseBottomIsAboveTheHeadIsFree(string type, float yaw)
    {
        // a 4 m object centred at 6 m: its bottom is at 4 m, well above the 1.8 m avatar
        var field = new ObstacleField(); field.Set(Guid.NewGuid(), ObstacleField.For(type, new Vector3(0, 6, 0), new Vector3(0, yaw, 0), new Vector3(4, 4, 4)));
        if (type == "tree") field.Set(Guid.NewGuid(), ObstacleField.For(type, new Vector3(0, 12, 0), Vector3.Zero, new Vector3(4, 4, 4)));
        var state = new AuthoritativePlayerState { X = -8, Y = 0, Z = 0 };
        for (var i = 0; i < 600; i++) AuthoritativeMovement.Step(state, new MovementInput((uint)i, 1, 0, InputFlags.Run), Dt, null, float.NegativeInfinity, float.PositiveInfinity, field);
        Assert.True(state.X > 6f, $"{type} (yaw {yaw}): stopped at x={state.X:0.00}");
    }

    [Theory]
    [InlineData("cube")] [InlineData("sphere")] [InlineData("cylinder")]
    public void MovingAnObjectUpMovesItsObstacleWithIt(string type)
    {
        var field = new ObstacleField(); var id = Guid.NewGuid();
        field.Set(id, ObstacleField.For(type, new Vector3(0, 1, 0), Vector3.Zero, new Vector3(4, 2, 4))); // on the ground
        field.Set(id, ObstacleField.For(type, new Vector3(0, 7, 0), Vector3.Zero, new Vector3(4, 2, 4))); // moved up
        var state = new AuthoritativePlayerState { X = -8, Y = 0, Z = 0 };
        for (var i = 0; i < 600; i++) AuthoritativeMovement.Step(state, new MovementInput((uint)i, 1, 0, InputFlags.Run), Dt, null, float.NegativeInfinity, float.PositiveInfinity, field);
        Assert.True(state.X > 6f, $"{type}: still blocked at x={state.X:0.00}");
    }
}
