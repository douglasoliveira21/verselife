using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

public partial class LuaScriptEditor : Node3D
{
    private PanelContainer _panel = null!; private TextEdit _editor = null!; private OptionButton _event = null!; private LineEdit _objectId = null!; private Label _console = null!; private HttpRequest _http = null!;
    public override void _Ready() { _http = new HttpRequest(); AddChild(_http); _http.RequestCompleted += OnResponse; BuildUi(); }
    public void Toggle() { _panel.Visible = !_panel.Visible; }
    private void BuildUi()
    {
        _panel = new PanelContainer { Name = "LuaEditor", Visible = false }; _panel.SetAnchorsPreset(Control.LayoutPreset.Center); _panel.OffsetLeft = -360; _panel.OffsetTop = -300; _panel.OffsetRight = 360; _panel.OffsetBottom = 300; var box = new VBoxContainer(); _panel.AddChild(box); box.AddChild(new Label { Text = "LUA SCRIPT · sandbox isolado · linhas visíveis no editor" }); _objectId = new LineEdit { PlaceholderText = "UUID do objeto" }; box.AddChild(_objectId); _event = new OptionButton(); foreach (var value in new[] { "on_start", "on_touch", "on_collision", "on_enter", "on_leave", "on_timer", "on_chat", "on_message", "on_owner_changed", "on_object_rezzed", "on_object_removed" }) _event.AddItem(value); box.AddChild(_event); _editor = new TextEdit { CustomMinimumSize = new Vector2(680, 360), Text = "function on_start(event)\n  object.send_message('Olá LifeVerse')\nend", WrapMode = TextEdit.LineWrappingMode.None }; box.AddChild(_editor); var buttons = new HBoxContainer(); var save = new Button { Text = "Salvar" }; var run = new Button { Text = "Executar" }; var stop = new Button { Text = "Parar" }; buttons.AddChild(save); buttons.AddChild(run); buttons.AddChild(stop); box.AddChild(buttons); _console = new Label { Text = "Console pronto" }; box.AddChild(_console); AddChild(_panel); save.Pressed += Save; run.Pressed += Run; stop.Pressed += Stop;
    }
    private string BaseUrl => GetNode<AppManager>("/root/AppManager").ApiBaseUrl;
    private string Token => GetNode<SessionManager>("/root/SessionManager").AccessToken;
    private bool TryContext(out Guid island, out Guid obj) { island = GetNode<NetworkManager>("/root/NetworkManager").CurrentIslandId; obj = Guid.Empty; return island != Guid.Empty && Guid.TryParse(_objectId.Text.Trim(), out obj); }
    private void Save() { if (!TryContext(out var island, out var obj)) { _console.Text = "Informe um UUID de objeto e entre numa ilha."; return; } var body = JsonSerializer.Serialize(new { name = "script.lua", source = _editor.Text, eventName = _event.GetItemText(_event.Selected) }); _http.Request($"{BaseUrl}/api/v1/worlds/{island}/objects/{obj}/scripts", [$"Authorization: Bearer {Token}", "Content-Type: application/json"], Godot.HttpClient.Method.Post, body); }
    private void Run() { if (!TryContext(out var island, out var obj)) return; _console.Text = "Executando no worker sandbox…"; var body = JsonSerializer.Serialize(new { eventName = _event.GetItemText(_event.Selected), payloadJson = "{}" }); _http.Request($"{BaseUrl}/api/v1/worlds/{island}/objects/{obj}/scripts/run", [$"Authorization: Bearer {Token}", "Content-Type: application/json"], Godot.HttpClient.Method.Post, body); }
    private void Stop() => _console.Text = "Parar exige o UUID do script na API; execução atual será encerrada pelo timeout do worker.";
    private void OnResponse(long result, long responseCode, string[] headers, byte[] body) { var text = Encoding.UTF8.GetString(body); _console.Text = responseCode is >= 200 and < 300 ? $"OK: {text[..Math.Min(text.Length, 180)]}" : $"Erro {responseCode}: {text[..Math.Min(text.Length, 180)]}"; }
}
