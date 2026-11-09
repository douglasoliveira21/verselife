using System.Text.Json;
using Godot;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// Placing a new shape: the person picks it in the list, presses the cube button and then clicks where it should stand. Until the click
/// a see-through copy of the shape follows the mouse over the ground and over other objects, so the spot is seen before it is chosen.
/// Shift + click keeps placing more of the same shape; Esc, another tool or the cube button again stops.
public partial class BuildEditor
{
    private const string GhostLook = "{\"color\":\"#7FE8FF\",\"transparency\":0.4,\"emission\":\"#12F2E0\",\"emissionStrength\":0.7}";
    private BuildObjectView? _placeGhost;
    private string? _placeType;

    /// Entry points for the radial menu: edit a given object, run a command on the selection, open the build tools or start placing a shape.
    public void EditObject(Guid id, GizmoMode? mode = null) { SetActive(true); SelectIds([id]); if (mode is { } wanted) SetMode(wanted); }
    public void RunCommand(string name) => CommandForTesting(name);
    public void StartPlacing() { SetActive(true); if (_placeType is null) CreateObject(); }
    public bool IsPlacingForTesting => _placeType is not null || _previewRoot is not null;
    public bool GhostVisibleForTesting => _placeGhost is { Visible: true };

    /// The cube button: starts (or stops) choosing a spot for the shape selected in the Geral tab.
    private void CreateObject()
    {
        if (!_active || !_network.IsAuthenticated && OperationSink is null) { _status.Text = "Entre em uma ilha autenticada antes de construir."; return; }
        if (_placeType is not null) { DisarmPlacement("Colocação cancelada."); return; }
        _placeType = _primitive.GetItemMetadata(_primitive.Selected).AsString();
        var state = new BuildObjectState { Id = Guid.NewGuid(), PrimitiveType = _placeType, Name = string.Empty, MaterialJson = GhostLook, PhysicsJson = "{\"collision\":false}" };
        _placeGhost = new BuildObjectView(); GetParent().AddChild(_placeGhost); _placeGhost.Apply(state); _placeGhost.Visible = false;
        _status.Text = $"Clique no terreno ou em um objeto para colocar: {PrimitiveLabel(_placeType)}. Shift + clique coloca vários; Esc cancela.";
    }

    private void DisarmPlacement(string? message)
    {
        if (_previewRoot is not null) { ClearPreview(); if (_placeType is null) { if (message is not null) _status.Text = message; return; } }
        if (_placeType is null) return;
        _placeType = null;
        if (_placeGhost is not null && IsInstanceValid(_placeGhost)) _placeGhost.QueueFree();
        _placeGhost = null;
        if (message is not null) _status.Text = message;
    }

    /// The point a screen position looks at, lifted half a metre along the surface so a one-metre shape sits on it; null when it looks at nothing.
    private Vector3? SpotAt(Vector2 screen)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return null;
        var origin = camera.ProjectRayOrigin(screen); var direction = camera.ProjectRayNormal(screen);
        // The placement ghost has a selection-only body. Exclude it from the surface
        // query, otherwise the preview can repeatedly raycast itself and flicker.
        var mask = uint.MaxValue & ~BuildGizmo.PhysicsLayer & ~BuildObjectView.SelectionPhysicsLayer;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(origin, origin + direction * 150f, mask));
        if (hit.Count == 0) return null;
        var spot = hit["position"].AsVector3() + hit["normal"].AsVector3() * .5f;
        if (_gridSnap.ButtonPressed && GridStep() > 0f) spot = new Vector3(AlignMath.SnapToGrid(spot.X, GridStep()), spot.Y, AlignMath.SnapToGrid(spot.Z, GridStep()));
        return spot;
    }

    /// Keeps the see-through copy under the mouse; it hides while the mouse is over a window or looks at nothing.
    private void UpdateGhost()
    {
        if (_placeType is null || _placeGhost is null || !IsInstanceValid(_placeGhost)) return;
        var spot = GetViewport().GuiGetHoveredControl() is null ? SpotAt(GetViewport().GetMousePosition()) : null;
        _placeGhost.Visible = spot is not null;
        if (spot is { } where) _placeGhost.Position = where;
    }

    /// A click while placing: puts the shape there. Returns true when the click was used.
    private bool TryPlaceClick(Vector2 screen, bool keepPlacing)
    {
        if (_placeType is null) return false;
        if (SpotAt(screen) is not { } spot) { _status.Text = "Clique no terreno ou em um objeto para colocar."; return true; }
        var type = _placeType;
        CreateObjectAt(spot, type);
        if (!keepPlacing) DisarmPlacement(null);
        return true;
    }

    /// Creates the shape at a spot: a plain object in the current colour, with the usual revision and permission checks on the server.
    public void CreateObjectAt(Vector3 position, string type)
    {
        if (!_active || !_network.IsAuthenticated && OperationSink is null) { _status.Text = "Entre em uma ilha autenticada antes de construir."; return; }
        var template = new BuildObjectState
        {
            PrimitiveType = type, Name = PrimitiveLabel(type),
            MaterialJson = MaterialFromFields("{}"), GeometryJson = "{}", PhysicsJson = "{\"collision\":true}"
        };
        var entry = new HistoryEntry("create", []);
        CreateFrom(template, position, null, created => { var item = new HistoryItem(created.Id, null, Clone(created)); entry.Items.Add(item); SelectIds([created.Id]); Teach(TutorialStep.Create); });
        PushHistory(entry);
        _status.Text = _placeType is null ? $"{PrimitiveLabel(type)} criada; use Mover (2), Girar (3) ou Esticar (4)." : $"{PrimitiveLabel(type)} colocada; clique de novo para colocar outra, Esc para parar.";
    }

    /// Test hook: clicks the given spot while placement is on.
    public void PlaceAtForTesting(Vector3 position, bool keep = false)
    {
        if (_placeType is null) return;
        var type = _placeType;
        CreateObjectAt(position, type);
        if (!keep) DisarmPlacement(null);
    }
}
