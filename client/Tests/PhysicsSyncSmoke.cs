using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>Poses from the island's simulation move physical objects smoothly, leave edited ones alone and survive an update of the stored object.</summary>
public partial class PhysicsSyncSmoke : Node3D
{
    public override async void _Ready()
    {
        try
        {
            var sync = new PhysicsSync { Name = "PhysicsSync" }; AddChild(sync);
            var id = Guid.NewGuid(); var other = Guid.NewGuid();
            var view = new BuildObjectView(); AddChild(view);
            view.Apply(new BuildObjectState { Id = id, PrimitiveType = "sphere", PositionX = 1, PositionY = 5, PositionZ = 1, ScaleX = 1, ScaleY = 1, ScaleZ = 1, MaterialJson = "{}", GeometryJson = "{}", PhysicsJson = "{\"mode\":\"physical\"}" });
            await Frames(2);
            sync.OnOperation("{\"operation\":\"sound\",\"objectId\":\"" + id + "\"}");
            sync.OnOperation("not json"); sync.OnOperation("{\"operation\":\"physics\",\"bodies\":[{\"id\":\"zzz\"},{\"id\":\"" + other + "\",\"p\":[1,2],\"q\":[0,0,0,1]}]}");
            Require(sync.TrackedForTesting == 0, "operations that are not poses, broken ones and unknown ids are ignored");
            sync.OnOperation("{\"operation\":\"physics\",\"bodies\":[{\"id\":\"" + id + "\",\"p\":[1,1,1],\"q\":[0,0,0,1],\"asleep\":false}]}");
            Require(sync.TrackedForTesting == 1, "a pose is kept");
            sync.StepForTesting(.016); sync.StepForTesting(.016);
            Require(view.Position.Y < 5f && view.Position.Y > 1f, $"the object moves toward the pose without jumping (y={view.Position.Y})");
            for (var i = 0; i < 60; i++) sync.StepForTesting(.05);
            Require(view.Position.DistanceTo(new Vector3(1, 1, 1)) < .05f && view.IsDriven, "it arrives and is driven by the simulation");

            view.Apply(new BuildObjectState { Id = id, PrimitiveType = "sphere", Name = "Bola", PositionX = 1, PositionY = 5, PositionZ = 1, ScaleX = 1, ScaleY = 1, ScaleZ = 1, MaterialJson = "{}", GeometryJson = "{}", PhysicsJson = "{\"mode\":\"physical\"}" });
            Require(view.Position.DistanceTo(new Vector3(1, 1, 1)) < .05f, "an update of the stored object does not put it back where it was first placed");
            view.Apply(new BuildObjectState { Id = id, PrimitiveType = "sphere", PositionX = 9, PositionY = 5, PositionZ = 9, ScaleX = 1, ScaleY = 1, ScaleZ = 1, MaterialJson = "{}", GeometryJson = "{}", PhysicsJson = "{\"mode\":\"physical\"}" });
            Require(view.Position.DistanceTo(new Vector3(9, 5, 9)) < .01f && !view.IsDriven, "a builder moving the object takes it over again");

            view.SetSelected(true);
            sync.OnOperation("{\"operation\":\"physics\",\"bodies\":[{\"id\":\"" + id + "\",\"p\":[0,0,0],\"q\":[0,0,0,1]}]}");
            for (var i = 0; i < 20; i++) sync.StepForTesting(.05);
            Require(view.Position.DistanceTo(new Vector3(9, 5, 9)) < .01f, "an object being edited is left alone");
            GD.Print("event=physics_sync_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=physics_sync_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
