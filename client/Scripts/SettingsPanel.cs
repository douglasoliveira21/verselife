using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

public partial class SettingsPanel : Node3D
{
    private PanelContainer _panel = null!; private SpinBox _width = null!; private SpinBox _height = null!; private OptionButton _fps = null!; private CheckButton _fullscreen = null!; private CheckButton _vsync = null!; private HSlider _scale = null!; private Label _status = null!;
    public override void _Ready() { Build(); }
    public void Toggle() { _panel.Visible = !_panel.Visible; }
    private void Build() { _panel = new PanelContainer { Visible = false }; _panel.SetAnchorsPreset(Control.LayoutPreset.Center); _panel.OffsetLeft = -220; _panel.OffsetTop = -220; _panel.OffsetRight = 220; _panel.OffsetBottom = 220; var box = new VBoxContainer(); _panel.AddChild(box); box.AddChild(new Label { Text = "CONFIGURAÇÕES" }); _width = new SpinBox { MinValue = 640, MaxValue = 7680, Value = DisplayServer.WindowGetSize().X }; box.AddChild(Labeled("Largura", _width)); _height = new SpinBox { MinValue = 360, MaxValue = 4320, Value = DisplayServer.WindowGetSize().Y }; box.AddChild(Labeled("Altura", _height)); _fps = new OptionButton(); foreach (var fps in new[] { 30, 60, 120, 144, 240 }) _fps.AddItem(fps.ToString()); _fps.Select(2); box.AddChild(Labeled("Limite FPS", _fps)); _fullscreen = new CheckButton { Text = "Tela cheia" }; _vsync = new CheckButton { Text = "V-Sync", ButtonPressed = true }; box.AddChild(_fullscreen); box.AddChild(_vsync); _scale = new HSlider { MinValue = .75, MaxValue = 2, Step = .05, Value = 1 }; box.AddChild(Labeled("Escala da interface", _scale)); var save = new Button { Text = "Salvar" }; box.AddChild(save); _status = new Label(); box.AddChild(_status); AddChild(_panel); save.Pressed += Save; }
    private static Control Labeled(string text, Control control) { var box = new HBoxContainer(); box.AddChild(new Label { Text = text, CustomMinimumSize = new Vector2(140, 0) }); box.AddChild(control); return box; }
    private void Save() { var settings = GetNode<ClientSettings>("/root/ClientSettings"); settings.Apply((int)_width.Value, (int)_height.Value, _fullscreen.ButtonPressed, _vsync.ButtonPressed, int.Parse(_fps.GetItemText(_fps.Selected)), .003f, (float)_scale.Value, 1f); _status.Text = "Configurações salvas."; }
}
