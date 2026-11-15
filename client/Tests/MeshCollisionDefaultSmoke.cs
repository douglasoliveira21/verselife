using Godot;
using LifeVerse.Client.Build;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Building;
using LifeVerse.Shared.Networking;

namespace LifeVerse.Client.Tests;

/// <summary>An imported model (a house) must stay exact: editing its physics must not turn it into a solid box, and the server must not treat it as a wall.</summary>
public partial class MeshCollisionDefaultSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            var house = new BuildObjectState { PrimitiveType = "mesh", PhysicsJson = "{}" };
            Require(BuildPhysicsManager.SettingsOf(house).CollisionShape == "triangle", "a mesh with no shape is read as exact (triangle)");
            Require(PhysicsRules.Write(house.PhysicsJson, BuildPhysicsManager.SettingsOf(house) with { Temporary = true }).Contains("\"triangle\""), "editing its physics keeps it exact");
            Require(BuildPhysicsManager.SettingsOf(new BuildObjectState { PrimitiveType = "mesh", PhysicsJson = "{\"shape\":\"box\"}" }).CollisionShape == "box", "a shape the person chose is respected");
            Require(BuildPhysicsManager.SettingsOf(new BuildObjectState { PrimitiveType = "cube", PhysicsJson = "{}" }).CollisionShape == "box", "a cube is still a box");
            Require(!ObstacleField.Blocks("{\"shape\":\"triangle\"}", "mesh") && !ObstacleField.Blocks("{}", "mesh") && ObstacleField.Blocks("{\"shape\":\"box\"}", "mesh"), "the server only walls a mesh the person made a box or hull");
            GD.Print("event=mesh_collision_default_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=mesh_collision_default_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
