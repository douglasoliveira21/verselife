using Godot;
using LifeVerse.Shared.Building;
using NVector3 = System.Numerics.Vector3;

namespace LifeVerse.Client.Scripts;

public enum GizmoMode { Move, Rotate, Scale }

/// <summary>What a drag has done so far. Move: metres along the axis. Rotate: radians about it. Scale: the stretch factor.</summary>
public readonly record struct GizmoDrag(GizmoMode Mode, int Axis, Vector3 AxisDirection, float Amount);

/// 3D handles for the selection: arrows to move, rings to rotate and cubes to stretch along X, Y and Z.
/// The handles keep a constant size on screen, draw on top of the scene and live on their own physics layer.
public partial class BuildGizmo : Node3D
{
    public const uint PhysicsLayer = 1u << 19;
    private static readonly Color[] AxisColors = [new("#e14b4b"), new("#5bd05b"), new("#4b8ae1")];
    private static readonly Color Highlight = new("#ffe14a");
    private readonly Dictionary<CollisionObject3D, int> _handleAxis = [];
    private readonly List<StandardMaterial3D>[] _materials = [[], [], []];
    private readonly Node3D[] _roots = new Node3D[3];
    private Basis _axes = Basis.Identity;
    private int _hover = -1;
    private int _active = -1;
    private float _startParameter;
    private Vector3 _startHit;
    private Vector3 _pivotAtStart;
    private float _displayScale = 1f;

    public GizmoMode Mode { get; private set; } = GizmoMode.Move;
    public bool Dragging => _active >= 0;
    public Vector3 Pivot => _pivotAtStart;

    public override void _Ready()
    {
        for (var mode = 0; mode < 3; mode++)
        {
            _roots[mode] = new Node3D { Name = $"{(GizmoMode)mode}Handles", Visible = mode == 0 };
            AddChild(_roots[mode]);
            for (var axis = 0; axis < 3; axis++)
            {
                var handle = (GizmoMode)mode switch { GizmoMode.Move => Arrow(axis, cube: false), GizmoMode.Scale => Arrow(axis, cube: true), _ => Ring(axis) };
                handle.Rotation = axis switch { 0 => new Vector3(0, 0, -Mathf.Pi / 2), 2 => new Vector3(Mathf.Pi / 2, 0, 0), _ => Vector3.Zero };
                _roots[mode].AddChild(handle);
            }
        }
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        var size = Mathf.Max(.25f, camera.GlobalPosition.DistanceTo(GlobalPosition) * .13f);
        _displayScale = size;
        GlobalTransform = new Transform3D(_axes.Orthonormalized().Scaled(Vector3.One * size), GlobalPosition);
        if (_active < 0) SetHighlight(HandleAt(camera.ProjectRayOrigin(GetViewport().GetMousePosition()), camera.ProjectRayNormal(GetViewport().GetMousePosition())));
    }

    /// Shows the handles at a pivot. `axes` orients them (identity for world axes, an object's basis for local ones).
    public void ShowAt(Vector3 pivot, GizmoMode mode, Basis axes)
    {
        Mode = mode; _axes = axes.Orthonormalized(); _pivotAtStart = pivot;
        for (var i = 0; i < 3; i++) _roots[i].Visible = i == (int)mode;
        GlobalPosition = pivot; Visible = true; _active = -1;
    }

    public void HideHandles() { Visible = false; _active = -1; _hover = -1; }

    public Vector3 AxisDirection(int axis) => axis switch { 0 => _axes.X, 1 => _axes.Y, _ => _axes.Z };

    /// The axis (0..2) of the handle under a ray, or -1.
    public int HandleAt(Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (!Visible || !IsInsideTree()) return -1;
        // Colliders from different axes overlap in perspective. Prefer the handle
        // whose drawing is actually closest to the pointer on screen, then use the
        // physics ray as a fallback for unusual camera angles.
        var screenAxis = ScreenHandleAt();
        if (screenAxis >= 0) return screenAxis;
        var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayOrigin + rayDirection * 500f, PhysicsLayer);
        query.CollideWithAreas = true; query.CollideWithBodies = false;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 && hit["collider"].AsGodotObject() is CollisionObject3D body && _handleAxis.TryGetValue(body, out var axis) ? axis : -1;
    }

    private int ScreenHandleAt()
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return -1;
        var mouse = GetViewport().GetMousePosition();
        var bestAxis = -1;
        var bestDistance = 16f;
        for (var axis = 0; axis < 3; axis++)
        {
            var direction = AxisDirection(axis).Normalized();
            var distance = Mode == GizmoMode.Rotate
                ? DistanceToRing(camera, mouse, direction)
                : DistanceToSegment(mouse, camera.UnprojectPosition(GlobalPosition + direction * (_displayScale * .12f)), camera.UnprojectPosition(GlobalPosition + direction * (_displayScale * 1.28f)));
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            bestAxis = axis;
        }
        return bestAxis;
    }

    private float DistanceToRing(Camera3D camera, Vector2 mouse, Vector3 normal)
    {
        var helper = Mathf.Abs(normal.Dot(Vector3.Up)) > .9f ? Vector3.Right : Vector3.Up;
        var u = normal.Cross(helper).Normalized();
        var v = normal.Cross(u).Normalized();
        var best = float.MaxValue;
        var previous = camera.UnprojectPosition(GlobalPosition + u * _displayScale);
        const int samples = 48;
        for (var i = 1; i <= samples; i++)
        {
            var angle = Mathf.Tau * i / samples;
            var current = camera.UnprojectPosition(GlobalPosition + (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * _displayScale);
            best = Mathf.Min(best, DistanceToSegment(mouse, previous, current));
            previous = current;
        }
        return best;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared < .001f) return point.DistanceTo(start);
        var amount = Mathf.Clamp((point - start).Dot(segment) / lengthSquared, 0f, 1f);
        return point.DistanceTo(start + segment * amount);
    }

    public bool BeginDrag(int axis, Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (axis is < 0 or > 2 || !Visible) return false;
        _pivotAtStart = GlobalPosition;
        var direction = AxisDirection(axis);
        if (Mode == GizmoMode.Rotate)
        {
            if (GizmoMath.PlaneHit(ToN(_pivotAtStart), ToN(direction), ToN(rayOrigin), ToN(rayDirection)) is not { } hit) return false;
            _startHit = FromN(hit);
        }
        else _startParameter = GizmoMath.AxisParameter(ToN(_pivotAtStart), ToN(direction), ToN(rayOrigin), ToN(rayDirection));
        _active = axis;
        SetHighlight(axis);
        return true;
    }

    /// Current state of the drag. `moveSnap` is in metres, `rotateSnap` in radians and `scaleSnap` a factor step; 0 means no snapping, the drag follows the mouse smoothly.
    public GizmoDrag? Drag(Vector3 rayOrigin, Vector3 rayDirection, float moveSnap, float rotateSnap, float scaleSnap = 0f)
    {
        if (_active < 0) return null;
        var direction = AxisDirection(_active);
        switch (Mode)
        {
            case GizmoMode.Rotate:
            {
                if (GizmoMath.PlaneHit(ToN(_pivotAtStart), ToN(direction), ToN(rayOrigin), ToN(rayDirection)) is not { } hit) return new GizmoDrag(Mode, _active, direction, 0f);
                var angle = GizmoMath.SignedAngle(ToN(_pivotAtStart), ToN(direction), ToN(_startHit), hit);
                return new GizmoDrag(Mode, _active, direction, GizmoMath.Snap(angle, rotateSnap));
            }
            case GizmoMode.Scale:
            {
                var current = GizmoMath.AxisParameter(ToN(_pivotAtStart), ToN(direction), ToN(rayOrigin), ToN(rayDirection));
                return new GizmoDrag(Mode, _active, direction, GizmoMath.Snap(GizmoMath.ScaleFactor(_startParameter, current), scaleSnap));
            }
            default:
            {
                var current = GizmoMath.AxisParameter(ToN(_pivotAtStart), ToN(direction), ToN(rayOrigin), ToN(rayDirection));
                return new GizmoDrag(Mode, _active, direction, GizmoMath.Snap(current - _startParameter, moveSnap));
            }
        }
    }

    public void EndDrag() { _active = -1; }

    private void SetHighlight(int axis)
    {
        if (axis == _hover && _active < 0) return;
        _hover = axis;
        for (var i = 0; i < 3; i++) foreach (var material in _materials[i]) material.AlbedoColor = (i == axis ? Highlight : AxisColors[i]) with { A = .95f };
    }

    private Node3D Arrow(int axis, bool cube)
    {
        var root = new Node3D { Name = $"Axis{axis}" };
        root.AddChild(Visual(new CylinderMesh { TopRadius = .018f, BottomRadius = .018f, Height = .85f }, new Vector3(0, .425f, 0), axis));
        root.AddChild(cube ? Visual(new BoxMesh { Size = Vector3.One * .15f }, new Vector3(0, .95f, 0), axis) : Visual(new CylinderMesh { TopRadius = 0, BottomRadius = .075f, Height = .24f }, new Vector3(0, .97f, 0), axis));
        root.AddChild(Collider(new BoxShape3D { Size = new Vector3(.32f, 1.28f, .32f) }, new Vector3(0, .62f, 0), axis));
        // the letter of the axis at its tip, in the axis colour, so X, Y and Z are never confused
        root.AddChild(new Label3D { Text = "XYZ"[axis].ToString(), Position = new Vector3(0, 1.3f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FixedSize = false, PixelSize = .0035f, FontSize = 72, OutlineSize = 22, Modulate = Colors.White, OutlineModulate = AxisColors[axis], RenderPriority = 25, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return root;
    }

    private Node3D Ring(int axis)
    {
        var root = new Node3D { Name = $"Ring{axis}" };
        root.AddChild(Visual(new TorusMesh { InnerRadius = .975f, OuterRadius = 1f, Rings = 64, RingSegments = 6 }, Vector3.Zero, axis));
        var area = new Area3D { CollisionLayer = PhysicsLayer, CollisionMask = 0, Monitoring = false };
        area.AddChild(new CollisionShape3D { Shape = new TorusMesh { InnerRadius = .80f, OuterRadius = 1.16f, Rings = 32, RingSegments = 8 }.CreateTrimeshShape() });
        _handleAxis[area] = axis; root.AddChild(area);
        return root;
    }

    private MeshInstance3D Visual(Mesh mesh, Vector3 position, int axis)
    {
        var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = AxisColors[axis] with { A = .95f }, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, NoDepthTest = true, RenderPriority = 20 };
        _materials[axis].Add(material);
        return new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    private Area3D Collider(Shape3D shape, Vector3 position, int axis)
    {
        var area = new Area3D { CollisionLayer = PhysicsLayer, CollisionMask = 0, Monitoring = false, Position = position };
        area.AddChild(new CollisionShape3D { Shape = shape });
        _handleAxis[area] = axis;
        return area;
    }

    private static NVector3 ToN(Vector3 v) => new(v.X, v.Y, v.Z);
    private static Vector3 FromN(NVector3 v) => new(v.X, v.Y, v.Z);
}
