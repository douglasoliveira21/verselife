using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// In-world construction editor: select one or several objects (a group is selected as a whole), move / rotate /
/// stretch them with 3D handles, copy, paste, duplicate, delete, group and undo/redo. Every change is sent to the
/// island server as an update with the object's revision; the server decides and replicates the result.
public partial class BuildEditor : Node3D
{
    private const int HistoryLimit = 60;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private sealed class HistoryItem(Guid id, BuildObjectState? before, BuildObjectState? after) { public Guid Id { get; set; } = id; public BuildObjectState? Before { get; set; } = before; public BuildObjectState? After { get; set; } = after; }
    private sealed record HistoryEntry(string Kind, List<HistoryItem> Items);
    private sealed record DragStart(BuildObjectView View, Vector3 Position, Basis Basis, Vector3 Scale);

    private NetworkManager _network = null!;
    private SessionManager _session = null!;
    private HttpRequest _http = null!;
    private BuildGizmo _gizmo = null!;
    private PanelContainer _panel = null!;
    private OptionButton _primitive = null!;
    private LineEdit _snap = null!, _color = null!, _rotation = null!, _geometry = null!, _material = null!, _name = null!;
    private Label _status = null!, _selectionLabel = null!;
    private readonly Dictionary<GizmoMode, Button> _modeButtons = [];
    private readonly Dictionary<Guid, BuildObjectView> _objects = [];
    private readonly List<BuildObjectView> _selection = [];
    private readonly List<BuildObjectState> _clipboard = [];
    private readonly List<HistoryEntry> _undo = [];
    private readonly List<HistoryEntry> _redo = [];
    private readonly Queue<Action<BuildObjectState>> _createCallbacks = new();
    private readonly Dictionary<Guid, Guid> _alias = [];
    private readonly List<DragStart> _dragStart = [];
    private string _propertyClipboard = string.Empty;
    private GizmoMode _mode = GizmoMode.Move;
    private Vector3 _dragPivot;
    private bool _dragging;
    private bool _dragMoved;
    private bool _active;
    private Guid _islandId;

    /// Lets tests capture the operations instead of sending them over the network.
    public Func<string, bool>? OperationSink { get; set; }
    public IReadOnlyList<BuildObjectView> Selection => _selection;
    public bool Active => _active;

    public override void _Ready()
    {
        _network = GetNode<NetworkManager>("/root/NetworkManager"); _session = GetNode<SessionManager>("/root/SessionManager");
        _network.BuildOperationReceived += OnOperation; _network.BuildOperationRejected += OnRejected;
        _http = new HttpRequest(); AddChild(_http); _http.RequestCompleted += OnObjectsLoaded;
        _network.ConnectionStateChanged += OnConnectionState;
        _gizmo = new BuildGizmo { Name = "BuildGizmo" }; AddChild(_gizmo);
        BuildPanel();
    }

    public override void _ExitTree()
    {
        if (_network is null) return;
        _network.BuildOperationReceived -= OnOperation; _network.BuildOperationRejected -= OnRejected; _network.ConnectionStateChanged -= OnConnectionState;
    }

    /// Players see the island's objects as soon as they are online, not only after opening the build tools.
    private void OnConnectionState(string state, string detail)
    {
        if (state == "online") { _islandId = _network.CurrentIslandId; LoadObjects(); }
        else if (state is "offline" or "failed") { CancelDrag(); ClearSelection(); foreach (var view in _objects.Values) if (IsInstanceValid(view)) view.QueueFree(); _objects.Clear(); }
    }

    // ---- activation and input --------------------------------------------------------------------------------

    public void SetActive(bool active)
    {
        _active = active; _panel.Visible = active;
        if (!active) { CancelDrag(); ClearSelection(); return; }
        _islandId = _network.CurrentIslandId;
        if (_islandId == Guid.Empty && Guid.TryParse(OS.GetEnvironment("LIFEVERSE_ISLAND_ID"), out var id)) _islandId = id;
        if (_islandId != Guid.Empty) LoadObjects();
        _status.Text = "Clique em um objeto para selecionar (Shift soma). Atalhos: 1 mover, 2 girar, 3 escalar.";
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input.IsActionPressed("build_mode")) { SetActive(!_active); return; }
        if (!_active) return;
        if (input.IsActionPressed("ui_cancel")) { if (_dragging) CancelDrag(); else SetActive(false); return; }
        if (input is InputEventKey { Pressed: true, Echo: false } key && GetViewport().GuiGetFocusOwner() is not (LineEdit or TextEdit)) { if (HandleKey(key)) GetViewport().SetInputAsHandled(); return; }
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: false } click) { if (OnPress(click.Position, click.ShiftPressed)) GetViewport().SetInputAsHandled(); }
    }

    public override void _Process(double delta)
    {
        if (!_active || !_dragging) return;
        if (!Input.IsMouseButtonPressed(MouseButton.Left)) { EndDrag(); return; }
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        var mouse = GetViewport().GetMousePosition();
        UpdateDrag(camera.ProjectRayOrigin(mouse), camera.ProjectRayNormal(mouse));
    }

    private bool HandleKey(InputEventKey key)
    {
        var ctrl = key.CtrlPressed || key.MetaPressed;
        switch (key.PhysicalKeycode)
        {
            case Key.Key1: SetMode(GizmoMode.Move); return true;
            case Key.Key2: SetMode(GizmoMode.Rotate); return true;
            case Key.Key3: SetMode(GizmoMode.Scale); return true;
            case Key.Delete: DeleteSelection(); return true;
            case Key.C when ctrl: CopySelection(); return true;
            case Key.V when ctrl: Paste(); return true;
            case Key.D when ctrl: Duplicate(); return true;
            case Key.G when ctrl && key.ShiftPressed: Ungroup(); return true;
            case Key.G when ctrl: GroupSelection(); return true;
            case Key.Z when ctrl && !key.ShiftPressed: Undo(); return true;
            case Key.Y when ctrl: Redo(); return true;
            case Key.Z when ctrl && key.ShiftPressed: Redo(); return true;
            default: return false;
        }
    }

    private bool OnPress(Vector2 screen, bool additive)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return false;
        var origin = camera.ProjectRayOrigin(screen); var direction = camera.ProjectRayNormal(screen);
        var axis = _gizmo.HandleAt(origin, direction);
        if (axis >= 0 && _selection.Count > 0) return BeginDrag(axis, origin, direction);
        var picked = PickObject(origin, direction);
        if (picked is null) { if (!additive) ClearSelection(); return false; }
        SelectView(picked, additive);
        return false;
    }

    private BuildObjectView? PickObject(Vector3 origin, Vector3 direction)
    {
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + direction * 150f, uint.MaxValue & ~BuildGizmo.PhysicsLayer);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (!hit.TryGetValue("collider", out var collider)) return null;
        var node = collider.AsGodotObject() as Node;
        while (node is not null and not BuildObjectView) node = node.GetParent();
        return node as BuildObjectView;
    }

    // ---- selection -------------------------------------------------------------------------------------------

    private void SelectView(BuildObjectView view, bool additive)
    {
        var members = view.State.GroupId is { } group ? _objects.Values.Where(x => x.State.GroupId == group).ToList() : [view];
        if (!additive) ClearSelectionVisuals();
        else if (_selection.Contains(view)) { foreach (var member in members) { _selection.Remove(member); member.SetSelected(false); } AfterSelectionChanged(); return; }
        foreach (var member in members) if (!_selection.Contains(member)) { _selection.Add(member); member.SetSelected(true); }
        AfterSelectionChanged();
    }

    public void SelectIds(IEnumerable<Guid> ids)
    {
        ClearSelectionVisuals();
        foreach (var id in ids) if (_objects.TryGetValue(Resolve(id), out var view) && !_selection.Contains(view)) { _selection.Add(view); view.SetSelected(true); }
        AfterSelectionChanged();
    }

    private void ClearSelection() { ClearSelectionVisuals(); AfterSelectionChanged(); }
    private void ClearSelectionVisuals() { foreach (var view in _selection) if (IsInstanceValid(view)) view.SetSelected(false); _selection.Clear(); }

    private void AfterSelectionChanged()
    {
        _selectionLabel.Text = _selection.Count switch
        {
            0 => "Nada selecionado",
            1 => $"Selecionado: {_selection[0].State.Name} · revisão {_selection[0].State.Revision}{(_selection[0].State.GroupId is null ? "" : " · em grupo")}",
            _ => $"{_selection.Count} objetos selecionados"
        };
        if (_selection.Count == 1) { var s = _selection[0].State; _name.Text = s.Name; _rotation.Text = $"{s.RotationX:0.##},{s.RotationY:0.##},{s.RotationZ:0.##}"; _geometry.Text = s.GeometryJson; }
        RefreshGizmo();
    }

    private void RefreshGizmo()
    {
        if (_selection.Count == 0 || !_active) { _gizmo.HideHandles(); return; }
        var center = _selection.Aggregate(Vector3.Zero, (sum, view) => sum + view.Position) / _selection.Count;
        var axes = _mode == GizmoMode.Scale && _selection.Count == 1 ? Basis.FromEuler(_selection[0].Rotation) : Basis.Identity;
        _gizmo.ShowAt(center, _mode, axes);
    }

    private void SetMode(GizmoMode mode)
    {
        _mode = mode;
        foreach (var (key, button) in _modeButtons) button.SetPressedNoSignal(key == mode);
        RefreshGizmo();
    }

    // ---- dragging with the handles ---------------------------------------------------------------------------

    public bool BeginDrag(int axis, Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (_selection.Count == 0 || !_gizmo.BeginDrag(axis, rayOrigin, rayDirection)) return false;
        _dragStart.Clear();
        foreach (var view in _selection) _dragStart.Add(new DragStart(view, view.Position, Basis.FromEuler(view.Rotation), view.Scale));
        _dragPivot = _gizmo.Pivot; _dragging = true; _dragMoved = false;
        return true;
    }

    public void UpdateDrag(Vector3 rayOrigin, Vector3 rayDirection)
    {
        var snap = float.TryParse(_snap.Text, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? Mathf.Clamp(parsed, 0f, 100f) : .5f;
        if (_gizmo.Drag(rayOrigin, rayDirection, snap, Mathf.DegToRad(5f)) is not { } drag) return;
        _dragMoved |= drag.Mode switch { GizmoMode.Move => !Mathf.IsZeroApprox(drag.Amount), GizmoMode.Rotate => !Mathf.IsZeroApprox(drag.Amount), _ => !Mathf.IsEqualApprox(drag.Amount, 1f) };
        switch (drag.Mode)
        {
            case GizmoMode.Move:
                foreach (var start in _dragStart) start.View.Position = start.Position + drag.AxisDirection * drag.Amount;
                _gizmo.GlobalPosition = _dragPivot + drag.AxisDirection * drag.Amount;
                break;
            case GizmoMode.Rotate:
            {
                var rotation = new Basis(drag.AxisDirection.Normalized(), drag.Amount);
                foreach (var start in _dragStart) { start.View.Position = _dragPivot + rotation * (start.Position - _dragPivot); start.View.Rotation = (rotation * start.Basis).GetEuler(); }
                break;
            }
            default:
                foreach (var start in _dragStart)
                {
                    if (_dragStart.Count == 1)
                    {
                        var scale = start.Scale; scale[drag.Axis] = ClampScale(start.Scale[drag.Axis] * drag.Amount); start.View.Scale = scale;
                    }
                    else { start.View.Position = _dragPivot + (start.Position - _dragPivot) * drag.Amount; start.View.Scale = new Vector3(ClampScale(start.Scale.X * drag.Amount), ClampScale(start.Scale.Y * drag.Amount), ClampScale(start.Scale.Z * drag.Amount)); }
                }
                break;
        }
    }

    public void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false; _gizmo.EndDrag();
        if (_dragMoved) CommitUpdates(_dragStart.Select(start => FromView(start.View)).ToList());
        _dragStart.Clear();
        RefreshGizmo();
    }

    private void CancelDrag()
    {
        if (!_dragging) return;
        _dragging = false; _gizmo.EndDrag();
        foreach (var start in _dragStart) start.View.Apply(start.View.State);
        _dragStart.Clear();
        RefreshGizmo();
    }

    private static float ClampScale(float value) => Mathf.Clamp(Mathf.Snapped(value, .01f), .01f, 64f);

    private static BuildObjectState FromView(BuildObjectView view)
    {
        var state = Clone(view.State);
        state.PositionX = view.Position.X; state.PositionY = view.Position.Y; state.PositionZ = view.Position.Z;
        state.RotationX = Mathf.RadToDeg(view.Rotation.X); state.RotationY = Mathf.RadToDeg(view.Rotation.Y); state.RotationZ = Mathf.RadToDeg(view.Rotation.Z);
        state.ScaleX = view.Scale.X; state.ScaleY = view.Scale.Y; state.ScaleZ = view.Scale.Z;
        return state;
    }

    // ---- editing actions -------------------------------------------------------------------------------------

    private void CommitUpdates(List<BuildObjectState> afters)
    {
        var entry = new HistoryEntry("update", []);
        foreach (var after in afters)
        {
            if (!_objects.TryGetValue(after.Id, out var view)) continue;
            entry.Items.Add(new HistoryItem(after.Id, Clone(view.State), Clone(after)));
            Send(Command("update", after.Id, view.State.Revision, PayloadFrom(after)));
        }
        if (entry.Items.Count > 0) PushHistory(entry);
    }

    private void ApplySelected()
    {
        if (_selection.Count == 0) { _status.Text = "Selecione um objeto primeiro."; return; }
        var snap = float.TryParse(_snap.Text, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? Mathf.Clamp(parsed, .01f, 100f) : .5f;
        var afters = new List<BuildObjectState>();
        foreach (var view in _selection)
        {
            var after = FromView(view);
            after.PositionX = Mathf.Snapped(after.PositionX, snap); after.PositionY = Mathf.Snapped(after.PositionY, snap); after.PositionZ = Mathf.Snapped(after.PositionZ, snap);
            if (_selection.Count == 1)
            {
                var rotation = ParseVector(_rotation.Text, new Vector3(after.RotationX, after.RotationY, after.RotationZ));
                after.RotationX = rotation.X; after.RotationY = rotation.Y; after.RotationZ = rotation.Z;
                if (!string.IsNullOrWhiteSpace(_name.Text)) after.Name = _name.Text.Trim();
            }
            if (!string.IsNullOrWhiteSpace(_material.Text)) after.MaterialJson = _material.Text;
            else if (!string.IsNullOrWhiteSpace(_color.Text)) after.MaterialJson = JsonSerializer.Serialize(new { color = _color.Text, roughness = .7f });
            if (!string.IsNullOrWhiteSpace(_geometry.Text)) after.GeometryJson = _geometry.Text;
            afters.Add(after);
        }
        CommitUpdates(afters);
    }

    private void CreateObject()
    {
        if (!_active || !_network.IsAuthenticated && OperationSink is null) { _status.Text = "Entre em uma ilha autenticada antes de construir."; return; }
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        var center = GetViewport().GetVisibleRect().Size / 2;
        var origin = camera.ProjectRayOrigin(center); var direction = camera.ProjectRayNormal(center);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(origin, origin + direction * 30f, uint.MaxValue & ~BuildGizmo.PhysicsLayer));
        var position = hit.Count > 0 ? hit["position"].AsVector3() + Vector3.Up * .5f : origin + direction * 5f;
        var template = new BuildObjectState { PrimitiveType = _primitive.GetItemText(_primitive.Selected), MaterialJson = JsonSerializer.Serialize(new { color = string.IsNullOrWhiteSpace(_color.Text) ? "#A8C7D8" : _color.Text, roughness = .7f }), GeometryJson = string.IsNullOrWhiteSpace(_geometry.Text) ? "{}" : _geometry.Text, PhysicsJson = "{\"collision\":true}", Name = string.IsNullOrWhiteSpace(_name.Text) ? "Object" : _name.Text.Trim() };
        var entry = new HistoryEntry("create", []);
        CreateFrom(template, position, null, created => { var item = new HistoryItem(created.Id, null, Clone(created)); entry.Items.Add(item); SelectIds([created.Id]); });
        PushHistory(entry);
    }

    /// Creates a new object that looks like `template` at a position; `onCreated` runs when the server confirms it.
    private void CreateFrom(BuildObjectState template, Vector3 position, Guid? group, Action<BuildObjectState> onCreated)
    {
        var state = Clone(template);
        state.PositionX = position.X; state.PositionY = position.Y; state.PositionZ = position.Z; state.GroupId = group;
        _createCallbacks.Enqueue(onCreated);
        Send(Command("create", null, 0, PayloadFrom(state)));
    }

    private void CopySelection()
    {
        if (_selection.Count == 0) { _status.Text = "Nada para copiar."; return; }
        _clipboard.Clear(); foreach (var view in _selection) _clipboard.Add(Clone(FromView(view)));
        _status.Text = $"{_clipboard.Count} objeto(s) copiado(s). Ctrl+V cola onde o cursor aponta.";
    }

    private void Paste()
    {
        if (_clipboard.Count == 0) { _status.Text = "A área de transferência está vazia."; return; }
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        var mouse = GetViewport().GetMousePosition();
        var origin = camera.ProjectRayOrigin(mouse); var direction = camera.ProjectRayNormal(mouse);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(origin, origin + direction * 150f, uint.MaxValue & ~BuildGizmo.PhysicsLayer));
        PlaceCopies(_clipboard, hit.Count > 0 ? hit["position"].AsVector3() : origin + direction * 6f, snapToBase: hit.Count > 0);
    }

    private void Duplicate()
    {
        if (_selection.Count == 0) { _status.Text = "Selecione um objeto para duplicar."; return; }
        var copies = _selection.Select(view => Clone(FromView(view))).ToList();
        var center = copies.Aggregate(Vector3.Zero, (sum, s) => sum + new Vector3(s.PositionX, s.PositionY, s.PositionZ)) / copies.Count;
        PlaceCopies(copies, center + new Vector3(1f, 0, 1f), snapToBase: false);
    }

    /// Places copies so their center lands on `target`; groups are preserved as new groups, never shared with the originals.
    private void PlaceCopies(List<BuildObjectState> source, Vector3 target, bool snapToBase)
    {
        if (!_network.IsAuthenticated && OperationSink is null) { _status.Text = "Entre em uma ilha autenticada antes de construir."; return; }
        var center = source.Aggregate(Vector3.Zero, (sum, s) => sum + new Vector3(s.PositionX, s.PositionY, s.PositionZ)) / source.Count;
        var lowest = source.Min(s => s.PositionY - s.ScaleY * .5f);
        var groups = source.Where(s => s.GroupId is not null).Select(s => s.GroupId!.Value).Distinct().ToDictionary(id => id, _ => Guid.NewGuid());
        var created = new List<Guid>(); var entry = new HistoryEntry("create", []);
        foreach (var state in source)
        {
            var offset = new Vector3(state.PositionX, state.PositionY, state.PositionZ) - center;
            var position = new Vector3(target.X + offset.X, snapToBase ? target.Y + (state.PositionY - lowest) : target.Y + offset.Y, target.Z + offset.Z);
            var expected = source.Count;
            CreateFrom(state, position, state.GroupId is { } g ? groups[g] : null, confirmed =>
            {
                entry.Items.Add(new HistoryItem(confirmed.Id, null, Clone(confirmed))); created.Add(confirmed.Id);
                if (created.Count == expected) SelectIds(created);
            });
        }
        PushHistory(entry);
    }

    private void DeleteSelection()
    {
        if (_selection.Count == 0) return;
        var entry = new HistoryEntry("delete", []);
        foreach (var view in _selection.ToList())
        {
            entry.Items.Add(new HistoryItem(view.State.Id, Clone(view.State), null));
            Send(Command("delete", view.State.Id, view.State.Revision, null));
        }
        PushHistory(entry);
        ClearSelection();
    }

    private void GroupSelection()
    {
        if (_selection.Count < 2) { _status.Text = "Selecione dois ou mais objetos para agrupar."; return; }
        var group = Guid.NewGuid();
        CommitUpdates(_selection.Select(view => { var state = FromView(view); state.GroupId = group; return state; }).ToList());
        _status.Text = $"{_selection.Count} objetos agrupados; clicar em um seleciona todos.";
    }

    private void Ungroup()
    {
        if (_selection.All(view => view.State.GroupId is null)) { _status.Text = "A seleção não está agrupada."; return; }
        CommitUpdates(_selection.Where(view => view.State.GroupId is not null).Select(view => { var state = FromView(view); state.GroupId = null; return state; }).ToList());
        _status.Text = "Grupo desfeito.";
    }

    // ---- undo / redo -----------------------------------------------------------------------------------------

    private void PushHistory(HistoryEntry entry)
    {
        _undo.Add(entry); _redo.Clear();
        if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
    }

    public void Undo() => Step(_undo, _redo, undo: true);
    public void Redo() => Step(_redo, _undo, undo: false);

    private void Step(List<HistoryEntry> from, List<HistoryEntry> to, bool undo)
    {
        if (from.Count == 0) { _status.Text = undo ? "Nada para desfazer." : "Nada para refazer."; return; }
        var entry = from[^1]; from.RemoveAt(from.Count - 1);
        // An undo of "create" deletes, an undo of "delete" re-creates; redo does the opposite.
        var deleting = (entry.Kind == "create" && undo) || (entry.Kind == "delete" && !undo);
        var creating = (entry.Kind == "delete" && undo) || (entry.Kind == "create" && !undo);
        foreach (var item in entry.Items)
        {
            if (entry.Kind == "update")
            {
                var target = undo ? item.Before : item.After;
                if (target is null || !_objects.TryGetValue(Resolve(item.Id), out var view)) continue;
                var state = Clone(target); state.Id = view.State.Id;
                Send(Command("update", state.Id, view.State.Revision, PayloadFrom(state)));
            }
            else if (deleting)
            {
                if (_objects.TryGetValue(Resolve(item.Id), out var view)) Send(Command("delete", view.State.Id, view.State.Revision, null));
            }
            else if (creating)
            {
                var template = undo ? item.Before : item.After;
                if (template is null) continue;
                var captured = item; var old = item.Id;
                _createCallbacks.Enqueue(created => { _alias[old] = created.Id; captured.Id = created.Id; });
                Send(Command("create", null, 0, PayloadFrom(Clone(template))));
            }
        }
        to.Add(entry);
        ClearSelection();
        _status.Text = undo ? "Desfeito." : "Refeito.";
    }

    private Guid Resolve(Guid id)
    {
        for (var hops = 0; hops < 16 && _alias.TryGetValue(id, out var next) && next != id; hops++) id = next;
        return id;
    }

    // ---- server traffic --------------------------------------------------------------------------------------

    private void Send(string commandJson)
    {
        if (OperationSink is not null && OperationSink(commandJson)) return;
        _network.SubmitBuildOperation(commandJson);
    }

    private void LoadObjects()
    {
        if (_islandId == Guid.Empty || !_session.IsAuthenticated) return;
        var app = GetNode<AppManager>("/root/AppManager");
        _http.Request($"{app.ApiBaseUrl}/api/v1/worlds/islands/{_islandId}/objects", [$"Authorization: Bearer {_session.AccessToken}"]);
    }

    private void OnObjectsLoaded(long result, long responseCode, string[] headers, byte[] body)
    {
        if (responseCode is < 200 or >= 300) return;
        try { using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(body)); foreach (var element in doc.RootElement.EnumerateArray()) AddOrUpdate(JsonSerializer.Deserialize<BuildObjectState>(element.GetRawText(), Web)!); }
        catch (Exception exception) { GD.PushWarning($"event=build_objects_invalid error={exception.GetType().Name}"); }
    }

    public void ApplyOperationForTesting(string json) => OnOperation(json);

    private void OnOperation(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var operation = doc.RootElement.GetProperty("operation").GetString();
            if (doc.RootElement.TryGetProperty("objectState", out var state) && state.ValueKind != JsonValueKind.Null)
            {
                var objectState = JsonSerializer.Deserialize<BuildObjectState>(state.GetRawText(), Web)!;
                AddOrUpdate(objectState);
                if (operation == "create" && IsMine(objectState) && _createCallbacks.Count > 0) _createCallbacks.Dequeue()(objectState);
                if (_selection.Any(view => view.State.Id == objectState.Id) && !_dragging) AfterSelectionChanged();
            }
            else if (doc.RootElement.TryGetProperty("objectId", out var id) && Guid.TryParse(id.GetString(), out var objectId) && _objects.Remove(objectId, out var removed))
            {
                _selection.Remove(removed); removed.QueueFree(); AfterSelectionChanged();
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
    }

    private bool IsMine(BuildObjectState state) => _session.UserId == Guid.Empty || state.CreatorUserId == _session.UserId;

    private void OnRejected(string code)
    {
        _createCallbacks.Clear();
        foreach (var view in _selection) if (IsInstanceValid(view)) view.Apply(view.State);
        RefreshGizmo();
        _status.Text = code switch { "revision_conflict" => "Outra pessoa alterou este objeto antes; a edição foi recusada.", "rate_limit" => "Muitas alterações seguidas; aguarde um instante.", "object_quota" or "server_rejected_409" => "A ilha atingiu o limite de objetos.", _ => $"Construção rejeitada: {code}" };
    }

    private void AddOrUpdate(BuildObjectState state)
    {
        if (_objects.TryGetValue(state.Id, out var existing)) { existing.Apply(state); if (_selection.Contains(existing)) existing.SetSelected(true); return; }
        var view = new BuildObjectView(); view.Apply(state); GetParent().AddChild(view); _objects[state.Id] = view;
    }

    public void SelectForTesting(Guid id, bool additive = false) { if (_objects.TryGetValue(Resolve(id), out var view)) SelectView(view, additive); }
    public void LoadStatesForTesting(IEnumerable<BuildObjectState> states) { foreach (var state in states) AddOrUpdate(state); }
    public BuildObjectView? ViewForTesting(Guid id) => _objects.GetValueOrDefault(Resolve(id));
    public int ObjectCountForTesting => _objects.Count;
    public void SetModeForTesting(GizmoMode mode) => SetMode(mode);
    public void CommandForTesting(string command)
    {
        switch (command)
        {
            case "copy": CopySelection(); break;
            case "paste": Paste(); break;
            case "duplicate": Duplicate(); break;
            case "delete": DeleteSelection(); break;
            case "group": GroupSelection(); break;
            case "ungroup": Ungroup(); break;
            case "undo": Undo(); break;
            case "redo": Redo(); break;
            case "create": CreateObject(); break;
        }
    }

    // ---- panel and payload helpers ---------------------------------------------------------------------------

    private void BuildPanel()
    {
        _panel = new PanelContainer { Name = "BuildTools", Visible = false };
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _panel.OffsetLeft = 18; _panel.OffsetTop = 44; _panel.OffsetRight = 372; _panel.OffsetBottom = 45;
        var box = new VBoxContainer(); _panel.AddChild(box);
        box.AddChild(new Label { Text = "CONSTRUÇÃO · servidor autoritativo (B)" });
        var modes = new HBoxContainer();
        foreach (var (mode, text) in new[] { (GizmoMode.Move, "Mover (1)"), (GizmoMode.Rotate, "Girar (2)"), (GizmoMode.Scale, "Escalar (3)") })
        {
            var button = new Button { Text = text, ToggleMode = true, ButtonPressed = mode == _mode, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var captured = mode; button.Pressed += () => SetMode(captured);
            _modeButtons[mode] = button; modes.AddChild(button);
        }
        box.AddChild(modes);
        _selectionLabel = new Label { Text = "Nada selecionado" }; box.AddChild(_selectionLabel);
        _primitive = new OptionButton();
        foreach (var primitive in new[] { "cube", "sphere", "cylinder", "cone", "plane", "prism", "tube", "ring", "pyramid" }) _primitive.AddItem(primitive);
        box.AddChild(_primitive);
        box.AddChild(Row(("Criar primitiva", CreateObject)));
        box.AddChild(Row(("Copiar", CopySelection), ("Colar", Paste), ("Duplicar", Duplicate), ("Excluir", DeleteSelection)));
        box.AddChild(Row(("Agrupar", GroupSelection), ("Desagrupar", Ungroup)));
        box.AddChild(Row(("Desfazer", Undo), ("Refazer", Redo)));
        _snap = new LineEdit { Text = "0.5", PlaceholderText = "Snap de posição (m)", TooltipText = "Passo do movimento e da posição aplicada" }; box.AddChild(_snap);
        _rotation = new LineEdit { Text = "0,0,0", PlaceholderText = "Rotação X,Y,Z (graus)" }; box.AddChild(_rotation);
        _name = new LineEdit { Text = "Object", PlaceholderText = "Nome do objeto" }; box.AddChild(_name);
        _color = new LineEdit { Text = "#A8C7D8", PlaceholderText = "Cor do material" }; box.AddChild(_color);
        _material = new LineEdit { PlaceholderText = "Material por face JSON (opcional)" }; box.AddChild(_material);
        _geometry = new LineEdit { Text = "{\"hollow\":0,\"taperX\":0,\"taperY\":0,\"twist\":0}", PlaceholderText = "Geometria: hollow/taper/twist/cut" }; box.AddChild(_geometry);
        box.AddChild(Row(("Aplicar propriedades à seleção", ApplySelected)));
        box.AddChild(Row(("Copiar propriedades", CopyProperties), ("Colar propriedades", PasteProperties)));
        _status = new Label { Text = "Permissão: build/edit validada pelo servidor", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(320, 0) }; box.AddChild(_status);
        AddChild(_panel);
    }

    private static HBoxContainer Row(params (string Text, Action Action)[] buttons)
    {
        var row = new HBoxContainer();
        foreach (var (text, action) in buttons) { var button = new Button { Text = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; button.Pressed += action; row.AddChild(button); }
        return row;
    }

    private void CopyProperties()
    {
        if (_selection.Count == 0) return;
        var s = _selection[0].State;
        _propertyClipboard = JsonSerializer.Serialize(new { materialJson = s.MaterialJson, geometryJson = s.GeometryJson });
        _status.Text = "Propriedades copiadas.";
    }

    private void PasteProperties()
    {
        if (string.IsNullOrWhiteSpace(_propertyClipboard)) return;
        try { using var doc = JsonDocument.Parse(_propertyClipboard); _material.Text = doc.RootElement.GetProperty("materialJson").GetString() ?? "{}"; _geometry.Text = doc.RootElement.GetProperty("geometryJson").GetString() ?? "{}"; _status.Text = "Propriedades coladas nos campos; use Aplicar."; }
        catch (JsonException) { _status.Text = "Propriedades inválidas na área de transferência."; }
    }

    private static Vector3 ParseVector(string text, Vector3 fallback)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 3 && parts.All(x => float.TryParse(x, System.Globalization.CultureInfo.InvariantCulture, out _))
            ? new Vector3(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)) : fallback;
    }

    private static BuildObjectState Clone(BuildObjectState state) => JsonSerializer.Deserialize<BuildObjectState>(JsonSerializer.Serialize(state, Web), Web)!;

    /// Every field the API overwrites on update, including the group, so a move never ungroups an object.
    private static Dictionary<string, object?> PayloadFrom(BuildObjectState s) => new()
    {
        ["primitiveType"] = s.PrimitiveType, ["positionX"] = s.PositionX, ["positionY"] = s.PositionY, ["positionZ"] = s.PositionZ,
        ["rotationX"] = s.RotationX, ["rotationY"] = s.RotationY, ["rotationZ"] = s.RotationZ, ["scaleX"] = s.ScaleX, ["scaleY"] = s.ScaleY, ["scaleZ"] = s.ScaleZ,
        ["materialJson"] = s.MaterialJson, ["geometryJson"] = s.GeometryJson, ["physicsJson"] = string.IsNullOrWhiteSpace(s.PhysicsJson) ? "{\"collision\":true}" : s.PhysicsJson,
        ["groupId"] = s.GroupId, ["expectedRevision"] = s.Revision, ["name"] = s.Name
    };

    private static string Command(string operation, Guid? objectId, int revision, object? payload) => JsonSerializer.Serialize(new { operation, objectId, @object = payload, expectedRevision = revision });
}
