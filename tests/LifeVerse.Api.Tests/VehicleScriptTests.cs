using LifeVerse.Shared.Vehicles;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Vehicles driven by scripts: only the owner's scripts, only while nobody sits in the car, a held input that lapses, an autopilot that arrives, and the hand-over when somebody gets in.</summary>
public sealed class VehicleScriptTests
{
    private static readonly Guid Owner = Guid.NewGuid(), Stranger = Guid.NewGuid(), Script = Guid.NewGuid(), OtherScript = Guid.NewGuid();
    private static float Flat(float x, float z) => 0f;

    private static (VehicleFleet Fleet, FleetVehicle Car) Rig() { var fleet = new VehicleFleet(); return (fleet, fleet.Add(Guid.NewGuid(), "Carro", "car", 0, 0, 0, 20f, Flat, Owner)!); }

    private static void Run(VehicleFleet fleet, double from, double seconds, Action<FleetVehicle, Guid, bool>? arrived = null)
    {
        for (var t = 0.0; t < seconds; t += .05) fleet.Tick(.05f, Flat, -100, 1000, from + t, arrived);
    }

    [Fact]
    public void OnlyTheOwnersScriptsDriveOnlyEmptyCars()
    {
        var (fleet, car) = Rig();
        Assert.True(fleet.Drive(Script, Owner, car.Id, 1, 0, false, 0));
        Assert.False(fleet.Drive(Script, Stranger, car.Id, 1, 0, false, 0)); Assert.False(fleet.DriveTo(Script, Stranger, car.Id, 5, 5, 3)); Assert.False(fleet.StopScript(Script, Stranger, car.Id));   // somebody else's car
        Assert.False(fleet.Drive(Script, Guid.Empty, car.Id, 1, 0, false, 0)); Assert.False(fleet.Drive(Script, Owner, 999, 1, 0, false, 0));
        Assert.Equal(EnterResult.Ok, fleet.Enter(7, car.Id, 0, 0, 0)); Assert.False(fleet.Drive(Script, Owner, car.Id, 1, 0, false, 0));                         // somebody sits in it
    }

    [Fact]
    public void AHeldInputMovesTheCarForASecondAndThenItBrakes()
    {
        var (fleet, car) = Rig(); fleet.Drive(Script, Owner, car.Id, 1, 0, false, now: 0);
        Run(fleet, 0, .9); var moving = car.State.Speed; Assert.True(moving > 1f); Assert.True(car.State.Z < 0);                  // forward is -Z
        Run(fleet, 1, 8); Assert.Equal(0f, car.State.Speed);                                                                       // the command lapsed: it braked to a stop
    }

    [Fact]
    public void TheAutopilotDrivesToThePointAndTellsWhoSentIt()
    {
        var (fleet, car) = Rig(); var arrivals = new List<(Guid Script, bool Ok)>();
        Assert.True(fleet.DriveTo(Script, Owner, car.Id, 30, -40, 10));
        Run(fleet, 0, 40, (v, by, ok) => arrivals.Add((by, ok)));
        Assert.Equal([(Script, true)], arrivals); var dx = car.State.X - 30; var dz = car.State.Z + 40; Assert.True(dx * dx + dz * dz <= 5 * 5, $"stopped {MathF.Sqrt(dx * dx + dz * dz)} m from the target");
        Run(fleet, 40, 5); Assert.Equal(0f, car.State.Speed); Assert.Single(arrivals);                                           // arriving is told once, and the car rests
    }

    [Fact]
    public void ACarThatCannotMoveIsDeclaredStuck()
    {
        var fleet = new VehicleFleet(); var car = fleet.Add(Guid.NewGuid(), "Preso", "car", 0, 0, 0, 20f, Flat, Owner)!; var arrivals = new List<bool>();
        fleet.DriveTo(Script, Owner, car.Id, 0, -500, 10);
        for (var t = 0.0; t < 12; t += .05) { fleet.Tick(.05f, Flat, 100, 1000, t, (v, by, ok) => arrivals.Add(ok)); }      // the "sea" is everywhere: it never gets going
        Assert.Equal([false], arrivals);
    }

    [Fact]
    public void ADriverTakesTheWheelAndTheScriptLetsGo()
    {
        var (fleet, car) = Rig(); fleet.DriveTo(Script, Owner, car.Id, 0, -80, 10); Run(fleet, 0, 2);
        Assert.Equal(EnterResult.Ok, fleet.Enter(5, car.Id, car.State.X, car.State.Y, car.State.Z)); fleet.Tick(.05f, Flat, -100, 1000, 3); Assert.Equal(Guid.Empty, car.Controller); Assert.Null(car.TargetX);
    }

    [Fact]
    public void AScriptThatEndsLetsGoOfItsCarsOnly()
    {
        var fleet = new VehicleFleet(); var a = fleet.Add(Guid.NewGuid(), "A", "car", 0, 0, 0, 20f, Flat, Owner)!; var b = fleet.Add(Guid.NewGuid(), "B", "car", 9, 0, 0, 20f, Flat, Owner)!;
        fleet.DriveTo(Script, Owner, a.Id, 0, -50, 5); fleet.DriveTo(OtherScript, Owner, b.Id, 9, -50, 5);
        Assert.Equal(1, fleet.ReleaseScript(Script)); Assert.Null(a.TargetX); Assert.NotNull(b.TargetX);
        Assert.False(fleet.StopScript(OtherScript, Owner, a.Id)); Assert.True(fleet.StopScript(OtherScript, Owner, b.Id)); Assert.Null(b.TargetX);
    }

    [Theory]
    [InlineData(float.NaN, 5)]
    [InlineData(5, float.PositiveInfinity)]
    public void MadeUpNumbersAreRefused(float x, float z)
    {
        var (fleet, car) = Rig(); Assert.False(fleet.DriveTo(Script, Owner, car.Id, x, z, 5)); Assert.Null(car.TargetX);
        Assert.True(fleet.Drive(Script, Owner, car.Id, float.NaN, 5f, false, 0)); Assert.Equal((0f, 1f), (car.ScriptInput.Throttle, car.ScriptInput.Steer));          // clamped, not trusted
    }
}
