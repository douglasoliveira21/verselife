using System.Net.Http.Json;
using Godot;
using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Terrain;
using LifeVerse.Shared.Vehicles;

namespace LifeVerse.IslandServer.Scripts;

/// Vehicles on this island: the fleet is simulated here every tick, a driver is pinned to the seat, and clients
/// only see snapshots. Kept in its own file so ServerMain stays about players, terrain and building.
public partial class ServerMain
{
    private readonly VehicleFleet _fleet = new();

    private float WorldLimit => _terrain.HalfExtent + TerrainMap.OceanMargin;

    private sealed record VehicleRow(Guid Id, string Name, string VehicleType, float PositionX, float PositionZ, float RotationY, float MaxSpeed);
    private sealed record VehicleFeed(List<VehicleRow> Vehicles);

    /// Adds vehicles the API knows about that this island has not seen yet and tells everyone connected.
    private async Task RefreshVehiclesAsync(CancellationToken ct)
    {
        using var response = await _api.GetAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/vehicles", ct);
        if (!response.IsSuccessStatusCode) return;
        var feed = await response.Content.ReadFromJsonAsync<VehicleFeed>(cancellationToken: ct);
        foreach (var row in feed?.Vehicles ?? [])
        {
            if (_fleet.Add(row.Id, row.Name, row.VehicleType, row.PositionX, row.PositionZ, row.RotationY, row.MaxSpeed, _terrain.SampleHeight) is not { } vehicle) continue;
            foreach (var target in _players.Values.Where(x => x.Authenticated)) SendVehicle(target.PeerId, vehicle);
            Log("vehicle_added", new { vehicle.Id, vehicle.Name });
        }
    }

    /// Checks the API for new vehicles right away and then every 15 seconds. A failure never stops the island.
    private async Task VehicleSyncLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            do
            {
                try { await RefreshVehiclesAsync(ct); }
                catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException) { Log("vehicles_sync_failed", new { error = exception.Message }); }
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private void SendVehicle(int peerId, FleetVehicle vehicle) => _bridge.RpcId(peerId, NetworkBridge.MethodName.VehicleSpawned,
        vehicle.Id, vehicle.Name, vehicle.Type, vehicle.State.X, vehicle.State.Y, vehicle.State.Z, vehicle.State.Yaw);

    private void SendAllVehicles(int peerId) { foreach (var vehicle in _fleet.Vehicles) SendVehicle(peerId, vehicle); }

    private void TickVehicles(float dt) => _fleet.Tick(dt, _terrain.SampleHeight, _terrain.SeaLevel, WorldLimit);

    /// A driver does not walk: the avatar rides at the seat with the car's heading and velocity.
    private bool PinDriver(ConnectedPlayer player)
    {
        if (_fleet.VehicleOf(player.PeerId) is not { } car) return false;
        var forwardX = -MathF.Sin(car.State.Yaw); var forwardZ = -MathF.Cos(car.State.Yaw);
        player.State.X = car.State.X; player.State.Y = car.State.Y + VehicleFleet.DriverHeight; player.State.Z = car.State.Z;
        player.State.VelocityX = forwardX * car.State.Speed; player.State.VelocityY = 0; player.State.VelocityZ = forwardZ * car.State.Speed;
        player.State.Flying = false; player.State.Seated = true;
        player.Motion = NetworkMotionState.Sit; player.LastProcessedInput = player.LatestInput.Sequence;
        player.LatestInput = new MovementInput(player.LatestInput.Sequence, 0, 0, InputFlags.None);
        return true;
    }

    /// The yaw other players see: the car's heading for a driver, the walking direction for everyone else.
    private float YawOf(ConnectedPlayer player) => _fleet.VehicleOf(player.PeerId) is { } car ? -car.State.Yaw : MathF.Atan2(player.State.VelocityX, -player.State.VelocityZ);

    private void BroadcastVehicles()
    {
        if (_fleet.Vehicles.Count == 0) return;
        var payload = VehicleCodec.Encode(_serverTick, _fleet.Snapshot());
        foreach (var target in _players.Values.Where(x => x.Authenticated)) _bridge.RpcId(target.PeerId, NetworkBridge.MethodName.VehicleSnapshots, payload);
    }

    public void EnterVehicle(int peerId, int vehicleId)
    {
        if (!_players.TryGetValue(peerId, out var player) || !player.Authenticated) return;
        var result = _fleet.Enter(peerId, vehicleId, player.State.X, player.State.Y, player.State.Z);
        if (result == EnterResult.Ok) { Log("vehicle_entered", new { peerId, vehicleId }); return; }
        _bridge.RpcId(peerId, NetworkBridge.MethodName.VehicleRejected, result switch
        {
            EnterResult.TooFar => "too_far", EnterResult.Occupied => "occupied", EnterResult.AlreadyDriving => "already_driving", _ => "no_vehicle"
        });
    }

    public void ExitVehicle(int peerId)
    {
        if (!_players.TryGetValue(peerId, out var player) || !player.Authenticated) return;
        if (!_fleet.Exit(peerId, _terrain.SampleHeight, out var x, out var y, out var z)) return;
        player.State.X = x; player.State.Y = y + AuthoritativeMovement.GroundOffset; player.State.Z = z;
        player.State.VelocityX = player.State.VelocityY = player.State.VelocityZ = 0; player.State.Seated = false;
        Log("vehicle_exited", new { peerId });
    }

    public void SubmitVehicleInput(int peerId, float throttle, float steer, bool brake)
    {
        if (!_players.TryGetValue(peerId, out var player) || !player.Authenticated) return;
        _fleet.SetInput(peerId, throttle, steer, brake);
    }
}
