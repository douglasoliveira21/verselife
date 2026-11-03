using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Vehicles;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class VehiclePhysicsTests
{
    private const float Dt = 1f / 30f;

    private static VehicleState Run(VehicleInput input, float seconds, VehicleState? state = null, float maxSpeed = 20f, Func<float, float, float>? terrain = null, float sea = float.NegativeInfinity, float limit = float.PositiveInfinity)
    {
        state ??= new VehicleState();
        for (var t = 0f; t < seconds; t += Dt) VehiclePhysics.Step(state, input, maxSpeed, Dt, terrain, sea, limit);
        return state;
    }

    [Fact]
    public void ThrottleAcceleratesTowardsButNeverPastTheMaximum()
    {
        var state = Run(new(1, 0, false), 30f, maxSpeed: 20f);
        Assert.InRange(state.Speed, 19f, 20f);
        var early = Run(new(1, 0, false), 1f);
        Assert.InRange(early.Speed, 4f, 8f);
    }

    [Fact]
    public void DrivingStraightMovesAlongMinusZAtYawZero()
    {
        var state = Run(new(1, 0, false), 3f);
        Assert.True(state.Z < -5f); Assert.InRange(state.X, -.01f, .01f);
    }

    [Fact]
    public void SteeringRightTurnsClockwiseAndLeftTheOtherWay()
    {
        var right = Run(new(1, 1, false), 3f); var left = Run(new(1, -1, false), 3f);
        Assert.True(right.X > 1f && right.Yaw < 0f);
        Assert.True(left.X < -1f && left.Yaw > 0f);
    }

    [Fact]
    public void ACarAtRestCannotTurnInPlace()
    {
        var state = Run(new(0, 1, false), 2f);
        Assert.Equal(0f, state.Yaw, 3); Assert.Equal(0f, state.X, 3); Assert.Equal(0f, state.Z, 3);
    }

    [Fact]
    public void BrakingStopsTheCarAndDoesNotReverseIt()
    {
        var state = Run(new(1, 0, false), 6f);
        Run(new(0, 0, true), 5f, state);
        Assert.Equal(0f, state.Speed, 3);
    }

    [Fact]
    public void ReleasingTheThrottleCoastsToAStop()
    {
        var state = Run(new(1, 0, false), 6f);
        var fast = state.Speed;
        Run(new(0, 0, false), 1f, state);
        Assert.True(state.Speed < fast && state.Speed > 0f);
        Run(new(0, 0, false), 60f, state);
        Assert.Equal(0f, state.Speed, 3);
    }

    [Fact]
    public void ReversingIsSlowerThanDrivingForwardAndPressingBackBrakesFirst()
    {
        var reverse = Run(new(-1, 0, false), 30f, maxSpeed: 20f);
        Assert.InRange(reverse.Speed, -7f, -6f);
        var state = Run(new(1, 0, false), 4f);
        VehiclePhysics.Step(state, new(-1, 0, false), 20f, Dt);
        Assert.True(state.Speed > 0f);
    }

    [Fact]
    public void SteeringGetsGentlerAtSpeed()
    {
        var slow = new VehicleState { Speed = 2f }; var fast = new VehicleState { Speed = 18f };
        Run(new(0, 1, false), 1f, slow); Run(new(0, 1, false), 1f, fast);
        Assert.True(MathF.Abs(slow.Steer) > MathF.Abs(fast.Steer));
    }

    [Fact]
    public void UphillSlowsTheCarDownhillSpeedsItUp()
    {
        var up = Run(new(1, 0, false), 4f, terrain: (_, z) => -z * .3f);
        var down = Run(new(1, 0, false), 4f, terrain: (_, z) => z * .3f);
        var flat = Run(new(1, 0, false), 4f);
        Assert.True(up.Speed < flat.Speed && down.Speed > flat.Speed);
    }

    [Fact]
    public void TheCarFollowsTheTerrainAndTiltsWithIt()
    {
        var state = Run(new(1, 0, false), 2f, terrain: (_, z) => -z * .2f);
        Assert.Equal(-state.Z * .2f, state.Y, 2);
        Assert.True(state.Pitch > .1f);
    }

    [Fact]
    public void DeepWaterStopsTheCar()
    {
        var state = Run(new(1, 0, false), 6f, terrain: (_, z) => z < -8f ? -5f : 0f, sea: 0f);
        Assert.True(state.Z >= -8.5f); Assert.Equal(0f, state.Speed, 3);
    }

    [Fact]
    public void TheEdgeOfTheWorldStopsTheCar()
    {
        var state = Run(new(1, 0, false), 20f, limit: 10f);
        Assert.Equal(-10f, state.Z, 3); Assert.Equal(0f, state.Speed, 3);
    }

    [Fact]
    public void BadInputIsIgnoredNotObeyed()
    {
        var state = Run(new(float.NaN, float.PositiveInfinity, false), 1f);
        Assert.True(float.IsFinite(state.X + state.Z + state.Yaw + state.Speed));
        var huge = Run(new(500f, -500f, false), 1f);
        var normal = Run(new(1f, -1f, false), 1f);
        Assert.Equal(normal.Speed, huge.Speed, 3);
    }

    [Fact]
    public void TheSimulationIsDeterministic()
    {
        var a = Run(new(1, .4f, false), 5f); var b = Run(new(1, .4f, false), 5f);
        Assert.Equal((a.X, a.Z, a.Yaw, a.Speed), (b.X, b.Z, b.Yaw, b.Speed));
    }
}

public sealed class VehicleFleetTests
{
    private static (VehicleFleet Fleet, FleetVehicle Car) Create()
    {
        var fleet = new VehicleFleet();
        return (fleet, fleet.Add(Guid.NewGuid(), "Carro", "car", 10f, 10f, 0f, 20f)!);
    }

    [Fact]
    public void VehiclesGetSequentialIdsAndTheSameKeyIsNotAddedTwice()
    {
        var fleet = new VehicleFleet(); var key = Guid.NewGuid();
        var first = fleet.Add(key, "A", "car", 0, 0, 0, 20f); var second = fleet.Add(Guid.NewGuid(), "B", "car", 0, 0, 0, 20f);
        Assert.Equal((1, 2), (first!.Id, second!.Id));
        Assert.Null(fleet.Add(key, "A", "car", 0, 0, 0, 20f));
        Assert.True(fleet.Contains(key));
    }

    [Fact]
    public void TheFleetHasAHardCap()
    {
        var fleet = new VehicleFleet();
        for (var i = 0; i < VehicleFleet.MaxVehicles; i++) Assert.NotNull(fleet.Add(Guid.NewGuid(), "x", "car", 0, 0, 0, 20f));
        Assert.Null(fleet.Add(Guid.NewGuid(), "extra", "car", 0, 0, 0, 20f));
    }

    [Fact]
    public void MaxSpeedIsKeptInASaneRange()
    {
        var fleet = new VehicleFleet();
        Assert.Equal(60f, fleet.Add(Guid.NewGuid(), "fast", "car", 0, 0, 0, 9999f)!.MaxSpeed);
        Assert.Equal(3f, fleet.Add(Guid.NewGuid(), "slow", "car", 0, 0, 0, -5f)!.MaxSpeed);
        Assert.Equal(20f, fleet.Add(Guid.NewGuid(), "nan", "car", 0, 0, 0, float.NaN)!.MaxSpeed);
    }

    [Fact]
    public void EnteringNeedsRangeAnEmptySeatAndNoOtherCar()
    {
        var (fleet, car) = Create();
        Assert.Equal(EnterResult.TooFar, fleet.Enter(1, car.Id, 30f, 0f, 30f));
        Assert.Equal(EnterResult.NoSuchVehicle, fleet.Enter(1, 99, 10f, 0f, 10f));
        Assert.Equal(EnterResult.Ok, fleet.Enter(1, car.Id, 11f, 0f, 11f));
        Assert.Equal(EnterResult.Occupied, fleet.Enter(2, car.Id, 11f, 0f, 11f));
        var other = fleet.Add(Guid.NewGuid(), "Outro", "car", 11f, 11f, 0f, 20f)!;
        Assert.Equal(EnterResult.AlreadyDriving, fleet.Enter(1, other.Id, 11f, 0f, 11f));
        Assert.Same(car, fleet.VehicleOf(1));
    }

    [Fact]
    public void EnteringWithANonFinitePositionIsRefused()
    {
        var (fleet, car) = Create();
        Assert.Equal(EnterResult.TooFar, fleet.Enter(1, car.Id, float.NaN, 0f, 0f));
    }

    [Fact]
    public void OnlyTheDriverControlsTheCarAndInputIsClamped()
    {
        var (fleet, car) = Create();
        fleet.Enter(1, car.Id, 10f, 0f, 10f);
        Assert.False(fleet.SetInput(2, 1f, 0f, false));
        Assert.True(fleet.SetInput(1, 50f, -50f, false));
        Assert.Equal(new VehicleInput(1f, -1f, false), car.Input);
        fleet.SetInput(1, float.NaN, float.NaN, true);
        Assert.Equal(new VehicleInput(0f, 0f, true), car.Input);
    }

    [Fact]
    public void ExitingPlacesThePlayerBesideTheCarAndFreesTheSeat()
    {
        var (fleet, car) = Create();
        Assert.False(fleet.Exit(1, null, out _, out _, out _));
        fleet.Enter(1, car.Id, 10f, 0f, 10f);
        Assert.True(fleet.Exit(1, (_, _) => 3f, out var x, out var y, out var z));
        Assert.Equal(0, car.DriverPeer); Assert.Equal(3f, y);
        Assert.InRange(MathF.Sqrt((x - 10f) * (x - 10f) + (z - 10f) * (z - 10f)), 2f, 2.5f);
        Assert.Equal(EnterResult.Ok, fleet.Enter(2, car.Id, 10f, 0f, 10f));
    }

    [Fact]
    public void ReleasingADisconnectedDriverFreesTheCar()
    {
        var (fleet, car) = Create();
        fleet.Enter(7, car.Id, 10f, 0f, 10f); fleet.SetInput(7, 1f, 0f, false);
        fleet.Release(7);
        Assert.Equal(0, car.DriverPeer); Assert.Equal(default, car.Input);
        fleet.Release(7);
    }

    [Fact]
    public void ADrivenCarMovesAndAnAbandonedOneRollsToAStop()
    {
        var (fleet, car) = Create();
        fleet.Enter(1, car.Id, 10f, 0f, 10f); fleet.SetInput(1, 1f, 0f, false);
        for (var i = 0; i < 90; i++) fleet.Tick(1f / 30f, null, float.NegativeInfinity, 500f);
        var z = car.State.Z;
        Assert.True(z < 0f && car.State.Speed > 5f);
        fleet.Exit(1, null, out _, out _, out _);
        for (var i = 0; i < 300; i++) fleet.Tick(1f / 30f, null, float.NegativeInfinity, 500f);
        Assert.Equal(0f, car.State.Speed, 3); Assert.True(car.State.Z < z);
    }

    [Fact]
    public void SnapshotListsEveryVehicleWithItsDriver()
    {
        var (fleet, car) = Create();
        fleet.Enter(4, car.Id, 10f, 0f, 10f);
        var snapshot = Assert.Single(fleet.Snapshot());
        Assert.Equal((car.Id, 4, 10f, 10f), (snapshot.Id, snapshot.DriverPeer, snapshot.X, snapshot.Z));
    }
}

public sealed class VehicleCodecTests
{
    [Fact]
    public void RoundTripKeepsPositionsExactlyAndAnglesClosely()
    {
        var input = new[] { new VehicleSnapshot(3, 512.25f, 12.5f, -400.75f, 1.2f, -.3f, 14.5f, 9), new VehicleSnapshot(4, -1f, 0f, 0f, -3f, 0f, -2f, 0) };
        var bytes = VehicleCodec.Encode(77, input);
        Assert.True(VehicleCodec.TryDecode(bytes, out var tick, out var output));
        Assert.Equal(77u, tick); Assert.Equal(2, output.Count);
        Assert.Equal((3, 512.25f, 12.5f, -400.75f, 14.5f, 9), (output[0].Id, output[0].X, output[0].Y, output[0].Z, output[0].Speed, output[0].DriverPeer));
        Assert.Equal(1.2f, output[0].Yaw, .001f); Assert.Equal(-3f, output[1].Yaw, .001f);
    }

    [Fact]
    public void AnEmptySnapshotIsValid() => Assert.True(VehicleCodec.TryDecode(VehicleCodec.Encode(1, []), out _, out var list) && list.Count == 0);

    [Theory]
    [InlineData(0)] [InlineData(5)] [InlineData(7)]
    public void TruncatedOrOversizedPayloadsAreRejected(int extra)
    {
        var bytes = VehicleCodec.Encode(1, [new VehicleSnapshot(1, 0, 0, 0, 0, 0, 0, 0)]);
        var broken = extra == 0 ? bytes[..^1] : [.. bytes, .. new byte[extra]];
        Assert.False(VehicleCodec.TryDecode(broken, out _, out _));
    }

    [Fact]
    public void ACountLargerThanTheCapIsRejected()
    {
        var bytes = new byte[6]; bytes[4] = 0xFF; bytes[5] = 0xFF;
        Assert.False(VehicleCodec.TryDecode(bytes, out _, out _));
    }

    [Fact]
    public void NonFiniteValuesAreRejected()
    {
        var bytes = VehicleCodec.Encode(1, [new VehicleSnapshot(1, float.NaN, 0, 0, 0, 0, 0, 0)]);
        Assert.False(VehicleCodec.TryDecode(bytes, out _, out _));
    }
}
