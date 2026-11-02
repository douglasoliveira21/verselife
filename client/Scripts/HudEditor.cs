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
    private void BuildUi() { _panel = new PanelContainer { Name = "HudEditor", Visible = false }; _panel.SetAnchorsPreset(Control.LayoutPreset.CenterRight); _panel.OffsetLeft = -460; _panel.OffsetTop = -300; _panel.OffsetRight = -18; _panel.OffsetBottom = 300; var box = new VBoxContainer(); _panel.AddChild(box); box.AddChild(new Label { Text = "HUD EDITOR · preview visual · camadas" }); _name = new LineEdit { Text = "Minha HUD" }; box.AddChild(_name); _canvas = new VBoxContainer { CustomMinimumSize = new Vector2(400, 300) }; _canvas.AddThemeConstantOverride("separation", 8); box.AddChild(_canvas); var buttons = new HBoxContainer(); foreach (var type in new[] { "Painel", "Texto", "Botao", "Imagem", "Barra", "Lista", "Entrada", "Icone", "Container" }) { var button = new Button { Text = "+" + type }; button.Pressed += () => AddComponent(type); buttons.AddChild(button); } box.AddChild(buttons); var actions = new HBoxContainer(); var save = new Button { Text = "Salvar HUD" }; var equip = new Button { Text = "Equipar" }; save.Pressed += Save; equip.Pressed += Equip; actions.AddChild(save); actions.AddChild(equip); box.AddChild(actions); _status = new Label { Text = "Preview local" }; box.AddChild(_status); AddChild(_panel); AddComponent("Texto"); }
    private void AddComponent(string type) { Control control = type switch { "Botao" => new Button { Text = "Botao" }, "Barra" => new ProgressBar { Value = 65 }, "Entrada" => new LineEdit { PlaceholderText = "Entrada" }, "Lista" => new ItemList { CustomMinimumSize = new Vector2(200, 60) }, "Imagem" or "Icone" => new ColorRect { Color = new Color("#4e78a8"), CustomMinimumSize = new Vector2(120, 50) }, "Painel" or "Container" => new PanelContainer { CustomMinimumSize = new Vector2(220, 60) }, _ => new Label { Text = "Texto LifeVerse" } }; control.SetMeta("hud_type", type); _canvas.AddChild(control); }
    private void Save() { var components = _canvas.GetChildren().Select(x => new { type = x.GetMeta("hud_type", x.GetType().Name).AsString(), visible = x is CanvasItem item && item.Visible, layer = x.GetIndex(), editable = true }).ToArray(); var body = JsonSerializer.Serialize(new { name = _name.Text, layoutJson = JsonSerializer.Serialize(new { version = 1, anchors = "top_left", margins = new { left = 0, top = 0 }, components }), scriptId = (Guid?)null }); var session = GetNode<SessionManager>("/root/SessionManager"); _http.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/huds", [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"], Godot.HttpClient.Method.Post, body); }
    private void Equip() { _status.Text = "Salve a HUD primeiro para equipá-la."; }
    private void OnResponse(long result, long responseCode, string[] headers, byte[] body) => _status.Text = responseCode is >= 200 and < 300 ? "HUD salva no inventário lógico." : $"HUD rejeitada ({responseCode}).";
}
