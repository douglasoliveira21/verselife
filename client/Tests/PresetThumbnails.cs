using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.Client.Tests;

/// <summary>
/// Windowed run that saves the pictures shown in Configurações › Gráficos for each quality preset: real captures of the world, without the interface.
/// Run: Godot --path client --resolution 1600x900 res://Tests/PresetThumbnails.tscn (LIFEVERSE_THUMBS_DIR = where to write, default client/Assets/ui/presets).
/// </summary>
public partial class PresetThumbnails : Node
{
    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            var terrain = world.GetNode<IslandTerrain>("IslandTerrain");
            await Frames(30);
            for (var i = 0; i < 10; i++) { var x = -30f + (i % 5) * 16f; var z = -26f + (i / 5) * 18f; terrain.ApplyEdit(TerrainTool.Raise, x, z, 8, .5f, 0, 0); terrain.ApplyEdit(TerrainTool.Paint, x, z, 9, 1f, i % 6, 0); }
            world.GetNode<IslandEnvironmentController>("EnvironmentController").Hour = 16.5f;
            foreach (var layer in world.GetChildren().OfType<CanvasLayer>()) layer.Visible = false;
            if (world.GetNodeOrNull<CanvasLayer>("ViewerUI") is { } ui) ui.Visible = false;
            if (world.GetNodeOrNull<Node>("Player")?.GetNodeOrNull<CanvasLayer>("HUD") is { } hud) hud.Visible = false;
            var dir = OS.GetEnvironment("LIFEVERSE_THUMBS_DIR"); if (string.IsNullOrWhiteSpace(dir)) dir = ProjectSettings.GlobalizePath("res://Assets/ui/presets");
            DirAccess.MakeDirRecursiveAbsolute(dir);
            foreach (var level in new[] { GraphicsQuality.Level.VeryLow, GraphicsQuality.Level.Low, GraphicsQuality.Level.Medium, GraphicsQuality.Level.High, GraphicsQuality.Level.Ultra })
            {
                GraphicsQuality.Choose(level); await Frames(45);
                var image = GetViewport().GetTexture().GetImage(); image.Resize(640, 360, Image.Interpolation.Lanczos);
                var path = $"{dir}/{GraphicsQuality.Keys[(int)level]}.jpg"; var error = image.SaveJpg(path, .9f);
                GD.Print($"event=preset_thumbnail level={level} result={error} path={path}");
            }
            GraphicsQuality.Chosen = GraphicsQuality.Level.Auto;
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=preset_thumbnails result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
