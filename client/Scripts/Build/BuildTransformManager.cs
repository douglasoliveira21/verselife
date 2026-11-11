using System.Globalization;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

public enum BuildSpace { World, Local }

/// <summary>
/// Turns a handle drag into new positions, rotations and sizes for the selected objects. While the mouse is down the objects only move on this computer (a preview);
/// on release the new state of each one goes to the island server, whose answer is what stays. Snapping, the world or object axes and the size limits live here.
/// </summary>
public partial class BuildTransformManager : Node
{
    public const float MinScale = .01f, MaxScale = 64f;

    private sealed record Item(BuildObjectView View, BuildObjectState Start, Vector3 Position, Basis Basis, Vector3 Scale);

    private readonly List<Item> _items = [];
    private BuildGizmoMode _mode; private BuildGizmoHandle _handle;
    private Vector3 _pivot, _axis, _start; private float _startAngle, _startParam; private Basis _activeBasis;
    private Vector3 _camForward;
    private Vector3 _lastDelta; private float _lastAngle; private float _lastFactor = 1;
    private BuildNetworkSynchronizer _sync = null!;

    public BuildSpace Space { get; set; } = BuildSpace.World;
    public bool SnapEnabled { get; set; }
    public float MoveStep { get; set; } = .5f;
    public float RotateStepDegrees { get; set; } = 15f;
    public float ScaleStep { get; set; } = .1f;
    public bool Dragging => _items.Count > 0;
    public IEnumerable<BuildObjectView> DraggedViews => _items.Select(i => i.View);
    public string Readout { get; private set; } = string.Empty;

    public void Initialize(BuildNetworkSynchronizer sync) => _sync = sync;

    /// <summary>Where the gizmo stands for a selection: the centre of the objects (or the object itself), with the axes of the world or of the active object.</summary>
    public (Vector3 Position, Basis Axes) GizmoFrame(IReadOnlyList<BuildObjectView> views, BuildGizmoMode mode)
    {
        var centre = Vector3.Zero; foreach (var view in views) centre += view.Position; centre /= Math.Max(1, views.Count);
        var local = Space == BuildSpace.Local || mode == BuildGizmoMode.Scale; // sizes are always along the object's own sides
        return (centre, local && views.Count > 0 ? views[^1].Basis.Orthonormalized() : Basis.Identity);
    }

    // ---- a drag --------------------------------------------------------------------------------------------------

    public bool Begin(BuildGizmoMode mode, BuildGizmoHandle handle, BuildGizmoController gizmo, IReadOnlyList<BuildObjectView> views, Vector3 rayOrigin, Vector3 rayDirection, Vector3 cameraForward)
    {
        if (views.Count == 0 || handle == BuildGizmoHandle.None) return false;
        _items.Clear(); _mode = mode; _handle = handle; _camForward = cameraForward;
        foreach (var view in views) _items.Add(new Item(view, BuildPersistenceManager.Clone(view.State), view.Position, view.Basis.Orthonormalized(), new Vector3(view.State.ScaleX, view.State.ScaleY, view.State.ScaleZ)));
        var frame = GizmoFrame(views, mode); _pivot = frame.Position; _activeBasis = frame.Axes; _axis = gizmo.AxisOf(handle);
        _lastDelta = Vector3.Zero; _lastAngle = 0; _lastFactor = 1; Readout = string.Empty;
        switch (mode)
        {
            case BuildGizmoMode.Move:
                if (handle == BuildGizmoHandle.Free) _start = BuildGizmoMath.RayPlane(rayOrigin, rayDirection, _pivot, -cameraForward) ?? _pivot;
                else _startParam = BuildGizmoMath.ClosestOnAxis(rayOrigin, rayDirection, _pivot, _axis).T;
                break;
            case BuildGizmoMode.Rotate:
                if (handle == BuildGizmoHandle.View) _axis = -cameraForward;
                _startAngle = BuildGizmoMath.AngleAround(_pivot, _axis, BuildGizmoMath.RayPlane(rayOrigin, rayDirection, _pivot, _axis) ?? _pivot + Vector3.Right);
                break;
            default:
                if (handle == BuildGizmoHandle.Free) _start = BuildGizmoMath.RayPlane(rayOrigin, rayDirection, _pivot, -cameraForward) ?? _pivot + Vector3.Right;
                else _startParam = BuildGizmoMath.ClosestOnAxis(rayOrigin, rayDirection, _pivot, _axis).T;
                break;
        }
        return true;
    }

    public void Update(Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (_items.Count == 0) return;
        switch (_mode)
        {
            case BuildGizmoMode.Move: UpdateMove(rayOrigin, rayDirection); break;
            case BuildGizmoMode.Rotate: UpdateRotate(rayOrigin, rayDirection); break;
            default: UpdateScale(rayOrigin, rayDirection); break;
        }
    }

    private void UpdateMove(Vector3 origin, Vector3 direction)
    {
        Vector3 delta;
        var active = _items[^1];
        if (_handle == BuildGizmoHandle.Free)
        {
            if (BuildGizmoMath.RayPlane(origin, direction, _pivot, -_camForward) is not { } hit) return;
            delta = hit - _start;
            if (SnapEnabled) { var target = active.Position + delta; delta = new Vector3(BuildGizmoMath.Snap(target.X, MoveStep), BuildGizmoMath.Snap(target.Y, MoveStep), BuildGizmoMath.Snap(target.Z, MoveStep)) - active.Position; }
        }
        else
        {
            var t = BuildGizmoMath.ClosestOnAxis(origin, direction, _pivot, _axis).T - _startParam;
            if (SnapEnabled)
            {
                // on a world axis the position itself lands on the grid; on an object axis the distance moved does
                if (Space == BuildSpace.World) { var along = active.Position.Dot(_axis); t = BuildGizmoMath.Snap(along + t, MoveStep) - along; } else t = BuildGizmoMath.Snap(t, MoveStep);
            }
            delta = _axis * t;
        }
        _lastDelta = delta;
        foreach (var item in _items) item.View.Position = item.Position + delta;
        Readout = $"Mover   Δ {Fmt(delta.X)}  {Fmt(delta.Y)}  {Fmt(delta.Z)}   →  {Fmt(active.Position.X + delta.X)}  {Fmt(active.Position.Y + delta.Y)}  {Fmt(active.Position.Z + delta.Z)}";
    }

    private void UpdateRotate(Vector3 origin, Vector3 direction)
    {
        if (BuildGizmoMath.RayPlane(origin, direction, _pivot, _axis) is not { } hit) return;
        var angle = BuildGizmoMath.Delta(_startAngle, BuildGizmoMath.AngleAround(_pivot, _axis, hit));
        if (SnapEnabled) angle = BuildGizmoMath.Snap(angle, Mathf.DegToRad(RotateStepDegrees));
        _lastAngle = angle;
        var turn = new Basis(_axis.Normalized(), angle);
        foreach (var item in _items)
        {
            item.View.Basis = (turn * item.Basis).Scaled(item.Scale);
            if (_items.Count > 1) item.View.Position = _pivot + turn * (item.Position - _pivot); // a group turns around its centre as one piece
        }
        Readout = $"Girar   {Fmt(Mathf.RadToDeg(angle))}°";
    }

    private void UpdateScale(Vector3 origin, Vector3 direction)
    {
        float factor;
        if (_handle == BuildGizmoHandle.Free)
        {
            if (BuildGizmoMath.RayPlane(origin, direction, _pivot, -_camForward) is not { } hit) return;
            var start = Mathf.Max(.05f, _start.DistanceTo(_pivot)); factor = hit.DistanceTo(_pivot) / start;
        }
        else
        {
            var t = BuildGizmoMath.ClosestOnAxis(origin, direction, _pivot, _axis).T; factor = Mathf.Abs(_startParam) < .05f ? 1f : t / _startParam;
        }
        factor = Mathf.Clamp(factor, .02f, 40f); _lastFactor = factor;
        var axisIndex = _handle switch { BuildGizmoHandle.X => 0, BuildGizmoHandle.Y => 1, BuildGizmoHandle.Z => 2, _ => -1 };
        var uniform = axisIndex < 0;
        Vector3 shown = Vector3.One;
        foreach (var item in _items)
        {
            var scale = item.Scale;
            for (var i = 0; i < 3; i++)
            {
                if (!uniform && i != axisIndex) continue;
                var value = scale[i] * factor; if (SnapEnabled) value = BuildGizmoMath.Snap(value, ScaleStep);
                scale[i] = Mathf.Clamp(value, MinScale, MaxScale);
            }
            item.View.Scale = scale; shown = scale;
            if (uniform && _items.Count > 1) item.View.Position = _pivot + (item.Position - _pivot) * factor;
        }
        Readout = $"Esticar   {Fmt(shown.X)}  {Fmt(shown.Y)}  {Fmt(shown.Z)}";
    }

    /// <summary>Lets go: every object that changed is sent to the server. Returns how many were sent.</summary>
    public int End()
    {
        var sent = 0;
        foreach (var item in _items)
        {
            if (!GodotObject.IsInstanceValid(item.View)) continue;
            var after = BuildPersistenceManager.Clone(item.Start); var v = item.View;
            after.PositionX = Round(v.Position.X); after.PositionY = Round(v.Position.Y); after.PositionZ = Round(v.Position.Z);
            if (_mode == BuildGizmoMode.Rotate) { var euler = BuildGizmoMath.EulerDegrees(v.Basis); after.RotationX = Round(euler.X); after.RotationY = Round(euler.Y); after.RotationZ = Round(euler.Z); } // other drags never touch the angles
            if (_mode == BuildGizmoMode.Scale) { after.ScaleX = Round(Mathf.Clamp(v.Scale.X, MinScale, MaxScale)); after.ScaleY = Round(Mathf.Clamp(v.Scale.Y, MinScale, MaxScale)); after.ScaleZ = Round(Mathf.Clamp(v.Scale.Z, MinScale, MaxScale)); }
            if (_mode == BuildGizmoMode.Scale && _items.Count == 1) { after.PositionX = item.Start.PositionX; after.PositionY = item.Start.PositionY; after.PositionZ = item.Start.PositionZ; }
            if (Same(after, item.Start)) { v.Apply(item.Start); continue; }
            Committed?.Invoke(item.Start, after);
            if (_sync.Update(after)) sent++; else v.Apply(item.Start);
        }
        _items.Clear(); return sent;
    }

    /// <summary>Puts every object back where it was before the drag.</summary>
    public void Cancel() { foreach (var item in _items) if (GodotObject.IsInstanceValid(item.View)) item.View.Apply(item.Start); _items.Clear(); }

    /// <summary>One object changed by a drag: its state before and after (the undo history records it).</summary>
    public event Action<BuildObjectState, BuildObjectState>? Committed;

    private static float Round(float value) => MathF.Round(value, 4);
    private static string Fmt(float value) => value.ToString("0.00", CultureInfo.GetCultureInfo("pt-BR"));

    private static bool Same(BuildObjectState a, BuildObjectState b) =>
        MathF.Abs(a.PositionX - b.PositionX) < 1e-4f && MathF.Abs(a.PositionY - b.PositionY) < 1e-4f && MathF.Abs(a.PositionZ - b.PositionZ) < 1e-4f
        && MathF.Abs(a.RotationX - b.RotationX) < 1e-3f && MathF.Abs(a.RotationY - b.RotationY) < 1e-3f && MathF.Abs(a.RotationZ - b.RotationZ) < 1e-3f
        && MathF.Abs(a.ScaleX - b.ScaleX) < 1e-4f && MathF.Abs(a.ScaleY - b.ScaleY) < 1e-4f && MathF.Abs(a.ScaleZ - b.ScaleZ) < 1e-4f;
}
