using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.Client.Tests;

/// <summary>
/// Windowed run: for each graphics quality, measures the frame rate for a few seconds and saves a screenshot (set LIFEVERSE_CAPTURE_PATH).
/// Run: Godot --path client --resolution 1920x1080 res://Tests/GraphicsBenchmark.tscn
/// </summary>
public partial class GraphicsBenchmark : Node
{
    public override async void _Ready()
    {
        try
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); Engine.MaxFps = 0;
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            var terrain = world.GetNode<IslandTerrain>("IslandTerrain");
            await Frames(30);
            // some hills so there is something to shade
            for (var i = 0; i < 10; i++) { var x = -30f + (i % 5) * 16f; var z = -26f + (i / 5) * 18f; terrain.ApplyEdit(TerrainTool.Raise, x, z, 8, .5f, 0, 0); terrain.ApplyEdit(TerrainTool.Paint, x, z, 9, 1f, i % 6, 0); }
            if (world.FindChild("Entrar_em_uma_ilha", true, false) is Control join) join.Visible = false;
            var env = world.GetNode<IslandEnvironmentController>("EnvironmentController"); env.Hour = 16.5f;
            var path = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH");
            foreach (var level in new[] { GraphicsQuality.Level.Low, GraphicsQuality.Level.Medium, GraphicsQuality.Level.High, GraphicsQuality.Level.Ultra })
            {
                GraphicsQuality.Choose(level); await Frames(40);
                var start = Time.GetTicksMsec(); var frames = 0; var worst = 0.0; var last = Time.GetTicksUsec();
                while (Time.GetTicksMsec() - start < 3000) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); frames++; var now = Time.GetTicksUsec(); worst = Math.Max(worst, (now - last) / 1000.0); last = now; }
                var fps = frames / ((Time.GetTicksMsec() - start) / 1000.0);
                GD.Print($"event=graphics_benchmark quality={level} fps={fps:0} worst_frame_ms={worst:0.0} size={GetViewport().GetVisibleRect().Size}");
                if (!string.IsNullOrWhiteSpace(path)) GetViewport().GetTexture().GetImage().SavePng($"{path}-{level}.png");
            }
            GraphicsQuality.Chosen = GraphicsQuality.Level.Auto;
            GD.Print("event=graphics_benchmark result=done");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=graphics_benchmark result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
