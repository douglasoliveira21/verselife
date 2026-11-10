using NV = System.Numerics.Vector3;
using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Building;

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
    private bool _selectOnly;
    private bool _faceTool;
    private int _selectedFace = -1;
    private BuildGrid _grid = null!;
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
        _grid = new BuildGrid(); AddChild(_grid);
        BuildPanel();
        SetUpTextureCache();
        SetUpCamera();
    }

    public override void _ExitTree()
    {
        SaveBuildWindowLayout();
        if (_network is null) return;
        _network.BuildOperationReceived -= OnOperation; _network.BuildOperationRejected -= OnRejected; _network.ConnectionStateChanged -= OnConnectionState;
    }

    /// Players see the island's objects as soon as they are online, not only after opening the build tools.
    private void OnConnectionState(string state, string detail)
    {
        if (state == "online") { _islandId = _network.CurrentIslandId; LoadObjects(); }
        else if (state is "offline" or "failed" or "switching") { _lockedBy.Clear(); _myLocks.Clear(); SettleAll(); _loadQueue.Clear(); _cameraRig?.Exit(); CancelDrag(); ClearSelection(); foreach (var view in _objects.Values) if (IsInstanceValid(view)) view.QueueFree(); _objects.Clear(); }
    }

    // ---- activation and input --------------------------------------------------------------------------------

    public void SetActive(bool active)
    {
        _active = active; _window.Visible = active;
        if (!active) { SaveBuildWindowLayout(); CancelDrag(); DisarmPlacement(null); ClearSelection(); _cameraRig?.Exit(); return; }
        _islandId = _network.CurrentIslandId;
        if (_islandId == Guid.Empty && Guid.TryParse(OS.GetEnvironment("LIFEVERSE_ISLAND_ID"), out var id)) _islandId = id;
        if (_islandId != Guid.Empty) LoadObjects();
        _limitsClock = 0; RefreshLimits(); StartTutorialIfNew();
        _status.Text = "Modo de construção ativo · Alterações são salvas automaticamente no servidor.";
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
        if (input.IsActionPressed("build_mode")) { SetActive(!_active); return; }
        if (!_active) return;
        if (input.IsActionPressed("ui_cancel")) { if (_placeType is not null || _previewRoot is not null) DisarmPlacement("Colocação cancelada."); else if (_dragging) CancelDrag(); else SetActive(false); return; }
        if (input is InputEventKey { Pressed: true, Echo: false } key) { if (HandleKey(key)) GetViewport().SetInputAsHandled(); return; }
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: false } click)
        {
            if (_previewRoot is not null && TryConfirmPreview(click.Position)) { GetViewport().SetInputAsHandled(); return; }
            if (_placeType is not null) { TryPlaceClick(click.Position, click.ShiftPressed); GetViewport().SetInputAsHandled(); return; }
            if (OnPress(click.Position, click.ShiftPressed)) GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        PumpLoad();
        UpdateGhost(); UpdatePreview();
        PumpRez(delta);
        PumpCollab(delta);
        if (_hierarchyDirty && _advancedHierarchyOpen) RefreshHierarchy();
        UpdateSaveLabel();
        PumpMarquee();
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
            case Key.Key1: SelectTool(); return true;
            case Key.Key2: SetMode(GizmoMode.Move); return true;
            case Key.Key3: SetMode(GizmoMode.Rotate); return true;
            case Key.Key4: SetMode(GizmoMode.Scale); return true;
            case Key.Key5: FaceTool(); return true;
            case Key.Home: FocusSelection(); return true;
            case Key.End: ToggleEditCamera(); return true;
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
        var picked = PickObject(origin, direction, out var face);
        if (picked is null) { BeginMarquee(screen, additive); if (!additive) ClearSelection(); return false; }
        if (!(_faceTool && additive && ReferenceEquals(_faceView, picked))) SelectView(picked, additive); // Shift + click on the same object adds faces instead of toggling the object
        if (_faceTool) SelectFace(picked, face, additive);
        return false;
    }

    private BuildObjectView? PickObject(Vector3 origin, Vector3 direction) => PickObject(origin, direction, out _);

    private BuildObjectView? PickObject(Vector3 origin, Vector3 direction, out int face)
    {
        face = -1;
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + direction * 150f, uint.MaxValue & ~BuildGizmo.PhysicsLayer);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (!hit.TryGetValue("collider", out var collider)) return null;
        var node = collider.AsGodotObject() as Node;
        while (node is not null and not BuildObjectView) node = node.GetParent();
        if (node is BuildObjectView picked && hit.TryGetValue("face_index", out var index) && index.VariantType == Variant.Type.Int) face = picked.FaceAtTriangle((int)index);
        return node as BuildObjectView;
    }

    // ---- selection -------------------------------------------------------------------------------------------

    private void SelectView(BuildObjectView view, bool additive)
    {
        var members = MembersOf(view);
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
        _hierarchyDirty = true; UpdatePivotInfo(); FillPivotFields(); UpdateArrayInfo();
        if (_faceView is not null && !_selection.Contains(_faceView)) ClearFace(); // a face belongs to the object it was picked on
        FillFields();
        RefreshGizmo();
        SyncLocks();
        if (_selection.Count > 0 && SelectionLockedByOther(out var who)) _status.Text = $"{who} está editando este objeto: você pode olhar, mas não alterar.";
    }

    private void RefreshGizmo()
    {
        UpdateGrid();
        if (_selection.Count == 0 || !_active || _selectOnly || _faceTool || SelectionLockedByOther(out _) || _selection.Any(v => v.State.EditLocked)) { _gizmo.HideHandles(); return; }
        var center = PivotWorld(); // where things turn and stretch around: the centre of the selection unless another pivot was chosen
        var local = Space == AxisSpace.Local || (_mode == GizmoMode.Scale && _selection.Count == 1 && !ReferenceChosen);
        var axes = local ? Basis.FromEuler(ReferenceChosen ? AxesEuler : _selection[0].Rotation) : Basis.Identity;
        _gizmo.ShowAt(center, _mode, axes);
        UpdatePivotInfo();
    }

    private void SetMode(GizmoMode mode)
    {
        _mode = mode; _selectOnly = false; _faceTool = false; ClearFace(); DisarmPlacement(null);
        UpdateToolButtons();
        RefreshGizmo();
    }

    private void SelectTool()
    {
        CancelDrag(); _selectOnly = true; _faceTool = false; ClearFace(); DisarmPlacement(null);
        UpdateToolButtons();
        RefreshGizmo();
    }

    // ---- dragging with the handles ---------------------------------------------------------------------------

    public bool BeginDrag(int axis, Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (_selection.Count == 0 || !_gizmo.BeginDrag(axis, rayOrigin, rayDirection)) return false;
        _dragStart.Clear();
        foreach (var view in _selection) _dragStart.Add(new DragStart(view, view.Position, Basis.FromEuler(view.Rotation), view.Scale));
        _dragPivot = _gizmo.Pivot; _dragging = true; _dragMoved = false;
        PrepareMagnet();
        return true;
    }

    public void UpdateDrag(Vector3 rayOrigin, Vector3 rayDirection)
    {
        var snap = GridStep();
        // The handles follow the mouse smoothly. Holding Ctrl snaps to the steps (movement step, turn step, 5 % of size); the grid option snaps positions too.
        var steps = Input.IsKeyPressed(Key.Ctrl);
        var rotationStep = RotationStep();
        if (_gizmo.Drag(rayOrigin, rayDirection, steps ? snap : 0f, steps ? Mathf.DegToRad(rotationStep) : 0f, steps ? .05f : 0f) is not { } drag) return;
        _dragMoved |= drag.Mode switch { GizmoMode.Move => !Mathf.IsZeroApprox(drag.Amount), GizmoMode.Rotate => !Mathf.IsZeroApprox(drag.Amount), _ => !Mathf.IsEqualApprox(drag.Amount, 1f) };
        switch (drag.Mode)
        {
            case GizmoMode.Move:
                var amount = drag.Amount; var magnet = MagnetAdjust(drag.AxisDirection, amount);
                if (magnet is { } pulled) amount = pulled.Amount;
                ShowMagnet(magnet);
                foreach (var start in _dragStart)
                {
                    var moved = start.Position + drag.AxisDirection * amount;
                    if (magnet is null && _gridSnap.ButtonPressed && snap > 0f) moved[drag.Axis] = LifeVerse.Shared.Building.AlignMath.SnapToGrid(moved[drag.Axis], snap);
                    start.View.Position = moved;
                }
                _gizmo.GlobalPosition = _dragPivot + drag.AxisDirection * amount;
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
                        if (_stretchBoth is not null && !_stretchBoth.ButtonPressed && _dragPivot.DistanceTo(start.Position) <= .001f) start.View.Position = start.Position + drag.AxisDirection.Normalized() * (scale[drag.Axis] - start.Scale[drag.Axis]) * .5f; // one-sided: the opposite face stays put
                        if (_dragPivot.DistanceTo(start.Position) > .001f) // a pivot away from the centre: the object grows away from it
                        {
                            var factors = drag.Axis switch { 0 => new NV(drag.Amount, 1, 1), 1 => new NV(1, drag.Amount, 1), _ => new NV(1, 1, drag.Amount) };
                            var pose = new Pose(N(start.Position), QuatOf(start.Basis.GetEuler()), N(start.Scale));
                            if (TransformMath.TryScaleAbout(pose, N(_dragPivot), factors, AxisSpace.Local, pose.Rotation, out var grown, out _)) start.View.Position = G(grown.Position);
                        }
                    }
                    else { start.View.Position = _dragPivot + (start.Position - _dragPivot) * drag.Amount; start.View.Scale = new Vector3(ClampScale(start.Scale.X * drag.Amount), ClampScale(start.Scale.Y * drag.Amount), ClampScale(start.Scale.Z * drag.Amount)); }
                }
                break;
        }
    }

    public void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false; _gizmo.EndDrag(); ShowMagnet(null);
        if (_dragMoved) CommitUpdates(_dragStart.Select(start => Tidy(FromView(start.View))).ToList());
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

    /// A drag leaves numbers like 2.3400001; stored values are rounded to a millimetre (a hundredth of a degree for turns).
    private static BuildObjectState Tidy(BuildObjectState s)
    {
        s.PositionX = Mathf.Snapped(s.PositionX, .001f); s.PositionY = Mathf.Snapped(s.PositionY, .001f); s.PositionZ = Mathf.Snapped(s.PositionZ, .001f);
        s.RotationX = Mathf.Snapped(s.RotationX, .01f); s.RotationY = Mathf.Snapped(s.RotationY, .01f); s.RotationZ = Mathf.Snapped(s.RotationZ, .01f);
        s.ScaleX = ClampScale(s.ScaleX); s.ScaleY = ClampScale(s.ScaleY); s.ScaleZ = ClampScale(s.ScaleZ);
        return s;
    }

    private static float ClampScale(float value) => Mathf.Clamp(Mathf.Snapped(value, .001f), .01f, 64f);

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
        if (afters.Any(a => _objects.TryGetValue(a.Id, out var v) && v.State.EditLocked)) { afters = [.. afters.Where(a => !_objects.TryGetValue(a.Id, out var v) || !v.State.EditLocked)]; SelectionProtected(); foreach (var view in _selection) if (IsInstanceValid(view)) view.Apply(view.State); RefreshGizmo(); if (afters.Count == 0) return; } // a protected object is not even moved
        var entry = new HistoryEntry("update", []);
        foreach (var after in afters)
        {
            if (!_objects.TryGetValue(after.Id, out var view)) continue;
            KeepTextureSize(view.State, after);
            entry.Items.Add(new HistoryItem(after.Id, Clone(view.State), Clone(after)));
            TeachFromChange(view.State, after);
            Send(Command("update", after.Id, view.State.Revision, PayloadFrom(after)));
        }
        if (entry.Items.Count > 0) PushHistory(entry);
    }

    /// Places an imported 3D model from the inventory as a "mesh" object, selects it and opens the move handles.
    public void CreateMeshFromAsset(Guid assetId, string name, Vector3 position)
    {
        if (!_network.IsAuthenticated && OperationSink is null) { _status.Text = "Entre em uma ilha autenticada antes de colocar objetos."; return; }
        if (!_active) SetActive(true);
        var template = new BuildObjectState
        {
            PrimitiveType = "mesh", Name = string.IsNullOrWhiteSpace(name) ? "Modelo" : name.Trim(), MaterialJson = "{}", PhysicsJson = JsonSerializer.Serialize(new { collision = ModelImportDialog.ImportedCollisionShape != "none", shape = ModelImportDialog.ImportedCollisionShape }),
            GeometryJson = JsonSerializer.Serialize(new { assetId = assetId.ToString().ToLowerInvariant() }), ScaleX = ModelImportDialog.PlacementSize, ScaleY = ModelImportDialog.PlacementSize, ScaleZ = ModelImportDialog.PlacementSize
        };
        var entry = new HistoryEntry("create", []);
        CreateFrom(template, position, null, created => { entry.Items.Add(new HistoryItem(created.Id, null, Clone(created))); SelectIds([created.Id]); });
        PushHistory(entry);
        _status.Text = $"Modelo \"{template.Name}\" colocado. Use as alças para mover, girar e escalar.";
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
        var created = new List<Guid>(); var entry = new HistoryEntry("create", []); var actions = new List<Action>(); var copyOf = new Dictionary<Guid, Guid>();
        foreach (var state in source)
        {
            var offset = new Vector3(state.PositionX, state.PositionY, state.PositionZ) - center;
            var position = new Vector3(target.X + offset.X, snapToBase ? target.Y + (state.PositionY - lowest) : target.Y + offset.Y, target.Z + offset.Z);
            var expected = source.Count; var template = state; var group = state.GroupId is { } g ? groups[g] : (Guid?)null;
            actions.Add(() => CreateFrom(template, position, group, confirmed =>
            {
                entry.Items.Add(new HistoryItem(confirmed.Id, null, Clone(confirmed))); created.Add(confirmed.Id); copyOf[template.Id] = confirmed.Id;
                if (created.Count == expected) { SelectIds(created); RestoreCopiedHierarchy(source, copyOf, groups); }
            }));
        }
        PushHistory(entry);
        RunPaced(actions);
        Teach(TutorialStep.Duplicate);
    }

    private void DeleteSelection()
    {
        if (_selection.Count == 0 || SelectionProtected()) return;
        if (_selection.FirstOrDefault(view => !IsOwner(view.State)) is { } someoneElses)
        {
            _status.Text = $"“{someoneElses.State.Name}” não é seu e não pode ser excluído.";
            return;
        }
        var entry = new HistoryEntry("delete", []);
        foreach (var view in _selection.ToList())
        {
            entry.Items.Add(new HistoryItem(view.State.Id, Clone(view.State), null));
            Send(Command("delete", view.State.Id, view.State.Revision, null));
        }
        PushHistory(entry);
        ClearSelection();
    }

    private void GroupSelection() => LinkSelection();

    private void Ungroup() => UnlinkSelection();

    // ---- undo / redo -----------------------------------------------------------------------------------------

    private void PushHistory(HistoryEntry entry)
    {
        _undo.Add(entry); _redo.Clear(); _stepped = null;
        if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
    }

    public void Undo() => Step(_undo, _redo, undo: true);
    public void Redo() => Step(_redo, _undo, undo: false);

    private void Step(List<HistoryEntry> from, List<HistoryEntry> to, bool undo)
    {
        if (from.Count == 0) { _status.Text = undo ? "Nada para desfazer." : "Nada para refazer."; return; }
        var entry = from[^1]; from.RemoveAt(from.Count - 1);
        if (entry.Kind is "structure" or "protect")
        {
            if (entry.Kind == "structure") ReplayStructure(entry, undo); else ReplayProtection(entry, undo);
            to.Add(entry); ClearSelection(); _status.Text = undo ? "Desfeito." : "Refeito."; return;
        }
        // An undo of "create" deletes, an undo of "delete" re-creates; redo does the opposite.
        var deleting = (entry.Kind == "create" && undo) || (entry.Kind == "delete" && !undo);
        var creating = (entry.Kind == "delete" && undo) || (entry.Kind == "create" && !undo);
        var skipped = new List<HistoryItem>();
        foreach (var item in entry.Items)
        {
            if (entry.Kind == "update")
            {
                var target = undo ? item.Before : item.After;
                if (target is null || !_objects.TryGetValue(Resolve(item.Id), out var view)) continue;
                if (ChangedByOthersSince(item.Id)) { skipped.Add(item); continue; } // someone else changed it after me: do not write over their work
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
        foreach (var item in skipped) entry.Items.Remove(item);
        if (entry.Items.Count > 0) { to.Add(entry); _stepped = (entry, undo); }
        ClearSelection();
        _status.Text = skipped.Count > 0 ? ConflictText(skipped.Count) : undo ? "Desfeito." : "Refeito.";
    }

    private Guid Resolve(Guid id)
    {
        for (var hops = 0; hops < 16 && _alias.TryGetValue(id, out var next) && next != id; hops++) id = next;
        return id;
    }

    // ---- server traffic --------------------------------------------------------------------------------------

    private void Send(string commandJson)
    {
        TrackPending(commandJson);
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
        if (responseCode is < 200 or >= 300) { _network.ReportWorldProgress("objects", 0, -2); return; } // -2: the list of objects could not be loaded
        try { using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(body)); var count = 0; foreach (var element in doc.RootElement.EnumerateArray()) { count++; _loadQueue.Enqueue(JsonSerializer.Deserialize<BuildObjectState>(element.GetRawText(), Web)!); } _loadDone = 0; _loadTotal = count; _network.ReportWorldProgress("objects", 0, count); }
        catch (Exception exception) { GD.PushWarning($"event=build_objects_invalid error={exception.GetType().Name}"); }
    }

    public void ApplyOperationForTesting(string json) => OnOperation(json);

    private void OnOperation(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var operation = doc.RootElement.GetProperty("operation").GetString();
            if (operation is "sound" or "sound_stop" or "text" or "physics") return; // effects of scripts and the simulation, handled by their own nodes (they carry an objectId but never remove anything)
            if (operation is "lock" or "unlock") { HandleLockMessage(doc.RootElement, operation); return; }
            if (operation == "rez") { OnRezzed(doc.RootElement); return; }
            if (operation is "structure" or "protect" or "properties") { if (doc.RootElement.TryGetProperty("objects", out var changed)) ApplyObjects(changed); return; }
            if (doc.RootElement.TryGetProperty("objectState", out var state) && state.ValueKind != JsonValueKind.Null)
            {
                var objectState = JsonSerializer.Deserialize<BuildObjectState>(state.GetRawText(), Web)!;
                NoteRevision(operation, objectState); AddOrUpdate(objectState); Settle(objectState.Id, objectState.Revision);
                if (operation == "create") SettleCreate(objectState);
                if (operation == "create" && IsMine(objectState) && _createCallbacks.Count > 0) _createCallbacks.Dequeue()(objectState);
                if (_selection.Any(view => view.State.Id == objectState.Id) && !_dragging) AfterSelectionChanged();
            }
            else if (doc.RootElement.TryGetProperty("objectId", out var id) && Guid.TryParse(id.GetString(), out var objectId) && _objects.Remove(objectId, out var removed))
            {
                Settle(objectId, null); _selection.Remove(removed); _lockedBy.Remove(objectId); _myLocks.Remove(objectId); removed.QueueFree(); AfterSelectionChanged();
            }
            if (doc.RootElement.TryGetProperty("healed", out var healed)) ApplyObjects(healed); // what hung under a deleted object was re-hung by the server
            _hierarchyDirty = true;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
    }

    private bool IsMine(BuildObjectState state) => _session.UserId == Guid.Empty || state.CreatorUserId == _session.UserId;

    private bool IsOwner(BuildObjectState state) => _session.UserId == Guid.Empty || state.OwnerUserId == _session.UserId;

    private void OnRejected(string code)
    {
        _createCallbacks.Clear(); RollBackHistoryOfRefusal(); SettleAll();
        CancelRez("Colocação interrompida: o servidor recusou um objeto (limite de objetos ou permissão).");
        foreach (var view in _selection) if (IsInstanceValid(view)) view.Apply(view.State);
        RefreshGizmo();
        RejectCodeText(code);
    }

    private void AddOrUpdate(BuildObjectState state)
    {
        _hierarchyDirty = true;
        if (_objects.TryGetValue(state.Id, out var existing)) { existing.Apply(state); if (_selection.Contains(existing)) existing.SetSelected(true); RefreshLockTag(existing); return; }
        var view = new BuildObjectView(); GetParent().AddChild(view); view.Apply(state); view.SetCollisionPreview(_showCollision?.ButtonPressed ?? false); _objects[state.Id] = view; RefreshLockTag(view);
    }

    public void SelectForTesting(Guid id, bool additive = false) { if (_objects.TryGetValue(Resolve(id), out var view)) SelectView(view, additive); }
    /// Selects an object from the world context menu without opening the build window.
    public void SelectFromContext(Guid id)
    {
        if (!_objects.ContainsKey(Resolve(id))) return;
        SelectIds([id]);
        if (_objects.TryGetValue(Resolve(id), out var view)) _status.Text = IsOwner(view.State)
            ? $"“{view.State.Name}” selecionado. Pressione Delete para excluir."
            : $"“{view.State.Name}” selecionado. Você não é o proprietário.";
    }

    /// The client offers deletion only to the owner. The backend repeats this check.
    public void DeleteOwnedObject(Guid id)
    {
        if (!_objects.TryGetValue(Resolve(id), out var view)) return;
        SelectIds([id]);
        if (!IsOwner(view.State)) { _status.Text = $"“{view.State.Name}” não é seu e não pode ser excluído."; return; }
        DeleteSelection();
    }
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
        ["groupId"] = s.GroupId, ["expectedRevision"] = s.Revision, ["name"] = s.Name, ["description"] = s.Description, ["contentJson"] = string.IsNullOrWhiteSpace(s.ContentJson) ? "[]" : s.ContentJson, ["pivotX"] = s.PivotX, ["pivotY"] = s.PivotY, ["pivotZ"] = s.PivotZ
    };

    private static string Command(string operation, Guid? objectId, int revision, object? payload) => JsonSerializer.Serialize(new { operation, objectId, @object = payload, expectedRevision = revision });
}
