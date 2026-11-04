using Godot;

namespace LifeVerse.Client.Scripts;

public partial class PerformanceOverlay : CanvasLayer
{
    private Label _label = null!;
    public void Toggle() => _label.Visible = !_label.Visible;
    public override void _Ready() { _label = new Label { Position = new Vector2(16, 40), Visible = OS.GetEnvironment("LIFEVERSE_SHOW_METRICS") == "1" }; AddChild(_label); }
    public override void _Process(double delta) { if (!_label.Visible) return; var fps = Engine.GetFramesPerSecond(); var memory = OS.GetStaticMemoryUsage() / 1024d / 1024d; var counts = GetNodeOrNull<LifeVerse.Client.Autoloads.NetworkManager>("/root/NetworkManager")?.DetailCounts; var crowd = counts is { Count: > 0 } ? $" · avatares completo {counts.GetValueOrDefault(LifeVerse.Shared.Avatar.AvatarDetail.Full)} / reduzido {counts.GetValueOrDefault(LifeVerse.Shared.Avatar.AvatarDetail.Reduced)} / simples {counts.GetValueOrDefault(LifeVerse.Shared.Avatar.AvatarDetail.Basic)} / ocultos {counts.GetValueOrDefault(LifeVerse.Shared.Avatar.AvatarDetail.Hidden)}" : string.Empty; _label.Text = $"FPS {fps:0} · frame {delta * 1000:0.0} ms · RAM {memory:0.0} MB · objects {GetTree().GetNodeCount()}{crowd}"; }
}
