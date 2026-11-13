namespace LifeVerse.Shared.Vehicles;

public sealed class FleetVehicle
{
    public required int Id { get; init; }
    public required Guid Key { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required float MaxSpeed { get; init; }
    public VehicleState State { get; } = new();
    public VehicleInput Input { get; set; }
    /// Network peer id of the driver, 0 when the seat is empty.
    public int DriverPeer { get; set; }
    /// The user who owns the vehicle (only the scripts of its owner may drive it).
    public Guid Owner { get; set; }
    /// Driven by a script while nobody sits in it: a held input (it lapses a second after the last command) or an autopilot toward a point.
    public Guid Controller { get; set; }
    public VehicleInput ScriptInput { get; set; }
    public double ScriptUntil { get; set; }
    public float? TargetX { get; set; }
    public float? TargetZ { get; set; }
    public float TargetSpeed { get; set; }
    public double StuckFor { get; set; }
}

public readonly record struct VehicleSnapshot(int Id, float X, float Y, float Z, float Yaw, float Pitch, float Speed, int DriverPeer);

public enum EnterResult { Ok, NoSuchVehicle, TooFar, Occupied, AlreadyDriving }

/// Every vehicle on one island and who drives it. Pure bookkeeping plus the physics step, so the island server
/// stays thin and the rules (range, one driver, one seat per player) are tested without Godot.
public sealed class VehicleFleet
{
    public const float EnterRange = 4f;
    public const int MaxVehicles = 64;
    public const float ExitDistance = 2.2f;
    public const float DriverHeight = .35f;
    private readonly Dictionary<int, FleetVehicle> _byId = [];
    private readonly Dictionary<Guid, FleetVehicle> _byKey = [];
    private int _nextId = 1;

    public IReadOnlyCollection<FleetVehicle> Vehicles => _byId.Values;

    public bool Contains(Guid key) => _byKey.ContainsKey(key);

    /// Returns null when the island already holds the maximum or the vehicle is already known.
    public FleetVehicle? Add(Guid key, string name, string type, float x, float z, float yaw, float maxSpeed, Func<float, float, float>? terrainHeight = null, Guid owner = default)
    {
        if (_byId.Count >= MaxVehicles || _byKey.ContainsKey(key)) return null;
        var vehicle = new FleetVehicle { Id = _nextId++, Key = key, Name = name, Type = type, MaxSpeed = Math.Clamp(float.IsFinite(maxSpeed) ? maxSpeed : 20f, 3f, 60f), Owner = owner };
        vehicle.State.X = x; vehicle.State.Z = z; vehicle.State.Yaw = yaw; vehicle.State.Y = terrainHeight?.Invoke(x, z) ?? 0f;
        _byId[vehicle.Id] = vehicle; _byKey[key] = vehicle;
        return vehicle;
    }

    public FleetVehicle? Get(int id) => _byId.GetValueOrDefault(id);
    public FleetVehicle? VehicleOf(int peer) => peer == 0 ? null : _byId.Values.FirstOrDefault(v => v.DriverPeer == peer);

    public EnterResult Enter(int peer, int vehicleId, float playerX, float playerY, float playerZ)
    {
        if (VehicleOf(peer) is not null) return EnterResult.AlreadyDriving;
        if (!_byId.TryGetValue(vehicleId, out var vehicle)) return EnterResult.NoSuchVehicle;
        if (vehicle.DriverPeer != 0) return EnterResult.Occupied;
        var dx = vehicle.State.X - playerX; var dy = vehicle.State.Y - playerY; var dz = vehicle.State.Z - playerZ;
        if (!float.IsFinite(dx + dy + dz) || dx * dx + dy * dy + dz * dz > EnterRange * EnterRange) return EnterResult.TooFar;
        vehicle.DriverPeer = peer; vehicle.Input = default;
        return EnterResult.Ok;
    }

    public bool SetInput(int peer, float throttle, float steer, bool brake)
    {
        if (VehicleOf(peer) is not { } vehicle) return false;
        vehicle.Input = new VehicleInput(float.IsFinite(throttle) ? Math.Clamp(throttle, -1f, 1f) : 0f, float.IsFinite(steer) ? Math.Clamp(steer, -1f, 1f) : 0f, brake);
        return true;
    }

    /// Frees the seat and returns where the driver steps out: beside the car, on the ground.
    public bool Exit(int peer, Func<float, float, float>? terrainHeight, out float x, out float y, out float z)
    {
        x = y = z = 0;
        if (VehicleOf(peer) is not { } vehicle) return false;
        vehicle.DriverPeer = 0; vehicle.Input = default;
        var yaw = vehicle.State.Yaw;
        x = vehicle.State.X - MathF.Cos(yaw) * ExitDistance; z = vehicle.State.Z + MathF.Sin(yaw) * ExitDistance;
        y = terrainHeight?.Invoke(x, z) ?? vehicle.State.Y;
        return true;
    }

    public void Release(int peer) { if (VehicleOf(peer) is { } vehicle) { vehicle.DriverPeer = 0; vehicle.Input = default; } }

    // ---- control by scripts: only the owner's scripts, only while the seat is empty ----------------------------------------------------

    public const float ArriveWithin = 3f, ScriptInputSeconds = 1f, StuckSeconds = 6f, TurnInPlaceAbove = 1.2f, SlowSpeed = 2f;

    private FleetVehicle? Controlled(Guid owner, int id) => _byId.TryGetValue(id, out var v) && owner != Guid.Empty && v.Owner == owner && v.DriverPeer == 0 ? v : null;

    /// <summary>Holds a throttle, steering and brake on an empty vehicle of the owner for one second (the script repeats the command to keep driving).</summary>
    public bool Drive(Guid script, Guid owner, int id, float throttle, float steer, bool brake, double now)
    {
        if (Controlled(owner, id) is not { } v) return false;
        v.Controller = script; v.TargetX = v.TargetZ = null; v.StuckFor = 0;
        v.ScriptInput = new VehicleInput(float.IsFinite(throttle) ? Math.Clamp(throttle, -1f, 1f) : 0f, float.IsFinite(steer) ? Math.Clamp(steer, -1f, 1f) : 0f, brake); v.ScriptUntil = now + ScriptInputSeconds; return true;
    }

    /// <summary>Sends an empty vehicle of the owner driving to a point (in a straight line: it does not go around obstacles) at up to a speed.</summary>
    public bool DriveTo(Guid script, Guid owner, int id, float x, float z, float speed)
    {
        if (Controlled(owner, id) is not { } v || !float.IsFinite(x) || !float.IsFinite(z)) return false;
        v.Controller = script; v.TargetX = x; v.TargetZ = z; v.TargetSpeed = float.IsFinite(speed) ? Math.Clamp(speed, 1f, v.MaxSpeed) : MathF.Min(8f, v.MaxSpeed); v.ScriptUntil = 0; v.StuckFor = 0; return true;
    }

    public bool StopScript(Guid script, Guid owner, int id)
    {
        if (Controlled(owner, id) is not { } v || v.Controller != script) return false;
        Release(v); return true;
    }

    /// <summary>Takes the vehicles a script was driving out of its hands (it ended, stopped or restarted): they brake to a halt.</summary>
    public int ReleaseScript(Guid script) { var held = _byId.Values.Where(v => v.Controller == script).ToList(); foreach (var v in held) Release(v); return held.Count; }

    private static void Release(FleetVehicle v) { v.Controller = Guid.Empty; v.TargetX = v.TargetZ = null; v.ScriptInput = default; v.ScriptUntil = 0; v.StuckFor = 0; }

    /// <summary>The throttle and steering that take a vehicle toward a point: turns toward it, slows for sharp turns and for the last metres, brakes at the end.</summary>
    public static VehicleInput Autopilot(VehicleState s, float maxSpeed, float targetX, float targetZ, float wanted)
    {
        var dx = targetX - s.X; var dz = targetZ - s.Z; var distance = MathF.Sqrt(dx * dx + dz * dz);
        var desiredYaw = MathF.Atan2(-dx, -dz); var error = desiredYaw - s.Yaw;
        while (error > MathF.PI) error -= 2 * MathF.PI; while (error < -MathF.PI) error += 2 * MathF.PI;
        var steer = Math.Clamp(-error * 1.6f, -1f, 1f);
        var speed = MathF.Min(wanted, maxSpeed);
        if (MathF.Abs(error) > TurnInPlaceAbove) speed = MathF.Min(speed, SlowSpeed);                       // a sharp turn first, slowly
        speed = MathF.Min(speed, MathF.Max(SlowSpeed, MathF.Sqrt(2 * 5f * MathF.Max(0, distance - ArriveWithin))));   // slows down to arrive without overshooting
        var throttle = Math.Clamp((speed - s.Speed) * .6f, -1f, 1f);
        return new VehicleInput(throttle, steer, throttle < -.5f);
    }

    /// <summary>A step of every vehicle. Empty ones follow a script (held input or autopilot) or brake to a stop; driven ones follow their driver. <paramref name="arrived"/> hears of each autopilot that ended, with the script that sent it (true: it arrived, false: it was stuck).</summary>
    public void Tick(float delta, Func<float, float, float>? terrainHeight, float seaLevel, float worldLimit, double now = 0, Action<FleetVehicle, Guid, bool>? arrived = null)
    {
        foreach (var vehicle in _byId.Values)
        {
            if (vehicle.DriverPeer != 0 && vehicle.Controller != Guid.Empty) Release(vehicle);          // somebody sat in it: the script lets go
            var input = vehicle.Input; var resting = MathF.Abs(vehicle.State.Speed) < .01f && MathF.Abs(vehicle.State.Steer) < .001f;
            if (vehicle.DriverPeer == 0)
            {
                if (vehicle.TargetX is { } tx && vehicle.TargetZ is { } tz)
                {
                    var dx = tx - vehicle.State.X; var dz = tz - vehicle.State.Z;
                    if (dx * dx + dz * dz <= ArriveWithin * ArriveWithin) { var by = vehicle.Controller; Release(vehicle); arrived?.Invoke(vehicle, by, true); input = new VehicleInput(0, 0, true); }
                    else
                    {
                        input = Autopilot(vehicle.State, vehicle.MaxSpeed, tx, tz, vehicle.TargetSpeed);
                        vehicle.StuckFor = MathF.Abs(vehicle.State.Speed) < .3f ? vehicle.StuckFor + delta : 0;
                        if (vehicle.StuckFor >= StuckSeconds) { var by = vehicle.Controller; Release(vehicle); arrived?.Invoke(vehicle, by, false); input = new VehicleInput(0, 0, true); }
                    }
                }
                else if (vehicle.ScriptUntil > now) input = vehicle.ScriptInput;
                else input = new VehicleInput(0, 0, true);                                              // an empty car brakes to a stop
                if (resting && input.Throttle == 0 && input.Steer == 0) { vehicle.State.Speed = 0; continue; }
            }
            VehiclePhysics.Step(vehicle.State, input, vehicle.MaxSpeed, delta, terrainHeight, seaLevel, worldLimit);
        }
    }

    public IReadOnlyList<VehicleSnapshot> Snapshot() => _byId.Values.Select(v => new VehicleSnapshot(v.Id, v.State.X, v.State.Y, v.State.Z, v.State.Yaw, v.State.Pitch, v.State.Speed, v.DriverPeer)).ToList();
}
