using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Vehicles;

namespace LifeVerse.Client.Tests;

/// Vehicles on the client: a view appears when announced, follows snapshots, reports its driver and is found
/// by "nearest" within range; the wheels keep turning without errors. No network involved.
public partial class VehicleSmoke : Node3D
{
    public override async void _Ready()
    {
        try
        {
            var client = new VehicleClient(); AddChild(client);
            client.Spawn(1, "Carro vermelho", "car", new Vector3(10, 0, 10), 0f, this);
            client.Spawn(2, "Carro azul", "car", new Vector3(-30, 0, 0), 1f, this);
            client.Spawn(1, "duplicado", "car", Vector3.Zero, 0f, this);
            Require(client.Count == 2, $"two vehicles expected, got {client.Count}");
            Require(client.Get(1)!.GetNodeOrNull<MeshInstance3D>("Body") is not null && client.Get(1)!.GetChildren().OfType<MeshInstance3D>().Count(m => m.Name.ToString().StartsWith("Wheel")) == 4, "a car needs a body and four wheels");

            Require(client.Nearest(new Vector3(11, 0, 11), VehicleFleet.EnterRange)?.VehicleId == 1, "the red car should be the nearest");
            Require(client.Nearest(new Vector3(0, 0, 0), VehicleFleet.EnterRange) is null, "nothing is within range at the origin");
            Require(client.DrivenBy(0) is null && client.DrivenBy(5) is null, "nobody drives yet");

            var payload = VehicleCodec.Encode(9, [new VehicleSnapshot(1, 20f, 1f, 5f, .8f, .1f, 12f, 5), new VehicleSnapshot(77, 0, 0, 0, 0, 0, 0, 0)]);
            Require(VehicleCodec.TryDecode(payload, out _, out var decoded), "snapshot must decode");
            client.Apply(decoded);
            Require(client.DrivenBy(5)?.VehicleId == 1, "peer 5 should be driving the red car");
            await Frames(90);
            var car = client.Get(1)!;
            Require(car.GlobalPosition.DistanceTo(new Vector3(20, 1, 5)) < .5f, $"the car should have followed the snapshot: {car.GlobalPosition}");
            Require(Mathf.Abs(Mathf.AngleDifference(car.Rotation.Y, .8f)) < .05f, $"the car should face the snapshot heading: {car.Rotation.Y}");
            Require(car.SeatTransform.Origin.DistanceTo(car.GlobalPosition) is > .2f and < 1.5f, "the seat should be close to the car centre");

            client.Clear();
            Require(client.Count == 0 && client.Get(1) is null, "clear should remove every vehicle");
            GD.Print("event=vehicle_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=vehicle_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
