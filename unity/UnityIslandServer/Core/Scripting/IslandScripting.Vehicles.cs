using LifeVerse.Scripting;
using LifeVerse.Shared.Vehicles;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>Vehicles as scripts see them: the fleet lives in the shared library, the island server simulates it, and here it is tied to the scripts of the vehicle's owner (they list, drive and send them, and hear who gets in or out).</summary>
public sealed partial class IslandScripting
{
    /// <summary>The fleet of the island. Set by the server; null in tests that have none.</summary>
    public VehicleFleet? Fleet { get; set; }

    private static VehicleData ToData(FleetVehicle v) => new(v.Id, v.Name, v.Type, new Vec3(v.State.X, v.State.Y, v.State.Z), v.State.Yaw, v.State.Speed, v.DriverPeer != 0);

    public IReadOnlyList<VehicleData> GetVehicles(Guid ownerUserId) => Fleet is null || ownerUserId == Guid.Empty ? [] : [.. Fleet.Vehicles.Where(v => v.Owner == ownerUserId).Select(ToData)];
    public bool DriveVehicle(ScriptDefinition script, int vehicle, float throttle, float steer, bool brake) => Fleet?.Drive(script.Id, script.OwnerUserId, vehicle, throttle, steer, brake, Now) ?? false;
    public bool DriveVehicleTo(ScriptDefinition script, int vehicle, float x, float z, float speed) => Fleet?.DriveTo(script.Id, script.OwnerUserId, vehicle, x, z, speed) ?? false;
    public bool StopVehicle(ScriptDefinition script, int vehicle) => Fleet?.StopScript(script.Id, script.OwnerUserId, vehicle) ?? false;
    public void ReleaseVehicles(ScriptDefinition script) => Fleet?.ReleaseScript(script.Id);

    /// <summary>An autopilot ended (the server's fleet tick reports it): the script that sent the car hears it.</summary>
    public void VehicleArrived(FleetVehicle car, Guid byScript, bool arrived) => _host.VehicleArrived(byScript, ToData(car), arrived);

    /// <summary>Somebody got in or out of a vehicle: the scripts of the vehicle's owner hear it.</summary>
    public void VehicleSeat(FleetVehicle car, bool entered, AvatarPresence who) => _host.VehicleSeatChanged(car.Owner, entered, ToData(car), who.AvatarId, who.Name);
}
