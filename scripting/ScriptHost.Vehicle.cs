using MoonSharp.Interpreter;

namespace LifeVerse.Scripting;

/// <summary>A vehicle as a script sees it.</summary>
public sealed record VehicleData(int Id, string Name, string Type, Vec3 Position, float Yaw, float Speed, bool Occupied);

public sealed partial class ScriptHost
{
    public const int MaxVehicleCommandsPerSecond = 10;

    /// <summary>vehicle.*: the owner's vehicles that nobody sits in, driven by a script whose owner gave it CONTROL_VEHICLES (editor, tab Permissões). A script never touches a car of somebody else or one with a driver.</summary>
    private void InstallVehicleApi(Instance instance)
    {
        var script = instance.Sandbox.Script; var vehicle = new Table(script); script.Globals["vehicle"] = vehicle;
        DynValue Fn(Func<CallbackArguments, DynValue> body) => DynValue.NewCallback((_, args) =>
        {
            instance.Metrics.ApiCalls++;
            if (!_permissions.Has(instance.Definition.Id, Guid.Empty, ScriptPermission.ControlVehicles)) throw new ScriptRuntimeException("vehicle needs the CONTROL_VEHICLES permission: the owner of the object gives it in the editor (tab Permissões)");
            return body(args);
        });
        DynValue Describe(VehicleData v) { var t = new Table(script); t["id"] = v.Id; t["name"] = v.Name; t["type"] = v.Type; t["x"] = v.Position.X; t["y"] = v.Position.Y; t["z"] = v.Position.Z; t["yaw"] = v.Yaw; t["speed"] = v.Speed; t["occupied"] = v.Occupied; return DynValue.NewTable(t); }
        void Rate()
        {
            var now = world.Now; while (instance.VehicleCommands.Count > 0 && now - instance.VehicleCommands.Peek() > 1) instance.VehicleCommands.Dequeue();
            if (instance.VehicleCommands.Count >= MaxVehicleCommandsPerSecond) throw new ScriptRuntimeException($"vehicle: at most {MaxVehicleCommandsPerSecond} commands a second"); instance.VehicleCommands.Enqueue(now);
        }
        int Id(CallbackArguments args) => (int)Math.Floor(Number(args, 0));
        vehicle["list"] = Fn(_ => { var table = new Table(script); var index = 1; foreach (var v in world.GetVehicles(instance.Definition.OwnerUserId).Take(64)) table[index++] = Describe(v); return DynValue.NewTable(table); });
        vehicle["get"] = Fn(args => world.GetVehicles(instance.Definition.OwnerUserId).FirstOrDefault(v => v.Id == Id(args)) is { } v ? Describe(v) : DynValue.Nil);
        vehicle["drive"] = Fn(args => { Rate(); return DynValue.NewBoolean(world.DriveVehicle(instance.Definition, Id(args), Number(args, 1), Number(args, 2), args.Count > 3 && args[3].CastToBool())); });
        vehicle["driveTo"] = Fn(args =>
        {
            Rate(); var x = Number(args, 1); var z = Number(args, 2); if (MathF.Abs(x) > MaxNpcDistance || MathF.Abs(z) > MaxNpcDistance) throw new ScriptRuntimeException($"coordinates are within {MaxNpcDistance:N0} m of the origin");
            return DynValue.NewBoolean(world.DriveVehicleTo(instance.Definition, Id(args), x, z, Number(args, 3, 8)));
        });
        vehicle["stop"] = Fn(args => { Rate(); return DynValue.NewBoolean(world.StopVehicle(instance.Definition, Id(args))); });
    }

    /// <summary>The vehicles a script was driving are let go (it ended, stopped or restarted): they brake to a stop.</summary>
    private void CloseVehicles(Instance instance) => world.ReleaseVehicles(instance.Definition);

    /// <summary>An autopilot a script started ended: <c>on_vehicle_arrived</c> with <c>arrived</c> true, or false when the car was stuck (it did not move for six seconds).</summary>
    public void VehicleArrived(Guid scriptId, VehicleData vehicle, bool arrived)
    {
        if (Find(scriptId) is not { } instance) return;
        Enqueue(new ScriptEvent(instance.Definition.ObjectId, "vehicle_arrived", new Dictionary<string, object?> { ["vehicle"] = VehicleTable(vehicle), ["arrived"] = arrived }, scriptId));
    }

    /// <summary>Somebody got into or out of a vehicle: the scripts of the vehicle's OWNER hear <c>on_vehicle_enter</c> / <c>on_vehicle_exit</c> (nobody else learns who drives what).</summary>
    public void VehicleSeatChanged(Guid ownerUserId, bool entered, VehicleData vehicle, Guid avatarId, string avatarName)
    {
        var data = new Dictionary<string, object?> { ["vehicle"] = VehicleTable(vehicle), ["avatar_id"] = avatarId, ["avatar_name"] = avatarName, ["avatar"] = new Dictionary<string, object?> { ["id"] = avatarId, ["name"] = avatarName } };
        var name = entered ? "vehicle_enter" : "vehicle_exit";
        foreach (var objectId in ObjectsWanting(name).ToList()) if (world.GetObject(objectId) is { } o && o.OwnerUserId == ownerUserId) Enqueue(new ScriptEvent(objectId, name, data));
    }

    private static Dictionary<string, object?> VehicleTable(VehicleData v) => new() { ["id"] = v.Id, ["name"] = v.Name, ["type"] = v.Type, ["x"] = v.Position.X, ["y"] = v.Position.Y, ["z"] = v.Position.Z, ["yaw"] = v.Yaw, ["speed"] = v.Speed, ["occupied"] = v.Occupied };
}
