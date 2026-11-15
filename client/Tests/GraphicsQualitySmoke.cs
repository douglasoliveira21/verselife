using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The graphics quality presets: Automático by graphics card, what each level sets on the viewport, the environment and the sun, and that the world applies a change at once.</summary>
public partial class GraphicsQualitySmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            Require(GraphicsQuality.Resolve(GraphicsQuality.Level.Auto, RenderingDevice.DeviceType.DiscreteGpu) == GraphicsQuality.Level.High, "a discrete card gets Alto");
            Require(GraphicsQuality.Resolve(GraphicsQuality.Level.Auto, RenderingDevice.DeviceType.IntegratedGpu) == GraphicsQuality.Level.Low, "an integrated one gets Baixo");
            Require(GraphicsQuality.Resolve(GraphicsQuality.Level.Auto, RenderingDevice.DeviceType.Cpu) == GraphicsQuality.Level.Medium, "anything else gets Médio");
            Require(GraphicsQuality.Resolve(GraphicsQuality.Level.Ultra, RenderingDevice.DeviceType.IntegratedGpu) == GraphicsQuality.Level.Ultra, "a chosen level is kept");
            Require(GraphicsQuality.Parse("ultra") == GraphicsQuality.Level.Ultra && GraphicsQuality.Parse("???") == GraphicsQuality.Level.Auto && GraphicsQuality.Parse(null) == GraphicsQuality.Level.Auto, "keys parse, unknown means automatic");
            Require(GraphicsQuality.Labels.Length == 5 && GraphicsQuality.Keys.Length == 5 && GraphicsQuality.Labels[0] == "Automático", "five choices");

            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            await Frames(10);
            var env = world.GetNode<WorldEnvironment>("WorldEnvironment").Environment; var sun = world.GetNode<DirectionalLight3D>("Sun"); var viewport = GetViewport();

            GraphicsQuality.Choose(GraphicsQuality.Level.Low); await Frames(2);
            Require(!env.SsaoEnabled && !env.GlowEnabled && viewport.Msaa3D == Viewport.Msaa.Disabled && !viewport.UseTaa && sun.DirectionalShadowMode == DirectionalLight3D.ShadowMode.Parallel2Splits, "Baixo: no effects, 2 shadow splits");
            GraphicsQuality.Choose(GraphicsQuality.Level.Medium); await Frames(2);
            Require(env.SsaoEnabled && env.GlowEnabled && viewport.ScreenSpaceAA == Viewport.ScreenSpaceAAEnum.Fxaa && !env.SsilEnabled, "Médio: ambient occlusion, glow and FXAA");
            GraphicsQuality.Choose(GraphicsQuality.Level.High); await Frames(2);
            Require(viewport.UseTaa && viewport.Msaa3D == Viewport.Msaa.Msaa2X && sun.DirectionalShadowMode == DirectionalLight3D.ShadowMode.Parallel4Splits && sun.ShadowEnabled, "Alto: TAA, MSAA 2x and 4 shadow splits");
            GraphicsQuality.Choose(GraphicsQuality.Level.Ultra); await Frames(2);
            Require(env.SsilEnabled && viewport.Msaa3D == Viewport.Msaa.Msaa4X && sun.DirectionalShadowMaxDistance >= 200f && env.AdjustmentEnabled, "Ultra: indirect light, MSAA 4x, long shadows, color adjustment");
            Require(sun.DirectionalShadowMaxDistance > GraphicsQuality.PresetOf(GraphicsQuality.Level.Low).ShadowDistance, "every step reaches further with shadows");
            GraphicsQuality.Choose(GraphicsQuality.Level.Auto); await Frames(2);
            Require(GraphicsQuality.Current != GraphicsQuality.Level.Auto, "Automático resolves to a level");

            GD.Print("event=graphics_quality_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=graphics_quality_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
