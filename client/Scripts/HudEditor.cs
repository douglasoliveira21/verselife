using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

public partial class HudEditor : Node3D
{
    private PanelContainer _panel = null!; private VBoxContainer _canvas = null!; private LineEdit _name = null!; private Label _status = null!; private HttpRequest _http = null!;
    public override void _Ready() { _http = new HttpRequest(); AddChild(_http); _http.RequestCompleted += OnResponse; BuildUi(); }
    public void Toggle() => _panel.Visible = !_panel.Visible;
    private void BuildUi() { _panel = new PanelContainer { Name = "HudEditor", Visible = false }; _panel.SetAnchorsPreset(Control.LayoutPreset.CenterRight); _panel.OffsetLeft = -460; _panel.OffsetTop = -300; _panel.OffsetRight = -18; _panel.OffsetBottom = 300; var box = new VBoxContainer(); _panel.AddChild(box); box.AddChild(new Label { Text = "HUD EDITOR · preview" }); _name = new LineEdit { Text = "Minha HUD" }; box.AddChild(_name); _canvas = new VBoxContainer { CustomMinimumSize = new Vector2(400, 300) }; _canvas.AddThemeConstantOverride("separation", 8); box.AddChild(_canvas); var buttons = new HBoxContainer(); foreach (var type in new[] { "Texto", "Botão", "Imagem", "Barra", "Lista", "Entrada" }) { var button = new Button { Text = "+" + type }; button.Pressed += () => AddComponent(type); buttons.AddChild(button); } box.AddChild(buttons); var save = new Button { Text = "Salvar HUD" }; save.Pressed += Save; box.AddChild(save); _status = new Label { Text = "Preview local" }; box.AddChild(_status); AddChild(_panel); AddComponent("Texto"); }
    private void AddComponent(string type) { Control control = type switch { "Botão" => new Button { Text = "Botão" }, "Barra" => new ProgressBar { Value = 65 }, "Entrada" => new LineEdit { PlaceholderText = "Entrada" }, "Lista" => new ItemList { CustomMinimumSize = new Vector2(200, 60) }, "Imagem" => new ColorRect { Color = new Color("#4e78a8"), CustomMinimumSize = new Vector2(120, 50) }, _ => new Label { Text = "Texto LifeVerse" } }; _canvas.AddChild(control); }
    private void Save() { var components = _canvas.GetChildren().Select(x => new { type = x.GetType().Name, visible = x is CanvasItem item && item.Visible }).ToArray(); var body = JsonSerializer.Serialize(new { name = _name.Text, layoutJson = JsonSerializer.Serialize(new { anchors = "top_left", layer = 0, components }), scriptId = (Guid?)null }); var session = GetNode<SessionManager>("/root/SessionManager"); _http.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/huds", [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"], Godot.HttpClient.Method.Post, body); }
    private void OnResponse(long result, long responseCode, string[] headers, byte[] body) => _status.Text = responseCode is >= 200 and < 300 ? "HUD salva no inventário lógico." : $"HUD rejeitada ({responseCode}).";
}
