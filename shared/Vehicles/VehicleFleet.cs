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
    public FleetVehicle? Add(Guid key, string name, string type, float x, float z, float yaw, float maxSpeed, Func<float, float, float>? terrainHeight = null)
    {
        if (_byId.Count >= MaxVehicles || _byKey.ContainsKey(key)) return null;
        var vehicle = new FleetVehicle { Id = _nextId++, Key = key, Name = name, Type = type, MaxSpeed = Math.Clamp(float.IsFinite(maxSpeed) ? maxSpeed : 20f, 3f, 60f) };
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

    public void Tick(float delta, Func<float, float, float>? terrainHeight, float seaLevel, float worldLimit)
    {
        foreach (var vehicle in _byId.Values)
        {
            // An empty car brakes to a stop; a driven one follows its latest input.
            if (vehicle.DriverPeer == 0 && MathF.Abs(vehicle.State.Speed) < .01f && MathF.Abs(vehicle.State.Steer) < .001f) { vehicle.State.Speed = 0; continue; }
            var input = vehicle.DriverPeer == 0 ? new VehicleInput(0, 0, true) : vehicle.Input;
            VehiclePhysics.Step(vehicle.State, input, vehicle.MaxSpeed, delta, terrainHeight, seaLevel, worldLimit);
        }
    }

    public IReadOnlyList<VehicleSnapshot> Snapshot() => _byId.Values.Select(v => new VehicleSnapshot(v.Id, v.State.X, v.State.Y, v.State.Z, v.State.Yaw, v.State.Pitch, v.State.Speed, v.DriverPeer)).ToList();
}
