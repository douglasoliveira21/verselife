using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.Client.Tests;

/// <summary>
/// Photographs the Godot world from the same nine cameras as the Unity client's world preview (unity/.../World/WorldPreview.cs), on the same sculpted island, at the same hours and weather,
/// so the two can be compared side by side. Windowed run with LIFEVERSE_CAPTURE_PATH set to a path prefix; writes {prefix}-{view}.png. The interface is hidden; nothing is sent anywhere.
/// </summary>
public partial class WorldCompareCapture : Node
{
    /// <summary>The same island as WorldPreview.SampleIsland in Unity: hills, a rocky top, dry grass, a dirt path.</summary>
    public static TerrainMap SampleIsland()
    {
        var map = new TerrainMap();
        for (var i = 0; i < 90; i++) map.Apply(TerrainTool.Raise, 15, 10, TerrainMap.MaxBrushRadius, 1f);
        for (var i = 0; i < 45; i++) map.Apply(TerrainTool.Raise, -45, -30, 20, 1f);
        for (var i = 0; i < 30; i++) map.Apply(TerrainTool.Raise, 60, -50, 18, 1f);
        map.Apply(TerrainTool.Paint, 15, 10, 9, 1f, 3);
        map.Apply(TerrainTool.Paint, -45, -30, 12, 1f, 7);
        for (var x = -40; x <= 15; x += 6) map.Apply(TerrainTool.Paint, x, x * .3f - 10, 3, 1f, 1);
        return map;
    }

    private static readonly (string Name, Vector3 Position, Vector3 Target, float Hour, string Weather)[] Views =
    {
        ("island", new(-95, 26, -150), new(0, 8, 0), 14f, "clear"), ("sunset", new(-95, 14, -150), new(0, 12, 0), 17.95f, "clear"), ("night", new(-95, 18, -150), new(0, 8, 0), 22f, "clear"),
        ("storm", new(-95, 22, -150), new(0, 8, 0), 13f, "storm"), ("under", new(30, -4.5f, -120), new(0, -2f, 0), 14f, "clear"), ("waves", new(-20, .9f, -170), new(10, 0f, -60), 15f, "clear"),
        ("rain", new(-60, 6f, -60), new(10, 8f, 10), 13f, "rain"), ("beach", new(0, 2.5f, -150), new(0, 1f, -90), 15f, "clear"), ("ground", new(-10, 1.7f, -40), new(15, 6f, 10), 11f, "clear"),
    };

    public override async void _Ready()
    {
        var prefix = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH");
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            await Frames(10);
            foreach (var node in world.FindChildren("*", "CanvasLayer", true, false)) if (node is CanvasLayer layer) layer.Visible = false;   // only the world
            if (world.GetNodeOrNull<Node3D>("Player") is { } player) { player.ProcessMode = ProcessModeEnum.Disabled; player.Visible = false; }
            world.GetNode<IslandTerrain>("IslandTerrain").ApplyFull(SampleIsland().Serialize());
            var environment = world.GetNode<IslandEnvironmentController>("EnvironmentController"); environment.DayNightCycle = false;
            var camera = new Camera3D { Fov = 62f * 1.0f, Near = .2f, Far = 6000f }; world.AddChild(camera); camera.MakeCurrent();
            foreach (var view in Views)
            {
                camera.GlobalPosition = view.Position; camera.LookAt(view.Target, Vector3.Up);
                environment.Hour = view.Hour; environment.Weather = view.Weather; camera.MakeCurrent();
                await Frames(60);
                if (!string.IsNullOrWhiteSpace(prefix)) { var error = GetViewport().GetTexture().GetImage().SavePng($"{prefix}-{view.Name}.png"); GD.Print($"event=godot_world_shot view={view.Name} saved={error == Error.Ok}"); }
            }
            GD.Print("event=godot_world_compare result=done"); GetTree().Quit(0);
        }
        catch (System.Exception exception) { GD.PrintErr("event=godot_world_compare result=failed error=" + exception); GetTree().Quit(1); }
    }

    private async System.Threading.Tasks.Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
