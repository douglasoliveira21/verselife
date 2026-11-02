using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.Client.Tests;

/// <summary>Runs the world with an edited heightmap. Set LIFEVERSE_CAPTURE_PATH (windowed run) to save screenshots.</summary>
public partial class TerrainSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>();
            AddChild(world);
            var terrain = world.GetNode<IslandTerrain>("IslandTerrain");
            var player = world.GetNode<PlayerController>("Player");
            await Frames(30);
            Require(Mathf.Abs(terrain.Map.SampleHeight(0, 0)) < .001f, "new terrain is not flat");
            Require(player.IsOnFloor() && Mathf.Abs(player.GlobalPosition.Y) < .1f, $"player not standing on flat terrain: {player.GlobalPosition}");
            await Capture("flat");

            // A hill and a painted dirt patch, applied the way server-accepted edits are replayed.
            var revision = terrain.Map.Revision;
            for (var i = 0; i < 12; i++) { revision++; terrain.ApplyEdit(TerrainTool.Raise, 14, -6, 9, .4f, 0, revision); }
            for (var i = 0; i < 6; i++) { revision++; terrain.ApplyEdit(TerrainTool.Paint, 14, -6, 12, 1f, 1, revision); }
            for (var i = 0; i < 6; i++) { revision++; terrain.ApplyEdit(TerrainTool.Paint, 22, 4, 6, 1f, 3, revision); }
            for (var i = 0; i < 6; i++) { revision++; terrain.ApplyEdit(TerrainTool.Paint, -14, 10, 7, 1f, 2, revision); }
            await Frames(5);
            var hill = terrain.Map.SampleHeight(14, -6);
            Require(hill > 3f, $"hill too low: {hill}");

            player.GlobalPosition = new Vector3(14, hill + 3f, -6);
            player.Velocity = Vector3.Zero;
            await Frames(90);
            Require(player.IsOnFloor(), "player did not land on the raised terrain");
            Require(Mathf.Abs(player.GlobalPosition.Y - hill) < .35f, $"player not resting on the hill: y={player.GlobalPosition.Y} hill={hill}");
            player.GlobalPosition = new Vector3(0, 1, 6);
            player.Velocity = Vector3.Zero;
            await Frames(30);
            await Capture("edited");

            GD.Print($"event=terrain_smoke result=passed hill={hill:F2}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=terrain_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Capture(string name)
    {
        var path = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;
        for (var i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var error = GetViewport().GetTexture().GetImage().SavePng($"{path}-{name}.png");
        GD.Print($"event=terrain_capture name={name} result={error}");
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
