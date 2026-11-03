using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Tests;

/// Needs a running stack (API + island server) and is therefore not part of godot-smoke.sh. Environment:
/// LIFEVERSE_API_URL, LIFEVERSE_E2E_TOKEN (access token), LIFEVERSE_E2E_ISLAND (island id). It joins the island through the
/// real entry flow, gets into the first vehicle, drives forward, steps out and checks the island server agreed at every step.
public partial class VehicleE2E : Node3D
{
    public override async void _Ready()
    {
        try
        {
            var session = GetNode<SessionManager>("/root/SessionManager");
            var network = GetNode<NetworkManager>("/root/NetworkManager");
            session.Set(OS.GetEnvironment("LIFEVERSE_E2E_TOKEN"), "", "e2e");
            var state = "";
            network.ConnectionStateChanged += (s, detail) => { state = s; GD.Print($"state={s} {detail}"); };
            network.VehicleMessage += text => GD.Print($"vehicle_message={text}");
            var error = network.JoinIsland(Guid.Parse(OS.GetEnvironment("LIFEVERSE_E2E_ISLAND")));
            Require(error == Error.Ok, $"join failed to start: {error}");
            await Until(() => state == "online", 90, "never came online");
            await Until(() => network.Vehicles.Count > 0, 30, "no vehicle was announced");
            var car = network.Vehicles.Get(1) ?? throw new InvalidOperationException("vehicle 1 missing");
            var start = car.TargetPosition;
            GD.Print($"vehicle at {start}");

            network.EnterVehicle(car.VehicleId);
            await Until(() => network.Vehicles.DrivenBy(network.OwnPeerId) is not null, 10, "the server never made us the driver");
            GD.Print("driving");

            for (var i = 0; i < 180; i++) { network.SendVehicleInput(1f, 0f, false); await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
            await Until(() => car.TargetPosition.DistanceTo(start) > 3f, 10, $"the car did not move: {car.TargetPosition}");
            Require(car.Speed > 1f, $"the car should be moving, speed {car.Speed}");
            GD.Print($"car moved to {car.TargetPosition} speed {car.Speed}");

            network.ExitVehicle();
            await Until(() => network.Vehicles.DrivenBy(network.OwnPeerId) is null, 10, "the seat was never freed");

            // Stepping out leaves the player beside the car, so getting back in works (the seat really was freed) and a second exit does too.
            network.EnterVehicle(car.VehicleId);
            await Until(() => network.Vehicles.DrivenBy(network.OwnPeerId) is not null, 10, "could not get back in after stepping out");
            network.ExitVehicle();
            await Until(() => network.Vehicles.DrivenBy(network.OwnPeerId) is null, 10, "the second exit never happened");
            GD.Print("event=vehicle_e2e result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=vehicle_e2e result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Until(Func<bool> condition, int seconds, string failure)
    {
        var deadline = Time.GetTicksMsec() + (ulong)seconds * 1000UL;
        while (!condition()) { if (Time.GetTicksMsec() > deadline) throw new InvalidOperationException(failure); await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
