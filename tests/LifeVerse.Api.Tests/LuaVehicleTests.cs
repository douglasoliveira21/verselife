using LifeVerse.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>vehicle.*: only with CONTROL_VEHICLES, only the owner's cars, limited per second, released when the script ends, and the owner's scripts hear who gets in or out and when an autopilot ends.</summary>
public sealed class LuaVehicleTests
{
    private static readonly Guid Owner = Guid.NewGuid(), Other = Guid.NewGuid(), Ana = Guid.NewGuid();

    private sealed class World : IScriptWorld
    {
        public double Now { get; set; } = 100; public Dictionary<Guid, ScriptObjectState> Objects { get; } = []; public List<string> Said { get; } = []; public List<string> Calls { get; } = [];
        public ScriptObjectState? GetObject(Guid id) => Objects.GetValueOrDefault(id);
        public void ApplyChange(ScriptDefinition script, ObjectChange change) { }
        public void Say(ScriptDefinition script, string text) => Said.Add(text);
        public IReadOnlyList<VehicleData> GetVehicles(Guid ownerUserId) { Calls.Add($"list {(ownerUserId == Owner ? "owner" : "other")}"); return ownerUserId == Owner ? [new(1, "Jipe", "car", new Vec3(5, 1, 6), .5f, 2f, false), new(2, "Moto", "bike", new Vec3(9, 1, 9), 0, 0, true)] : []; }
        public bool DriveVehicle(ScriptDefinition script, int vehicle, float throttle, float steer, bool brake) { Calls.Add($"drive {vehicle} {throttle} {steer} {brake}"); return vehicle == 1; }
        public bool DriveVehicleTo(ScriptDefinition script, int vehicle, float x, float z, float speed) { Calls.Add($"driveTo {vehicle} {x} {z} {speed}"); return vehicle == 1; }
        public bool StopVehicle(ScriptDefinition script, int vehicle) { Calls.Add($"stop {vehicle}"); return true; }
        public void ReleaseVehicles(ScriptDefinition script) => Calls.Add("release");
    }

    private static (ScriptHost Host, World World, Guid Object, Guid Script, List<ScriptReport> Reports) Rig(string source, bool allowed = true, Guid? objectOwner = null)
    {
        var world = new World(); var obj = Guid.NewGuid(); var owner = objectOwner ?? Owner; world.Objects[obj] = new ScriptObjectState(obj, owner, "Garagem", new Vec3(0, 0, 0), default, new Vec3(1, 1, 1));
        var host = new ScriptHost(world, new ScriptHostOptions(MillisecondsPerEvent: 2000)); var reports = new List<ScriptReport>(); host.Report += reports.Add;
        var script = Guid.NewGuid(); host.Sync([new ScriptDefinition(script, obj, owner, "garagem", source, 1)]); if (allowed) host.Permissions.Grant(script, Guid.Empty, ScriptPermission.ControlVehicles); host.Process(500); return (host, world, obj, script, reports);
    }

    private static void Touch(ScriptHost host, Guid obj) { host.Enqueue(new ScriptEvent(obj, "touch")); host.Process(500); }

    [Fact]
    public void NothingWorksWithoutTheOwnersPermission()
    {
        var (host, world, obj, _, reports) = Rig("function on_touch() vehicle.drive(1, 1, 0, false) end", allowed: false);
        Touch(host, obj); Assert.Contains("CONTROL_VEHICLES", Assert.Single(reports).Message); Assert.Empty(world.Calls);
    }

    [Fact]
    public void AScriptListsDrivesAndStopsItsOwnersCars()
    {
        var (host, world, obj, _, reports) = Rig("function on_touch() local all = vehicle.list() chat.say(#all .. ' ' .. all[1].name .. ' ' .. tostring(all[2].occupied)) chat.say(tostring(vehicle.get(1).speed) .. tostring(vehicle.get(7)))"
            + " vehicle.drive(1, 1, -1, true) vehicle.driveTo(1, 120, -40, 10) chat.say(tostring(vehicle.drive(2, 1, 0))) vehicle.stop(1) end");
        Touch(host, obj); Assert.Empty(reports); Assert.Equal(["2 Jipe true", "2nil", "false"], world.Said);
        Assert.Equal(["list owner", "list owner", "list owner", "drive 1 1 -1 True", "driveTo 1 120 -40 10", "drive 2 1 0 False", "stop 1"], world.Calls);
    }

    [Fact]
    public void TheScriptOfSomebodyWhoOwnsNoCarsSeesNone()
    {
        var (host, world, obj, _, _) = Rig("function on_touch() chat.say(#vehicle.list()) end", objectOwner: Other); host.Permissions.Grant(host.Scripts.Single().ScriptId, Guid.Empty, ScriptPermission.ControlVehicles);
        Touch(host, obj); Assert.Equal(["0"], world.Said); Assert.Equal(["list other"], world.Calls);
    }

    [Theory]
    [InlineData("vehicle.driveTo(1, 99999, 0, 5)")]
    [InlineData("vehicle.driveTo(1, 0/0, 0, 5)")]
    [InlineData("vehicle.drive('x', 1, 0)")]
    public void BadInputIsAnError(string call)
    {
        var (host, world, obj, _, reports) = Rig($"function on_touch() {call} end"); Touch(host, obj);
        Assert.Single(reports); Assert.DoesNotContain(world.Calls, c => c.StartsWith("driveTo", StringComparison.Ordinal) || c.StartsWith("drive ", StringComparison.Ordinal));
    }

    [Fact]
    public void ACommandFloodIsRefused_AndLaterItMayAgain()
    {
        var (host, world, obj, _, reports) = Rig("function on_touch() vehicle.drive(1, 1, 0, false) end");
        for (var i = 0; i < ScriptHost.MaxVehicleCommandsPerSecond; i++) Touch(host, obj); Assert.Empty(reports); Touch(host, obj); Assert.Contains("at most", Assert.Single(reports).Message);
        world.Now += 1.5; reports.Clear(); Touch(host, obj); Assert.Empty(reports);
    }

    [Fact]
    public void TheCarsAreLetGoWhenTheScriptStopsOrRestarts()
    {
        var (host, world, _, script, _) = Rig("function on_touch() end");
        host.Stop(script); Assert.Contains("release", world.Calls); world.Calls.Clear(); host.Restart(script); Assert.Contains("release", world.Calls);
    }

    [Fact]
    public void TheOwnersScriptsHearWhoGetsInOrOut_AndOnlyTheOwners()
    {
        const string Source = "function on_vehicle_enter(e) chat.say('entrou ' .. e.avatar.name .. ' ' .. e.vehicle.name) end function on_vehicle_exit(e) chat.say('saiu ' .. e.avatar_name) end function on_vehicle_arrived(e) chat.say('chegou ' .. tostring(e.arrived) .. ' ' .. e.vehicle.id) end";
        var (host, world, obj, script, reports) = Rig(Source); var (hostOther, worldOther, _, _, _) = Rig(Source, objectOwner: Other);
        var car = new VehicleData(1, "Jipe", "car", new Vec3(5, 1, 6), 0, 0, true);
        host.VehicleSeatChanged(Owner, true, car, Ana, "Ana"); host.VehicleSeatChanged(Owner, false, car, Ana, "Ana"); host.VehicleArrived(script, car, true); host.VehicleArrived(script, car with { Id = 2 }, false); host.VehicleArrived(Guid.NewGuid(), car, true); host.Process(500);
        Assert.Empty(reports); Assert.Equal(["entrou Ana Jipe", "saiu Ana", "chegou true 1", "chegou false 2"], world.Said);
        hostOther.VehicleSeatChanged(Owner, true, car, Ana, "Ana"); hostOther.Process(500); Assert.Empty(worldOther.Said);                                                  // the car is not theirs
    }
}
