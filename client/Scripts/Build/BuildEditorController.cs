using LifeVerse.Shared.Building;
using System.Globalization;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

/// <summary>
/// The new Construir editor. It lives in the world, owns the modules (synchronizer, selection, transforms, handles, history, window) and routes the player's input:
/// B opens and closes it, 1 to 5 pick the tools, a click selects, places or grabs a handle, Ctrl+D duplicates, Delete removes, Ctrl+Z and Ctrl+Y undo and redo, Esc backs
/// out. Every change is a command to the island server; what the person sees follows the answer of the server.
/// </summary>
public partial class BuildEditorController : Node3D
{
    private const float SpotReach = 150f;
    private const float DuplicateGap = .25f;

    private BuildNetworkSynchronizer _sync = null!;
    private BuildSelectionManager _selection = null!;
    private BuildTransformManager _transform = null!;
    private BuildGizmoController _gizmo = null!;
    private BuildUndoRedoManager _history = null!;
    private BuildFaceSelectionManager _faces = null!;
    private BuildNameDirectory _names = null!;
    private BuildObjectProperties _properties = null!;
    private BuildGeometryManager _geometry = null!;
    private BuildPhysicsManager _bodyFlags = null!;
    private BuildAlignManager _align = null!;
    private BuildContentManager _contentManager = null!;
    private BuildLockManager _locks = null!;
    private BuildAuditManager _audit = null!;
    private BuildPackageManager _packages = null!;
    private BuildCamera _cameraRig = null!;
    private AcceptDialog _historyDialog = null!;
    private Label _historyText = null!;
    private BuildResourceManager _resourceManager = null!;
    private BuildMaterialManager _materials = null!;
    private BuildTexturePicker _picker = null!;
    private BuildMediaPanel _mediaPanel = null!;
    private BuildLinkManager _links = null!;
    private BuildEditorWindow _window = null!;
    private BuildTool _tool = BuildTool.Select;
    private bool _active, _dragging;
    private string? _placeType;
    private BuildObjectView? _ghost;
    private double _limitsClock;
    private readonly List<BuildUndoRedoManager.Change> _dragChanges = [];

    public bool Active => _active;
    public BuildNetworkSynchronizer Synchronizer => _sync;
    public BuildContentManager ContentManager => _contentManager;
    public BuildLockManager Locks => _locks;
    public BuildAuditManager Audit => _audit;
    public BuildPackageManager Packages => _packages;
    public BuildCamera EditCamera => _cameraRig;
    public string HistoryTextForTesting => _historyText.Text;
    public BuildSelectionManager Selection => _selection;
    public BuildTransformManager Transforms => _transform;
    public BuildGizmoController Gizmo => _gizmo;
    public BuildUndoRedoManager History => _history;
    public BuildFaceSelectionManager Faces => _faces;
    public BuildNameDirectory Names => _names;
    public BuildObjectProperties Properties => _properties;
    public BuildGeometryManager Geometry => _geometry;
    public BuildPhysicsManager BodyFlags => _bodyFlags;
    public BuildAlignManager Align => _align;
    public BuildResourceManager ResourceManager => _resourceManager;
    public BuildMaterialManager Materials => _materials;
    public BuildTexturePicker TexturePicker => _picker;
    public BuildLinkManager Links => _links;
    public BuildEditorWindow Window => _window;
    public BuildTool Tool => _tool;
    public string? PlacingType => _placeType;
    public bool Dragging => _dragging;
    public bool GhostVisibleForTesting => _ghost is { Visible: true };

    public override void _Ready()
    {
        _sync = new BuildNetworkSynchronizer { Name = "Synchronizer" }; AddChild(_sync);
        _selection = new BuildSelectionManager { Name = "Selection" }; AddChild(_selection); _selection.Initialize(_sync);
        _transform = new BuildTransformManager { Name = "Transform" }; AddChild(_transform); _transform.Initialize(_sync);
        _history = new BuildUndoRedoManager { Name = "History" }; AddChild(_history); _history.Initialize(_sync);
        _faces = new BuildFaceSelectionManager { Name = "Faces" }; AddChild(_faces); _faces.Initialize(_sync);
        _links = new BuildLinkManager { Name = "Links" }; AddChild(_links); _links.Initialize(_sync);
        _gizmo = new BuildGizmoController { Name = "Gizmo", TopLevel = true }; AddChild(_gizmo);
        _names = new BuildNameDirectory { Name = "Names" }; AddChild(_names);
        _properties = new BuildObjectProperties { Name = "Properties" }; AddChild(_properties); _properties.Initialize(_sync, _selection);
        _geometry = new BuildGeometryManager { Name = "Geometry" }; AddChild(_geometry); _geometry.Initialize(_sync, _selection);
        _bodyFlags = new BuildPhysicsManager { Name = "BodyFlags" }; AddChild(_bodyFlags); _bodyFlags.Initialize(_sync, _selection);
        _align = new BuildAlignManager { Name = "Align" }; AddChild(_align); _align.Initialize(_sync, _selection, _history);
        _resourceManager = new BuildResourceManager { Name = "ResourceManager" }; AddChild(_resourceManager); _resourceManager.Initialize(_sync, _selection);
        _materials = new BuildMaterialManager { Name = "Materials" }; AddChild(_materials); _materials.Initialize(_sync, _selection, _faces, _history);
        _picker = new BuildTexturePicker { Name = "TexturePicker" }; AddChild(_picker);
        _mediaPanel = new BuildMediaPanel { Name = "MediaPanel" }; _mediaPanel.Initialize(_selection, _materials); _mediaPanel.ParcelAt = (x, z) => GetParent()?.GetNodeOrNull<LandManager>("LandManager")?.ParcelAt(x, z);
        _window = new BuildEditorWindow { Name = "Window" }; AddChild(_window);
        _window.General.Initialize(_properties, _names, () => GetParent()?.GetNodeOrNull<LandManager>("LandManager")?.MyGroups ?? []); _window.General.Said += _window.SetStatus;
        _window.Object.Initialize(_sync, _selection, _properties, _geometry, _bodyFlags, _align, GroundAt); _window.Object.Said += _window.SetStatus; _align.Said += _window.SetStatus; _window.Resources.Initialize(_sync, _selection, _resourceManager, _picker); _window.Resources.Said += _window.SetStatus;
        _window.Texture.Initialize(_selection, _materials, _picker, _mediaPanel); _window.Texture.Said += _window.SetStatus; _mediaPanel.Said += _window.SetStatus; _materials.Said += _window.SetStatus; _window.Texture.FaceToolRequested += () => { SetTool(BuildTool.Face); _window.SelectTabForTesting(3); }; _window.Texture.WholeObjectRequested += () => { _faces.Clear(); _window.Texture.Refresh(); };
        _locks = new BuildLockManager { Name = "Locks" }; AddChild(_locks); _locks.Initialize(_sync, _selection); _locks.Changed += () => { if (_locks.SelectionHeldBy() is { } who) _window.SetStatus($"{who} está editando este objeto: você pode olhar, mas não alterar."); };
        _packages = new BuildPackageManager { Name = "Packages" }; AddChild(_packages); _packages.Initialize(_sync, _selection, _history, SpotAt, _window.SetStatus, DeleteSelection, () => { if (!_active) SetActive(true); });
        _window.SavePackageRequested += (name, take) => _packages.SaveSelection(name, take); _window.PresetRequested += id => { Disarm(null); _packages.PlacePreset(id); }; _window.CameraRequested += ToggleEditCamera; _window.TerrainRequested += OpenTerrainTools;
        _cameraRig = new BuildCamera { Name = "BuildCamera", Current = false }; AddChild(_cameraRig); _cameraRig.ActiveChanged += on => { GetNodeOrNull<LifeVerse.Client.Autoloads.NetworkManager>("/root/NetworkManager")?.LocalPlayer?.SetAvatarHidden(on); _window.SetCameraPressed(on); };
        _audit = new BuildAuditManager { Name = "Audit" }; AddChild(_audit); _audit.Initialize(_sync, _selection); _audit.Said += _window.SetStatus;
        _historyDialog = new AcceptDialog { Title = "Histórico do objeto", Theme = ViewerTheme.Create(), MinSize = new Vector2I(560, 320) }; AddChild(_historyDialog); _historyText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _historyDialog.AddChild(_historyText);
        _audit.HistoryReady += text => { _historyText.Text = text; _historyDialog.PopupCentered(); };
        _window.ReturnRequested += () => _audit.ReturnSelection(); _window.HistoryRequested += _audit.ShowHistory;
        _contentManager = new BuildContentManager { Name = "ContentManager" }; AddChild(_contentManager); _contentManager.Initialize(_sync, _selection);
        _window.Content.Initialize(_selection, _contentManager, this); _window.Content.Said += _window.SetStatus;
        EnsureTextureCache();
        _window.Object.Changed += (before, after) => _history.Record("editar números", [new BuildUndoRedoManager.Change { Id = after.Id, Before = before, After = after, Known = before.Revision + 1 }]);
        _sync.Purchased += purchase => _window.SetStatus(purchase.SaleType == "original" ? $"Comprado por {purchase.Price:N0} moedas: agora é seu." : $"Comprado por {purchase.Price:N0} moedas: {string.Join(", ", purchase.Delivered)} no seu inventário.");
        _window.ToolRequested += SetTool; _window.PrimitivePicked += Arm; _window.DuplicateRequested += DuplicateSelection; _window.DeleteRequested += DeleteSelection; _window.ImportRequested += OpenImporter;
        _window.CloseRequested += () => SetActive(false); _window.UndoRequested += Undo; _window.RedoRequested += Redo; _window.OptionsChanged += ApplyOptions;
        _window.LinkRequested += LinkSelection; _window.UnlinkRequested += UnlinkSelection; _window.HierarchyPicked += id => _selection.Set([id]);
        _faces.Changed += () => { if (_tool == BuildTool.Face) _window.SetStatus(_faces.Describe()); _window.Texture.Refresh(); };
        _selection.Changed += OnSelectionChanged; _sync.Rejected += OnRejected; _sync.LimitsChanged += _window.SetLimits;
        _sync.ObjectChanged += (state, _) => { if (_selection.Contains(state.Id)) { OnSelectionChanged(); _window.RefreshPages(); } };
        _history.Message += _window.SetStatus; _history.Changed += () => _window.SetHistory(_history.UndoCount > 0, _history.RedoCount > 0);
        _transform.Committed += (before, after) => _dragChanges.Add(new BuildUndoRedoManager.Change { Id = after.Id, Before = before, After = after, Known = before.Revision + 1 });
        // the objects the synchronizer replicates live next to the other objects of the world, not inside this node
        _sync.SetWorldRoot(GetParent() as Node3D ?? this);
    }


    /// <summary>Textured materials need the cache that fetches the pictures; the classic editor used to create it, so the new one makes sure it is there.</summary>
    private void EnsureTextureCache()
    {
        var parent = GetParent(); if (parent is null || parent.GetNodeOrNull<TextureCache>("TextureCache") is not null) return;
        parent.CallDeferred(Node.MethodName.AddChild, new TextureCache { Name = "TextureCache" });
    }
    // ---- opening and closing ---------------------------------------------------------------------------------

    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active; _window.Visible = active;
        _locks.SetActive(active);
        if (!active) { CancelDrag(); Disarm(null); _packages.CancelPreview(null); _packages.CancelPlacing("Colocação interrompida."); _cameraRig.Exit(); _selection.Clear(); _gizmo.HideGizmo(); return; }
        _window.Focus(); SetTool(BuildTool.Select); ApplyOptions(); RefreshLimits();
        _window.SetStatus("Clique em um objeto para selecioná-lo ou escolha uma forma em Criar.");
    }

    public void SetTool(BuildTool tool)
    {
        CancelDrag();
        if (tool != BuildTool.Create) Disarm(null);
        _tool = tool; _window.SetTool(tool); if (tool != BuildTool.Face) _faces.Clear();
        if (tool == BuildTool.Create) _window.SetStatus("Escolha uma forma para colocar no mundo.");
        else if (tool is BuildTool.Move or BuildTool.Rotate or BuildTool.Scale && _selection.Count == 0) _window.SetStatus("Selecione um objeto para usar as alças.");
        else if (tool == BuildTool.Face) _window.SetStatus("Clique em uma face; Shift soma mais faces do mesmo objeto.");
        RefreshGizmo();
    }

    private void ApplyOptions()
    {
        _transform.Space = _window.Space; _transform.SnapEnabled = _window.SnapEnabled; _transform.MoveStep = _window.MoveStep; _transform.RotateStepDegrees = _window.RotateStepDegrees; _transform.ScaleStep = _window.ScaleStep;
        RefreshGizmo();
    }

    // ---- creating --------------------------------------------------------------------------------------------

    /// <summary>The shape was picked in the grid: from now on a see-through copy follows the cursor until the person clicks.</summary>
    public void Arm(string type)
    {
        if (!_active) return;
        if (!_sync.CanSend) { _window.SetStatus("Entre em uma ilha autenticada antes de construir."); return; }
        if (_placeType == type) { Disarm("Colocação cancelada."); return; }
        CancelDrag(); Disarm(null); _packages.CancelPreview(null); _selection.Clear();
        _placeType = type; _tool = BuildTool.Create; _window.SetTool(BuildTool.Create); _window.Creation.SetArmed(type);
        _ghost = BuildPrimitiveFactory.CreatePreview(GetParent() ?? this, type);
        _window.SetStatus($"Colocando: {BuildPrimitiveFactory.LabelOf(type)}.");
    }

    private void Disarm(string? message)
    {
        _placeType = null; _window?.Creation?.SetArmed(null);
        if (_ghost is not null && IsInstanceValid(_ghost)) _ghost.QueueFree();
        _ghost = null;
        if (message is not null) _window?.SetStatus(message);
    }

    // ---- editing camera and terrain ---------------------------------------------------------------------------------

    /// <summary>The middle and the reach of the selection (or of what the camera looks at), for pointing the camera at it.</summary>
    private (Vector3 Center, float Radius) SelectionSphere()
    {
        var views = _selection.Views().ToList();
        if (views.Count == 0) return (GroundAt(GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero) is { } y ? new Vector3(0, y, 0) : Vector3.Zero, 4f);
        var center = views.Aggregate(Vector3.Zero, (sum, v) => sum + v.GlobalPosition) / views.Count;
        var radius = views.Max(v => v.GlobalPosition.DistanceTo(center) + new Vector3(v.State.ScaleX, v.State.ScaleY, v.State.ScaleZ).Length() / 2f);
        return (center, Mathf.Max(radius, .5f));
    }

    /// <summary>Câmera: circles the selection (right button orbits, middle or Shift+right pans, wheel zooms) instead of following the avatar.</summary>
    public void ToggleEditCamera()
    {
        if (_cameraRig.Active) { _cameraRig.Exit(); _window.SetStatus("Câmera normal."); return; }
        var previous = GetViewport().GetCamera3D(); if (previous is null) return;
        var (center, radius) = SelectionSphere();
        _cameraRig.Enter(previous, center, _selection.Count > 0 ? OrbitMath.FocusDistance(radius, previous.Fov) : 10f);
        _window.SetStatus("Câmera de edição: botão direito gira, botão do meio (ou Shift + direito) desloca, roda dá zoom, Home foca na seleção.");
    }

    public void FocusEditCamera()
    {
        if (_selection.Count == 0) { _window.SetStatus("Selecione um objeto para focar."); return; }
        if (!_cameraRig.Active) ToggleEditCamera();
        var (center, radius) = SelectionSphere(); _cameraRig.Focus(center, radius); _window.SetStatus("Focado na seleção.");
    }

    /// <summary>Terreno: the terrain editor has its own workflow and history, so the building tools step aside and hand over the mouse.</summary>
    public void OpenTerrainTools()
    {
        var terrain = GetParent()?.GetNodeOrNull<TerrainEditor>("TerrainEditor");
        if (terrain is null) { _window.SetStatus("As ferramentas de terreno não estão disponíveis nesta ilha."); return; }
        SetActive(false); terrain.SetActive(true);
    }

    public bool OwnsTheMouse => _active && _cameraRig.Active;

    /// <summary>The point a screen position looks at, lifted along the surface so a one-metre shape rests on it; null when it looks at nothing.</summary>
    public Vector3? SpotAt(Vector2 screen)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return null;
        var origin = camera.ProjectRayOrigin(screen); var direction = camera.ProjectRayNormal(screen);
        // the preview and the selection bodies are not surfaces to stand on
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + direction * SpotReach, uint.MaxValue & ~BuildObjectView.SelectionPhysicsLayer);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0 ? null : BuildPrimitiveFactory.RestingPoint(hit["position"].AsVector3(), hit["normal"].AsVector3());
    }

    /// <summary>Asks the server to create the armed shape at a spot. The object appears when the server confirms it, and is then selected.</summary>
    public bool PlaceAt(Vector3 spot, bool keepPlacing)
    {
        if (_placeType is not { } type) return false;
        var change = new BuildUndoRedoManager.Change();
        var ok = _sync.Create(BuildPrimitiveFactory.NewState(type, spot), created =>
        {
            _history.Created(change, created); _history.Record($"criar {BuildPrimitiveFactory.LabelOf(created.PrimitiveType).ToLowerInvariant()}", [change]);
            _selection.Set([created.Id]); _window.SetStatus($"{BuildPrimitiveFactory.LabelOf(created.PrimitiveType)} criada."); RefreshLimits();
        });
        if (!ok) return true;
        if (!keepPlacing) { Disarm(null); SetTool(BuildTool.Move); }
        else _window.SetStatus($"{BuildPrimitiveFactory.LabelOf(type)} enviada; clique de novo para colocar outra, Esc para parar.");
        return true;
    }

    // ---- imported models -------------------------------------------------------------------------------------------

    private void OpenImporter()
    {
        if (GetParent()?.GetNodeOrNull<InventoryPanel>("InventoryPanel") is { } inventory) { inventory.OpenImporter(); _window.SetStatus("Escolha o arquivo do modelo; depois coloque-o no mundo pelo inventário."); }
        else _window.SetStatus("O inventário não está disponível.");
    }

    /// <summary>Places a model of the inventory in the world as a "mesh" object, with the collision chosen at import.</summary>
    public bool CreateMeshFromAsset(Guid assetId, string name, Vector3 position)
    {
        if (!_sync.CanSend) { _window.SetStatus("Entre em uma ilha autenticada antes de colocar objetos."); return false; }
        SetActive(true);
        var template = BuildPrimitiveFactory.NewState("mesh", position);
        template.Name = string.IsNullOrWhiteSpace(name) ? "Modelo" : name.Trim(); template.MaterialJson = "{}";
        template.PhysicsJson = System.Text.Json.JsonSerializer.Serialize(new { collision = ModelImportDialog.ImportedCollisionShape != "none", shape = ModelImportDialog.ImportedCollisionShape });
        template.GeometryJson = System.Text.Json.JsonSerializer.Serialize(new { assetId = assetId.ToString().ToLowerInvariant() });
        template.ScaleX = template.ScaleY = template.ScaleZ = ModelImportDialog.PlacementSize;
        var change = new BuildUndoRedoManager.Change();
        var ok = _sync.Create(template, created => { _history.Created(change, created); _history.Record("colocar modelo", [change]); _selection.Set([created.Id]); _window.SetStatus($"Modelo \"{created.Name}\" colocado."); RefreshLimits(); });
        if (ok) _window.SetStatus($"Colocando \"{template.Name}\"…");
        return ok;
    }

    // ---- selection, duplicate, delete -----------------------------------------------------------------------------

    public void DuplicateSelection()
    {
        if (_selection.Count == 0) { _window.SetStatus("Selecione um objeto para duplicar."); return; }
        var sources = _selection.Views().Select(v => v.State).ToList();
        var made = new List<Guid>(); var changes = new List<BuildUndoRedoManager.Change>();
        foreach (var state in sources)
        {
            var copy = BuildPersistenceManager.Clone(state); copy.Id = Guid.NewGuid(); copy.GroupId = null; copy.ParentObjectId = null;
            copy.PositionX += MathF.Max(.5f, MathF.Abs(state.ScaleX)) + DuplicateGap; // beside the original, not inside it
            var change = new BuildUndoRedoManager.Change(); changes.Add(change);
            _sync.Create(copy, created =>
            {
                _history.Created(change, created); made.Add(created.Id);
                if (made.Count == sources.Count) { _history.Record(made.Count == 1 ? "duplicar objeto" : "duplicar objetos", changes); _selection.Set(made); _window.SetStatus(made.Count == 1 ? "Objeto duplicado." : $"{made.Count} objetos duplicados."); RefreshLimits(); }
            });
        }
        _window.SetStatus(sources.Count == 1 ? "Duplicando…" : $"Duplicando {sources.Count} objetos…");
    }

    public void DeleteSelection()
    {
        if (_selection.Count == 0) { _window.SetStatus("Selecione um objeto para excluir."); return; }
        var views = _selection.Views().ToList(); var changes = new List<BuildUndoRedoManager.Change>(); var sent = 0;
        foreach (var view in views)
        {
            var state = BuildPersistenceManager.Clone(view.State);
            if (_sync.Delete(state.Id)) { sent++; changes.Add(new BuildUndoRedoManager.Change { Id = state.Id, Before = state, After = null, Known = state.Revision }); }
        }
        if (changes.Count > 0) _history.Record(changes.Count == 1 ? "excluir objeto" : "excluir objetos", changes);
        _window.SetStatus(sent == 1 ? "Excluindo…" : $"Excluindo {sent} objetos…"); RefreshLimits();
    }

    public void Undo() { CancelDrag(); _history.Undo(); }
    public void Redo() { CancelDrag(); _history.Redo(); }

    private void OnSelectionChanged()
    {
        var views = _selection.Views().ToList(); var impact = views.Sum(v => (long)v.State.Cost);
        _window.SetSelection(views.Count, impact, Summarize(views));
        _window.SetCollabEnabled(views.Count);
        var groups = views.Where(v => v.State.GroupId is not null).Select(v => v.State.GroupId).ToHashSet();
        _window.SetCanUnlink(groups.Count > 0);
        _window.SetHierarchy(groups.Count == 0 ? [] : _links.Tree().Where(row => groups.Contains(row.View.State.GroupId)).ToList(), _selection.Ids);
        if (_faces.ObjectId != Guid.Empty && !(views.Count == 1 && views[0].State.Id == _faces.ObjectId)) _faces.Clear();
        if (!_dragging) RefreshGizmo();
    }

    private static string Summarize(IReadOnlyList<BuildObjectView> views)
    {
        if (views.Count == 0) return string.Empty;
        if (views.Count > 1) return $"{views.Count} objetos: {string.Join(", ", views.Take(3).Select(v => v.State.Name))}{(views.Count > 3 ? "…" : string.Empty)}";
        var s = views[0].State; var c = CultureInfo.GetCultureInfo("pt-BR");
        return $"{s.Name}  ·  {BuildPrimitiveFactory.LabelOf(s.PrimitiveType)}\nPosição  {s.PositionX.ToString("0.00", c)}  {s.PositionY.ToString("0.00", c)}  {s.PositionZ.ToString("0.00", c)}\nTamanho  {s.ScaleX.ToString("0.00", c)}  {s.ScaleY.ToString("0.00", c)}  {s.ScaleZ.ToString("0.00", c)}";
    }

    private void OnRejected(string message) { CancelDrag(); _window.SetStatus(message); RefreshLimits(); }

    private void RefreshLimits()
    {
        var camera = GetViewport().GetCamera3D();
        var at = _selection.Views().FirstOrDefault()?.GlobalPosition ?? camera?.GlobalPosition ?? Vector3.Zero;
        _sync.RefreshLimits(at);
    }



    /// <summary>The height of the ground under a point: the first surface below the lowest point of the selection (or the highest one when the selection is under the ground).</summary>
    public float? GroundAt(Vector3 below)
    {
        var exclude = new Godot.Collections.Array<Rid>();
        foreach (var view in _selection.Views()) foreach (var body in view.FindChildren("*", "CollisionObject3D", true, false).OfType<CollisionObject3D>()) exclude.Add(body.GetRid());
        float? Cast(float fromY)
        {
            var query = PhysicsRayQueryParameters3D.Create(new Vector3(below.X, fromY, below.Z), new Vector3(below.X, fromY - 2000f, below.Z), uint.MaxValue & ~BuildObjectView.SelectionPhysicsLayer); query.Exclude = exclude;
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(query); return hit.Count == 0 ? null : hit["position"].AsVector3().Y;
        }
        return Cast(below.Y + .3f) ?? Cast(1000f);
    }
    // ---- faces and links ---------------------------------------------------------------------------------------------

    /// <summary>With the Face tool a click picks the object (if it is not the selected one) and then the face under the cursor.</summary>
    public bool HandleFaceClick(Vector2 screen, bool shift)
    {
        var camera = GetViewport().GetCamera3D(); if (camera is null) return false;
        if (_selection.PickAt(camera, screen, GetWorld3D()) is { } picked && !(_selection.Count == 1 && _selection.Contains(picked.State.Id))) _selection.Set([picked.State.Id]);
        if (_faces.Pick(camera, screen, GetWorld3D(), shift, out var message)) _window.SetStatus(_faces.Describe());
        else if (message.Length > 0) _window.SetStatus(message);
        return true;
    }

    public void LinkSelection()
    {
        var plan = _links.Link([.. _selection.Ids.Select(id => _sync.Views.GetValueOrDefault(id)).Where(v => v is not null)!]);
        if (plan.Sent) _history.Record("vincular", plan.Changes);
        _window.SetStatus(plan.Message);
    }

    public void UnlinkSelection()
    {
        var plan = _links.Unlink([.. _selection.Ids.Select(id => _sync.Views.GetValueOrDefault(id)).Where(v => v is not null)!]);
        if (plan.Sent) _history.Record("desvincular", plan.Changes);
        _window.SetStatus(plan.Message);
    }
    // ---- the handles -------------------------------------------------------------------------------------------------

    private static BuildGizmoMode? ModeOf(BuildTool tool) => tool switch { BuildTool.Move => BuildGizmoMode.Move, BuildTool.Rotate => BuildGizmoMode.Rotate, BuildTool.Scale => BuildGizmoMode.Scale, _ => null };

    /// <summary>Shows the handles of the chosen tool on the selection (or hides them).</summary>
    public void RefreshGizmo()
    {
        if (!_active || ModeOf(_tool) is not { } mode || _selection.Count == 0) { _gizmo.HideGizmo(); return; }
        var views = _selection.Views().ToList(); if (views.Count == 0) { _gizmo.HideGizmo(); return; }
        var frame = _transform.GizmoFrame(views, mode); _gizmo.Show(mode, frame.Position, frame.Axes);
    }

    /// <summary>A press on a handle starts a drag. Returns true when the press was on a handle.</summary>
    public bool BeginDrag(Vector2 screen)
    {
        if (!_gizmo.Shown || ModeOf(_tool) is not { } mode) return false;
        var camera = GetViewport().GetCamera3D(); if (camera is null) return false;
        var origin = camera.ProjectRayOrigin(screen); var direction = camera.ProjectRayNormal(screen);
        var handle = _gizmo.HitTest(origin, direction); if (handle == BuildGizmoHandle.None) return false;
        _dragChanges.Clear();
        if (_locks.SelectionHeldBy() is { } holder) { _window.SetStatus(BuildPersistenceManager.RejectionText("locked:" + holder)); return false; }
        if (!_transform.Begin(mode, handle, _gizmo, _selection.Views().ToList(), origin, direction, -camera.GlobalTransform.Basis.Z)) return false;
        _dragging = true; _gizmo.SetActive(handle); return true;
    }

    public void UpdateDrag(Vector2 screen)
    {
        if (!_dragging) return;
        var camera = GetViewport().GetCamera3D(); if (camera is null) return;
        _transform.Update(camera.ProjectRayOrigin(screen), camera.ProjectRayNormal(screen)); _window.SetStatus(_transform.Readout);
    }

    public void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false; _gizmo.SetActive(BuildGizmoHandle.None);
        var sent = _transform.End();
        if (sent > 0) { _history.Record(_tool == BuildTool.Move ? "mover" : _tool == BuildTool.Rotate ? "girar" : "esticar", _dragChanges.ToList()); _window.SetStatus(sent == 1 ? "Enviado ao servidor." : $"{sent} objetos enviados ao servidor."); }
        _dragChanges.Clear(); RefreshGizmo();
    }

    public void CancelDrag()
    {
        if (!_dragging) return;
        _dragging = false; _transform.Cancel(); _gizmo.SetActive(BuildGizmoHandle.None); _dragChanges.Clear(); RefreshGizmo();
    }

    // ---- input and per-frame -------------------------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent input)
    {
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
        if (input.IsActionPressed("build_mode")) { SetActive(!_active); GetViewport().SetInputAsHandled(); return; }
        if (!_active) return;
        if (input.IsActionPressed("ui_cancel"))
        {
            if (_dragging) CancelDrag(); else if (_packages.CancelPreview("Prévia cancelada.")) { } else if (_faces.Count > 0) _faces.Clear(); else if (_placeType is not null) Disarm("Colocação cancelada."); else if (_selection.Count > 0) _selection.Clear(); else SetActive(false);
            GetViewport().SetInputAsHandled(); return;
        }
        if (input is InputEventKey { Pressed: true, Echo: false } key && HandleKey(key)) { GetViewport().SetInputAsHandled(); return; }
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
        {
            if (click.Pressed && !click.DoubleClick) { if (HandleClick(click.Position, click.ShiftPressed, click.CtrlPressed || click.MetaPressed)) GetViewport().SetInputAsHandled(); }
            else if (!click.Pressed && _dragging) { EndDrag(); GetViewport().SetInputAsHandled(); }
        }
    }

    private bool HandleKey(InputEventKey key)
    {
        var ctrl = key.CtrlPressed || key.MetaPressed;
        switch (key.PhysicalKeycode)
        {
            case Key.Key1 when !ctrl: SetTool(BuildTool.Select); return true;
            case Key.Key2 when !ctrl: SetTool(BuildTool.Create); return true;
            case Key.Key3 when !ctrl: SetTool(BuildTool.Move); return true;
            case Key.Key4 when !ctrl: SetTool(BuildTool.Rotate); return true;
            case Key.Key5 when !ctrl: SetTool(BuildTool.Scale); return true;
            case Key.Key6 when !ctrl: SetTool(BuildTool.Face); return true;
            case Key.G when ctrl && key.ShiftPressed: UnlinkSelection(); return true;
            case Key.G when ctrl: LinkSelection(); return true;
            case Key.D when ctrl: DuplicateSelection(); return true;
            case Key.Z when ctrl && key.ShiftPressed: Redo(); return true;
            case Key.Z when ctrl: Undo(); return true;
            case Key.Y when ctrl: Redo(); return true;
            case Key.Delete: DeleteSelection(); return true;
            case Key.Home: FocusEditCamera(); return true;
            default: return false;
        }
    }

    /// <summary>A left press on the world: places the armed shape, grabs a handle, or picks the object under the cursor (nothing under it clears the selection).</summary>
    public bool HandleClick(Vector2 screen, bool shift, bool ctrl)
    {
        if (_packages.TryConfirm(screen)) return true;
        if (_placeType is not null)
        {
            if (SpotAt(screen) is not { } spot) { _window.SetStatus("Clique no terreno ou em um objeto para colocar."); return true; }
            return PlaceAt(spot, shift);
        }
        if (_tool == BuildTool.Face) return HandleFaceClick(screen, shift);
        if (BeginDrag(screen)) return true;
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return false;
        if (_selection.PickAt(camera, screen, GetWorld3D()) is { } picked) _selection.Pick(picked.State.Id, shift, ctrl);
        else if (!shift) _selection.Clear();
        return false;
    }

    public override void _Process(double delta)
    {
        if (!_active) return;
        if (_dragging) _locks.Stream(_transform.DraggedViews, delta);
        var mouse = GetViewport().GetMousePosition();
        if (_ghost is not null && IsInstanceValid(_ghost))
        {
            var spot = GetViewport().GuiGetHoveredControl() is null ? SpotAt(mouse) : null;
            _ghost.Visible = spot is not null; if (spot is { } where) _ghost.Position = where;
        }
        if (_dragging)
        {
            if (!Input.IsMouseButtonPressed(MouseButton.Left)) EndDrag(); else UpdateDrag(mouse);
        }
        else if (_gizmo.Shown && GetViewport().GetCamera3D() is { } camera && GetViewport().GuiGetHoveredControl() is null) _gizmo.UpdateHover(camera.ProjectRayOrigin(mouse), camera.ProjectRayNormal(mouse));
        _limitsClock += delta; if (_limitsClock >= 6) { _limitsClock = 0; RefreshLimits(); }
    }

    // ---- entry points for the rest of the game (radial menu, inventory, context menu) ---------------------------------

    /// <summary>The "Construir" button and the B key.</summary>
    public void StartBuilding() => SetActive(true);

    /// <summary>The context menu picked an object to edit.</summary>
    public void EditObject(Guid id) => EditObject(id, null);

    /// <summary>The context menu (Mover, Girar, Esticar): opens the editor on that object with that tool.</summary>
    public void EditObject(Guid id, BuildGizmoMode? mode)
    {
        SetActive(true); SetTool(mode switch { BuildGizmoMode.Rotate => BuildTool.Rotate, BuildGizmoMode.Scale => BuildTool.Scale, _ => BuildTool.Move }); _selection.Set(_selection.SetOf(id));
    }

    /// <summary>Commands of the object context menu that act on the selection.</summary>
    public void RunCommand(string command)
    {
        switch (command) { case "duplicate" or "copy": DuplicateSelection(); break; case "delete": DeleteSelection(); break; }
    }
    public void SelectFromContext(Guid id) => EditObject(id);

    /// <summary>"Delete" in the context menu: the server refuses it when the object is not the person's.</summary>
    public void DeleteOwnedObject(Guid id) => _sync.Delete(id);

    public override void _ExitTree() { Disarm(null); }
}
