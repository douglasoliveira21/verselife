using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

public partial class DiscoveryPanel : Node3D
{
    private PanelContainer _panel = null!; private Tree _islands = null!; private RichTextLabel _mini = null!; private HttpRequest _http = null!; private Label _status = null!;
    public override void _Ready() { _http = new HttpRequest(); AddChild(_http); _http.RequestCompleted += OnResponse; Build(); }
    public void Toggle() { _panel.Visible = !_panel.Visible; if (_panel.Visible) LoadIslands(); }
    private void Build() { _panel = new PanelContainer { Visible = false }; _panel.SetAnchorsPreset(Control.LayoutPreset.CenterRight); _panel.OffsetLeft = -480; _panel.OffsetTop = -270; _panel.OffsetRight = -18; _panel.OffsetBottom = 270; var box = new VBoxContainer(); _panel.AddChild(box); box.AddChild(new Label { Text = "MAPA GLOBAL · ILHAS INDEPENDENTES" }); _islands = new Tree { Columns = 3, HideRoot = true, CustomMinimumSize = new Vector2(430, 250) }; box.AddChild(_islands); var actions = new HBoxContainer(); var refresh = new Button { Text = "Atualizar" }; var favorite = new Button { Text = "Favoritar" }; actions.AddChild(refresh); actions.AddChild(favorite); box.AddChild(actions); _mini = new RichTextLabel { CustomMinimumSize = new Vector2(430, 80), Text = "Minimapa: aguardando posição…" }; box.AddChild(_mini); _status = new Label { Text = "Pronto" }; box.AddChild(_status); AddChild(_panel); refresh.Pressed += LoadIslands; favorite.Pressed += Favorite; }
    private void LoadIslands() { Request("/api/v1/discovery/islands?page=1&pageSize=50&sort=popular", Godot.HttpClient.Method.Get, null); }
    private void Favorite() { var item = _islands.GetSelected(); if (item is null) return; var id = item.GetMetadata(0).AsString(); Request($"/api/v1/discovery/islands/{id}/favorite", Godot.HttpClient.Method.Post, ""); }
    private void Request(string path, Godot.HttpClient.Method method, string? body) { var session = GetNode<SessionManager>("/root/SessionManager"); if (!session.IsAuthenticated) return; _http.Request(GetNode<AppManager>("/root/AppManager").ApiBaseUrl + path, [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"], method, body ?? string.Empty); }
    private void OnResponse(long result, long code, string[] headers, byte[] body) { if (code is < 200 or >= 300) { _status.Text = $"Mapa HTTP {code}"; return; } try { using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(body)); if (doc.RootElement.TryGetProperty("items", out var items)) { _islands.Clear(); foreach (var entry in items.EnumerateArray()) { var row = _islands.CreateItem(); row.SetText(0, entry.GetProperty("name").GetString() ?? "Ilha"); row.SetText(1, entry.GetProperty("category").GetString() ?? ""); row.SetText(2, entry.GetProperty("runtimeState").GetString() ?? ""); row.SetMetadata(0, entry.GetProperty("id").GetString() ?? string.Empty); } _status.Text = $"{items.GetArrayLength()} ilhas"; } else _status.Text = "Operação concluída."; } catch { _status.Text = "Resposta inválida."; } }
    public void UpdateMinimap(Vector3 position, float yaw, int poiCount = 0) { if (_mini is not null) _mini.Text = $"Minimapa · X {position.X:0.0} Y {position.Z:0.0} · direção {yaw:0}° · POIs {poiCount}"; }
}
