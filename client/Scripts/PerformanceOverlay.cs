using Godot;

namespace LifeVerse.Client.Scripts;

public partial class PerformanceOverlay : CanvasLayer
{
    private Label _label = null!;
    public override void _Ready() { _label = new Label { Position = new Vector2(16, 64), Visible = OS.GetEnvironment("LIFEVERSE_SHOW_METRICS") == "1" }; AddChild(_label); }
    public override void _Process(double delta) { if (!_label.Visible) return; var fps = Engine.GetFramesPerSecond(); var memory = OS.GetStaticMemoryUsage() / 1024d / 1024d; _label.Text = $"FPS {fps:0} · frame {delta * 1000:0.0} ms · RAM {memory:0.0} MB · objects {GetTree().GetNodeCount()}"; }
}
