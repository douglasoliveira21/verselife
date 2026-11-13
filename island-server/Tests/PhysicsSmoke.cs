using System.Text.Json;
using Godot;
using LifeVerse.IslandServer.Scripts;
using LifeVerse.Scripting;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.IslandServer.Tests;

/// <summary>The simulation of physical objects: a ball falls onto the terrain, bounces and comes to rest; a box stops on a fixed wall; poses are announced; people can kick a ball.</summary>
public partial class PhysicsSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var sent = new List<string>();
            var physics = new ServerPhysics { Name = "ServerPhysics" }; AddChild(physics); physics.Initialize(sent.Add); var contacts = new List<(Guid Self, Guid? Other, bool Entered, float Speed)>(); physics.Contacted = (self, other, entered, speed) => contacts.Add((self, other, entered, speed));
            var terrain = new TerrainMap(); // flat ground
            physics.SetTerrain(terrain); await Frames(2);
            Require(physics.HasGround, "the terrain becomes the ground");
            var ground = terrain.SampleHeight(0, 0);

            var ball = Guid.NewGuid(); var crate = Guid.NewGuid(); var wall = Guid.NewGuid(); var ghost = Guid.NewGuid();
            PhysicsBodyDefinition Def(Guid id, string type, Vec3 p, Vec3 s, string physics, bool blocks = true) => new(id, type, p, new Vec3(0, 0, 0), s, physics, blocks);
            physics.Sync([
                Def(ball, "sphere", new Vec3(0, ground + 6, 0), new Vec3(1, 1, 1), "{\"collision\":true,\"shape\":\"box\",\"mode\":\"physical\",\"mass\":1,\"friction\":0.6,\"bounce\":0.5}"),
                Def(crate, "cube", new Vec3(10, ground + 3, 0), new Vec3(1, 1, 1), "{\"collision\":true,\"shape\":\"box\",\"mode\":\"physical\",\"mass\":5}"),
                Def(wall, "cube", new Vec3(12, ground + 1, 0), new Vec3(1, 2, 6), "{}"),
                Def(ghost, "cube", new Vec3(-10, ground + 3, 0), new Vec3(1, 1, 1), "{\"collision\":false,\"mode\":\"physical\"}", false)
            ]);
            Require(physics.BodyCount == 2 && physics.FixedCount == 1, $"two bodies and one fixed object (got {physics.BodyCount}/{physics.FixedCount}); a ghost is no body");
            var start = physics.BodyOf(ball)!.GlobalPosition.Y;
            await Seconds(.8);
            var falling = physics.BodyOf(ball)!.GlobalPosition.Y;
            Require(falling < start - 1, $"the ball falls (from {start} to {falling})");
            await Seconds(9);
            var rest = physics.BodyOf(ball)!;
            Require(MathF.Abs(rest.GlobalPosition.Y - (ground + .5f)) < .15f, $"the ball rests on the ground with its radius above it (y={rest.GlobalPosition.Y}, ground={ground})");
            Require(contacts.Any(c => c.Self == ball && c.Other is null && c.Entered && c.Speed > .3f), $"the ball falling onto the ground is reported as a hit on the ground (contacts: {contacts.Count})");
            Require(sent.Count > 0 && sent.Any(m => m.Contains("\"operation\":\"physics\"") && m.Contains(ball.ToString())), "poses are announced to the players");
            Require(sent.Last().Contains("\"asleep\":true"), "the last announcement says the body went to sleep");

            sent.Clear(); physics.Push(new Vector3(rest.GlobalPosition.X - .6f, ground, rest.GlobalPosition.Z), new Vector3(4, 0, 0));
            await Seconds(1.2);
            Require(physics.BodyOf(ball)!.GlobalPosition.X > .5f, $"a walker kicks the ball ahead (x={physics.BodyOf(ball)!.GlobalPosition.X})");
            Require(sent.Count > 0, "the kick is announced");

            var crateBody = physics.BodyOf(crate)!;
            crateBody.LinearVelocity = new Vector3(8, 0, 0); crateBody.Sleeping = false;
            await Seconds(2.5);
            Require(crateBody.GlobalPosition.X < 12f - .5f - .45f + .2f, $"a fixed wall stops the box (x={crateBody.GlobalPosition.X})");

            physics.Sync([Def(ball, "sphere", new Vec3(-20, ground + 2, 5), new Vec3(1, 1, 1), "{\"collision\":true,\"shape\":\"box\",\"mode\":\"physical\",\"mass\":1,\"friction\":0.6,\"bounce\":0.5}")]);
            Require(physics.BodyCount == 1 && MathF.Abs(physics.BodyOf(ball)!.GlobalPosition.X + 20) < .05f, "moving the object in the builder puts the body there");
            GD.Print("event=physics_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=physics_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private async Task Seconds(double seconds) { await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
