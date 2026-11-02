using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

public partial class SocialPanel : Node3D
{
    private PanelContainer _panel = null!; private RichTextLabel _history = null!; private LineEdit _input = null!; private Label _status = null!; private HttpRequest _http = null!; private OptionButton _channel = null!;
    public override void _Ready() { _http = new HttpRequest(); AddChild(_http); _http.RequestCompleted += OnResponse; Build(); }
    public void Toggle() { _panel.Visible = !_panel.Visible; if (_panel.Visible) LoadMessages(); }
    private void Build() { _panel = new PanelContainer { Visible = false }; _panel.SetAnchorsPreset(Control.LayoutPreset.Center); _panel.OffsetLeft = -260; _panel.OffsetTop = -220; _panel.OffsetRight = 260; _panel.OffsetBottom = 220; var box = new VBoxContainer(); _panel.AddChild(box); box.AddChild(new Label { Text = "SOCIAL" }); _channel = new OptionButton(); foreach (var c in new[] { "global", "private", "group" }) _channel.AddItem(c); box.AddChild(_channel); _history = new RichTextLabel { CustomMinimumSize = new Vector2(480, 280) }; box.AddChild(_history); _input = new LineEdit { PlaceholderText = "Mensagem…", MaxLength = 2000 }; box.AddChild(_input); var send = new Button { Text = "Enviar" }; box.AddChild(send); _status = new Label { Text = "Online" }; box.AddChild(_status); AddChild(_panel); send.Pressed += Send; _input.TextSubmitted += _ => Send(); _channel.ItemSelected += _ => LoadMessages(); }
    private void LoadMessages() { Request("/api/v1/social/messages?channel=" + Uri.EscapeDataString(_channel.GetItemText(_channel.Selected)), Godot.HttpClient.Method.Get, null); }
    private void Send() { var content = _input.Text.Trim(); if (content.Length == 0) return; var body = JsonSerializer.Serialize(new { content, recipientUserId = (Guid?)null, groupId = (Guid?)null, islandId = _channel.GetItemText(_channel.Selected) == "local" ? GetNode<NetworkManager>("/root/NetworkManager").CurrentIslandId : (Guid?)null }); Request("/api/v1/social/messages", Godot.HttpClient.Method.Post, body); _input.Clear(); }
    private void Request(string path, Godot.HttpClient.Method method, string? body) { var session = GetNode<SessionManager>("/root/SessionManager"); if (!session.IsAuthenticated) return; var url = GetNode<AppManager>("/root/AppManager").ApiBaseUrl + path; _http.Request(url, [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"], method, body ?? string.Empty); }
    private void OnResponse(long result, long code, string[] headers, byte[] body) { if (code is < 200 or >= 300) { _status.Text = $"Social HTTP {code}"; return; } try { using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(body)); _history.Text = doc.RootElement.ValueKind == JsonValueKind.Array ? string.Join("\n", doc.RootElement.EnumerateArray().Select(x => $"[{x.GetProperty("createdAt").GetDateTimeOffset():HH:mm}] {x.GetProperty("content").GetString()}")) : "Mensagem enviada."; _status.Text = "Atualizado"; } catch { _status.Text = "Resposta inválida."; } }
}
