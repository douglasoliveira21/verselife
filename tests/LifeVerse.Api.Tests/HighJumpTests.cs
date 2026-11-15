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
        var lift = PeakAfterJump(field, null, 10f);
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
