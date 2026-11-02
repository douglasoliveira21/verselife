using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

public partial class BuildEditor : Node3D
{
    private NetworkManager _network = null!;
    private HttpRequest _http = null!;
    private PanelContainer _panel = null!;
    private OptionButton _primitive = null!;
    private LineEdit _snap = null!;
    private LineEdit _color = null!;
    private LineEdit _rotation = null!;
    private LineEdit _geometry = null!;
    private LineEdit _material = null!;
    private string _clipboard = string.Empty;
    private Label _status = null!;
    private Node3D? _selected;
    private Guid _islandId;
    private readonly Dictionary<Guid, BuildObjectView> _objects = [];
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private bool _active;

    public override void _Ready()
    {
        _network = GetNode<NetworkManager>("/root/NetworkManager"); _network.BuildOperationReceived += OnOperation; _network.BuildOperationRejected += OnRejected;
        _http = new HttpRequest(); AddChild(_http); _http.RequestCompleted += OnObjectsLoaded;
        BuildPanel();
    }
    public override void _ExitTree() { if (_network is null) return; _network.BuildOperationReceived -= OnOperation; _network.BuildOperationRejected -= OnRejected; }
    public override void _UnhandledInput(InputEvent input) { if (input.IsActionPressed("build_mode")) SetActive(!_active); if (_active && input.IsActionPressed("ui_cancel")) SetActive(false); if (_active && input is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left) SelectAt(mouse.Position); }

    private void BuildPanel()
    {
        _panel = new PanelContainer { Name = "BuildTools", Visible = false }; _panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft); _panel.OffsetLeft = 18; _panel.OffsetTop = 18; _panel.OffsetRight = 360; _panel.OffsetBottom = 430; var box = new VBoxContainer(); _panel.AddChild(box);
        box.AddChild(new Label { Text = "CONSTRUÇÃO · servidor autoritativo" }); var toggle = new Button { Text = "Construir (B)" }; toggle.Pressed += () => SetActive(!_active); box.AddChild(toggle);
        _primitive = new OptionButton(); foreach (var primitive in new[] { "cube", "sphere", "cylinder", "cone", "plane", "prism", "tube", "ring", "pyramid" }) _primitive.AddItem(primitive); box.AddChild(_primitive);
        var create = new Button { Text = "Criar primitiva" }; create.Pressed += CreateObject; box.AddChild(create); var duplicate = new Button { Text = "Duplicar" }; duplicate.Pressed += Duplicate; box.AddChild(duplicate); var delete = new Button { Text = "Excluir" }; delete.Pressed += Delete; box.AddChild(delete);
        _snap = new LineEdit { Text = "0.5", PlaceholderText = "Snap posição" }; box.AddChild(_snap); _rotation = new LineEdit { Text = "0,0,0", PlaceholderText = "Rotação X,Y,Z" }; box.AddChild(_rotation); _color = new LineEdit { Text = "#A8C7D8", PlaceholderText = "Cor material" }; box.AddChild(_color); _material = new LineEdit { Text = "", PlaceholderText = "Material por face JSON (opcional)" }; box.AddChild(_material); _geometry = new LineEdit { Text = "{\"hollow\":0,\"taperX\":0,\"taperY\":0,\"twist\":0}", PlaceholderText = "Geometria: hollow/taper/twist/cut" }; box.AddChild(_geometry); var apply = new Button { Text = "Aplicar transform/material" }; apply.Pressed += ApplySelected; box.AddChild(apply);
        var copy = new Button { Text = "Copiar propriedades" }; var paste = new Button { Text = "Colar propriedades" }; copy.Pressed += CopyProperties; paste.Pressed += PasteProperties; var tools = new HBoxContainer(); tools.AddChild(copy); tools.AddChild(paste); box.AddChild(tools);
        var history = new HBoxContainer(); var undo = new Button { Text = "Undo" }; undo.Pressed += Undo; var redo = new Button { Text = "Redo" }; redo.Pressed += Redo; history.AddChild(undo); history.AddChild(redo); box.AddChild(history); _status = new Label { Text = "Permissão: desconhecida" }; box.AddChild(_status); AddChild(_panel);
    }
    public void SetActive(bool active)
    {
        _active = active; _panel.Visible = active; if (!active) { ClearSelection(); return; }
        _islandId = _network.CurrentIslandId; if (_islandId == Guid.Empty && Guid.TryParse(OS.GetEnvironment("LIFEVERSE_ISLAND_ID"), out var id)) _islandId = id; if (_islandId != Guid.Empty) LoadObjects(); _status.Text = "Permissão: build/edit validada pelo servidor";
    }
    private void LoadObjects()
    {
        var session = GetNode<SessionManager>("/root/SessionManager"); if (_islandId == Guid.Empty || !session.IsAuthenticated) return; var app = GetNode<AppManager>("/root/AppManager"); _http.Request($"{app.ApiBaseUrl}/api/v1/worlds/islands/{_islandId}/objects", [$"Authorization: Bearer {session.AccessToken}"]);
    }
    private void OnObjectsLoaded(long result, long responseCode, string[] headers, byte[] body)
    {
        if (responseCode is < 200 or >= 300) return; try { using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(body)); foreach (var element in doc.RootElement.EnumerateArray()) AddOrUpdate(JsonSerializer.Deserialize<BuildObjectState>(element.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!); } catch (Exception exception) { GD.PushWarning($"event=build_objects_invalid error={exception.GetType().Name}"); }
    }
    private void CreateObject()
    {
        if (!_active || !_network.IsAuthenticated) { _status.Text = "Entre em uma ilha autenticada antes de construir."; return; } var camera = GetViewport().GetCamera3D(); if (camera is null) return; var origin = camera.ProjectRayOrigin(GetViewport().GetVisibleRect().Size / 2); var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(origin, origin + camera.ProjectRayNormal(GetViewport().GetVisibleRect().Size / 2) * 30)); var position = hit.Count > 0 ? hit["position"].AsVector3() + Vector3.Up * .5f : origin + camera.ProjectRayNormal(GetViewport().GetVisibleRect().Size / 2) * 5;
        var command = Command("create", null, 0, NewPayload(_primitive.GetItemText(_primitive.Selected), position, new Vector3(1, 1, 1), _color.Text)); _network.SubmitBuildOperation(command); _redo.Clear();
    }
    private void SelectAt(Vector2 screenPosition)
    {
        var camera = GetViewport().GetCamera3D(); if (camera is null) return; var from = camera.ProjectRayOrigin(screenPosition); var query = PhysicsRayQueryParameters3D.Create(from, from + camera.ProjectRayNormal(screenPosition) * 100); var hit = GetWorld3D().DirectSpaceState.IntersectRay(query); if (!hit.TryGetValue("collider", out var collider)) { ClearSelection(); return; } var node = collider.AsGodotObject() as Node; while (node is not null && node is not BuildObjectView) node = node.GetParent(); ClearSelection(); if (node is BuildObjectView selected) { _selected = selected; selected.SetSelected(true); _status.Text = $"Selecionado: {selected.State.PrimitiveType} · revisão {selected.State.Revision}"; }
    }
    private void Duplicate() { if (_selected is BuildObjectView view) { var s = view.State; _network.SubmitBuildOperation(Command("create", null, 0, NewPayload(s.PrimitiveType, view.Position + new Vector3(.5f, 0, .5f), view.Scale, s.MaterialJson))); } }
    private void Delete() { if (_selected is not BuildObjectView view) return; _network.SubmitBuildOperation(Command("delete", view.State.Id, view.State.Revision, null)); }
    private void ApplySelected()
    {
        if (_selected is not BuildObjectView view) return; var snap = float.TryParse(_snap.Text, out var parsed) ? Math.Clamp(parsed, .01f, 100f) : .5f; var p = view.Position; p.X = Mathf.Snapped(p.X, snap); p.Y = Mathf.Snapped(p.Y, snap); p.Z = Mathf.Snapped(p.Z, snap); var rotation = ParseVector(_rotation.Text, view.RotationDegrees); view.RotationDegrees = rotation; var material = string.IsNullOrWhiteSpace(_material.Text) ? (string.IsNullOrWhiteSpace(_color.Text) ? view.State.MaterialJson : JsonSerializer.Serialize(new { color = _color.Text, roughness = .7f })) : _material.Text; _network.SubmitBuildOperation(Command("update", view.State.Id, view.State.Revision, NewPayload(view.State.PrimitiveType, p, view.Scale, material, _geometry.Text, rotation)));
    }
    private void Undo() { if (_undo.Count == 0) return; var command = _undo.Pop(); _redo.Push(command); _network.SubmitBuildOperation(command); _status.Text = "Undo enviado; a revisão será validada pelo servidor."; }
    private void Redo() { if (_redo.Count == 0) return; var command = _redo.Pop(); _undo.Push(command); _network.SubmitBuildOperation(command); }
    private void OnOperation(string json) { try { using var doc = JsonDocument.Parse(json); var operation = doc.RootElement.GetProperty("operation").GetString(); if (doc.RootElement.TryGetProperty("objectState", out var state) && state.ValueKind != JsonValueKind.Null) { var objectState = JsonSerializer.Deserialize<BuildObjectState>(state.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!; AddOrUpdate(objectState); if (operation == "create") _undo.Push(Command("delete", objectState.Id, objectState.Revision, null)); } else if (doc.RootElement.TryGetProperty("objectId", out var id) && Guid.TryParse(id.GetString(), out var objectId) && _objects.Remove(objectId, out var removed)) removed.QueueFree(); } catch { } }
    private void OnRejected(string code) => _status.Text = $"Construção rejeitada: {code}";
    private void AddOrUpdate(BuildObjectState state) { if (_objects.TryGetValue(state.Id, out var existing)) { existing.Apply(state); return; } var view = new BuildObjectView(); view.Apply(state); GetParent().AddChild(view); _objects[state.Id] = view; }
    private void ClearSelection() { if (_selected is BuildObjectView old) old.SetSelected(false); _selected = null; }
    private void CopyProperties() { if (_selected is BuildObjectView view) _clipboard = JsonSerializer.Serialize(new { materialJson = view.State.MaterialJson, geometryJson = view.State.GeometryJson, scale = view.Scale, rotation = view.RotationDegrees }); }
    private void PasteProperties() { if (_selected is not BuildObjectView view || string.IsNullOrWhiteSpace(_clipboard)) return; try { using var doc = JsonDocument.Parse(_clipboard); _material.Text = doc.RootElement.GetProperty("materialJson").GetString() ?? "{}"; _geometry.Text = doc.RootElement.GetProperty("geometryJson").GetString() ?? "{}"; } catch { _status.Text = "Propriedades inválidas no clipboard."; } }
    private static Vector3 ParseVector(string text, Vector3 fallback) { var p = text.Split(',', StringSplitOptions.TrimEntries); return p.Length == 3 && p.All(x => float.TryParse(x, out _)) ? new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2])) : fallback; }
    private static Dictionary<string, object?> NewPayload(string primitive, Vector3 p, Vector3 scale, string material, string geometry = "{}", Vector3? rotation = null) { var r = rotation ?? Vector3.Zero; return new() { ["primitiveType"] = primitive, ["positionX"] = p.X, ["positionY"] = p.Y, ["positionZ"] = p.Z, ["rotationX"] = r.X, ["rotationY"] = r.Y, ["rotationZ"] = r.Z, ["scaleX"] = scale.X, ["scaleY"] = scale.Y, ["scaleZ"] = scale.Z, ["materialJson"] = material, ["geometryJson"] = geometry, ["physicsJson"] = "{\"collision\":true}", ["groupId"] = null, ["expectedRevision"] = 0 }; }
    private static string Command(string operation, Guid? objectId, int revision, object? payload) => JsonSerializer.Serialize(new { operation, objectId, @object = payload, expectedRevision = revision });
}
