using LifeVerse.Shared.Networking;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class NetworkProtocolTests
{
    [Fact]
    public void SnapshotCodec_RoundTripsQuantizedState()
    {
        var bytes = SnapshotCodec.Encode(42, [new PlayerSnapshot(7, 12.345f, 1f, -2.5f, 3.2f, 0f, -1f, 1.2f, NetworkMotionState.Run, 99)]);
        Assert.True(SnapshotCodec.TryDecode(bytes, out var snapshot));
        var player = Assert.Single(snapshot.Players);
        Assert.Equal((uint)42, snapshot.ServerTick);
        Assert.Equal(7, player.PeerId);
        Assert.InRange(player.X, 12.33f, 12.35f);
        Assert.Equal(NetworkMotionState.Run, player.Motion);
        Assert.Equal((uint)99, player.LastProcessedInput);
    }

    [Fact]
    public void SnapshotCodec_RejectsMalformedPayload()
    {
        Assert.False(SnapshotCodec.TryDecode([1, 2, 3], out _));
        var valid = SnapshotCodec.Encode(1, []);
        Assert.False(SnapshotCodec.TryDecode([.. valid, 0], out _));
    }

    [Fact]
    public void Movement_ClampsInputAndRejectsClientPositionByDesign()
    {
        var state = new AuthoritativePlayerState();
        for (var i = 0; i < 60; i++) AuthoritativeMovement.Step(state, new MovementInput((uint)i, 100, 100, InputFlags.Run), 1f / 60f);
        var horizontalSpeed = MathF.Sqrt(state.VelocityX * state.VelocityX + state.VelocityZ * state.VelocityZ);
        Assert.InRange(horizontalSpeed, 0, AuthoritativeMovement.RunSpeed + .001f);
        Assert.InRange(state.X, 0, AuthoritativeMovement.RunSpeed);
    }

    [Fact]
    public void Movement_DoesNotAllowFlightWithoutPermission()
    {
        var state = new AuthoritativePlayerState { CanFly = false };
        var motion = AuthoritativeMovement.Step(state, new MovementInput(1, 0, 0, InputFlags.Fly), 1f / 60f);
        Assert.False(state.Flying);
        Assert.NotEqual(NetworkMotionState.Fly, motion);
    }
}
