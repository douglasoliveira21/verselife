using Godot;

namespace LifeVerse.Client.Build;

public enum BuildGizmoMode { Move, Rotate, Scale }
public enum BuildGizmoHandle { None, X, Y, Z, Free, View }

/// <summary>
/// The 3D handles of the editor: arrows to move, rings to turn, boxes to stretch. X is red, Y green and Z blue. They are drawn over the world at a constant size on the
/// screen, found with a ray (no physics bodies, so they can never block or be blocked by anything) and light up when the cursor is on one.
/// </summary>
public partial class BuildGizmoController : Node3D
{
    public const float Reach = 1.6f;          // length of an axis, in gizmo units
    public const float RingRadius = 1.15f;
    public const float ViewRingRadius = 1.4f;
    private const float Grab = .14f;          // how close the ray must pass, in gizmo units
    private const float ScreenSize = .17f;    // gizmo units per metre of distance to the camera

    private static readonly Color[] AxisColors = [new("#E5484D"), new("#3FB950"), new("#4C8DFF")];
    private static readonly Color Lit = new("#FFD84A");

    private BuildGizmoMode _mode = BuildGizmoMode.Move;
    private Basis _axes = Basis.Identity;
    private BuildGizmoHandle _hover = BuildGizmoHandle.None, _active = BuildGizmoHandle.None;
    private readonly Dictionary<BuildGizmoHandle, List<MeshInstance3D>> _parts = [];

    public BuildGizmoMode Mode => _mode;
    public float UnitScale { get; private set; } = 1;
    public bool Shown => Visible;
    public BuildGizmoHandle Hover => _hover;

    public override void _Ready() { Visible = false; Rebuild(); }

    /// <summary>The direction of a handle's axis in the world (the gizmo's own axes: the world's, or the object's when the space is local).</summary>
    public Vector3 AxisOf(BuildGizmoHandle handle) => handle switch { BuildGizmoHandle.X => _axes.X.Normalized(), BuildGizmoHandle.Y => _axes.Y.Normalized(), BuildGizmoHandle.Z => _axes.Z.Normalized(), _ => Vector3.Zero };

    public void Show(BuildGizmoMode mode, Vector3 position, Basis axes)
    {
        var changed = mode != _mode; _mode = mode; _axes = axes.Orthonormalized(); GlobalPosition = position; Visible = true;
        GlobalTransform = new Transform3D(_axes.Scaled(Vector3.One * UnitScale), position);
        if (changed) Rebuild();
    }

    public void HideGizmo() { Visible = false; _hover = _active = BuildGizmoHandle.None; }

    public void SetActive(BuildGizmoHandle handle) { _active = handle; Paint(); }

    private void Rebuild()
    {
        foreach (var child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        _parts.Clear();
        var axes = new[] { BuildGizmoHandle.X, BuildGizmoHandle.Y, BuildGizmoHandle.Z };
        for (var i = 0; i < 3; i++)
        {
            var handle = axes[i]; var direction = i == 0 ? Vector3.Right : i == 1 ? Vector3.Up : Vector3.Back; var parts = new List<MeshInstance3D>(); _parts[handle] = parts;
            switch (_mode)
            {
                case BuildGizmoMode.Move:
                    parts.Add(Part(new CylinderMesh { TopRadius = .025f, BottomRadius = .025f, Height = Reach - .3f, RadialSegments = 8 }, direction * (Reach - .3f) / 2f, direction));
                    parts.Add(Part(new CylinderMesh { TopRadius = 0, BottomRadius = .09f, Height = .3f, RadialSegments = 12 }, direction * (Reach - .15f), direction));
                    break;
                case BuildGizmoMode.Scale:
                    parts.Add(Part(new CylinderMesh { TopRadius = .025f, BottomRadius = .025f, Height = Reach - .12f, RadialSegments = 8 }, direction * (Reach - .12f) / 2f, direction));
                    parts.Add(Part(new BoxMesh { Size = new Vector3(.2f, .2f, .2f) }, direction * Reach, direction, boxLike: true));
                    break;
                default:
                    parts.Add(Part(new TorusMesh { InnerRadius = RingRadius - .025f, OuterRadius = RingRadius + .025f, Rings = 48, RingSegments = 6 }, Vector3.Zero, direction));
                    break;
            }
        }
        var free = new List<MeshInstance3D> { Part(_mode == BuildGizmoMode.Rotate ? new TorusMesh { InnerRadius = ViewRingRadius - .02f, OuterRadius = ViewRingRadius + .02f, Rings = 48, RingSegments = 6 } : new SphereMesh { Radius = .13f, Height = .26f, RadialSegments = 12, Rings = 6 }, Vector3.Zero, Vector3.Up, white: true, billboard: _mode == BuildGizmoMode.Rotate) };
        _parts[_mode == BuildGizmoMode.Rotate ? BuildGizmoHandle.View : BuildGizmoHandle.Free] = free;
        Paint();
    }

    private MeshInstance3D Part(Mesh mesh, Vector3 at, Vector3 axis, bool boxLike = false, bool white = false, bool billboard = false)
    {
        var instance = new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = at, TopLevel = false };
        // a mesh is built along Y (cylinders, cones) or around Y (torus): turn it onto the axis it belongs to
        if (!boxLike && !axis.IsEqualApprox(Vector3.Up) && !white) instance.Basis = new Basis(new Quaternion(Vector3.Up, axis));
        if (white && !billboard) instance.Basis = Basis.Identity;
        instance.MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true, RenderPriority = 100, AlbedoColor = white ? Colors.White : Colors.Gray };
        AddChild(instance); return instance;
    }

    private void Paint()
    {
        foreach (var (handle, parts) in _parts)
        {
            var colour = handle is BuildGizmoHandle.Free or BuildGizmoHandle.View ? Colors.White : AxisColors[(int)handle - 1];
            if (handle == _hover || handle == _active) colour = Lit;
            foreach (var part in parts) if (part.MaterialOverride is StandardMaterial3D material) material.AlbedoColor = colour;
        }
    }

    // ---- per frame ---------------------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (!Visible) return;
        var camera = GetViewport().GetCamera3D(); if (camera is null) return;
        UnitScale = Mathf.Max(.3f, camera.GlobalPosition.DistanceTo(GlobalPosition) * ScreenSize);
        var position = GlobalPosition; Transform = new Transform3D(_axes.Scaled(Vector3.One * UnitScale), position);
        if (_parts.TryGetValue(BuildGizmoHandle.View, out var ring))
            foreach (var part in ring) part.GlobalTransform = new Transform3D(camera.GlobalTransform.Basis * new Basis(Vector3.Right, Mathf.Pi / 2f) * Basis.FromScale(Vector3.One * UnitScale), position);
    }

    /// <summary>The handle under the cursor (a ray from the camera), or none.</summary>
    public BuildGizmoHandle HitTest(Vector3 origin, Vector3 direction)
    {
        if (!Visible) return BuildGizmoHandle.None;
        var camera = GetViewport().GetCamera3D();
        var centre = GlobalPosition; var s = UnitScale; var best = BuildGizmoHandle.None; var bestDistance = float.MaxValue;
        void Offer(BuildGizmoHandle handle, float distance) { if (distance < bestDistance) { best = handle; bestDistance = distance; } }
        if (_mode == BuildGizmoMode.Rotate)
        {
            foreach (var handle in new[] { BuildGizmoHandle.X, BuildGizmoHandle.Y, BuildGizmoHandle.Z })
            {
                var axis = AxisOf(handle);
                if (BuildGizmoMath.RayPlane(origin, direction, centre, axis) is { } hit) { var off = Mathf.Abs(hit.DistanceTo(centre) - RingRadius * s); if (off < Grab * s) Offer(handle, off + (handle == BuildGizmoHandle.X ? 0 : .001f)); }
            }
            if (camera is not null && BuildGizmoMath.RayPlane(origin, direction, centre, -camera.GlobalTransform.Basis.Z) is { } viewHit) { var off = Mathf.Abs(viewHit.DistanceTo(centre) - ViewRingRadius * s); if (off < Grab * s) Offer(BuildGizmoHandle.View, off + .05f * s); }
        }
        else
        {
            foreach (var handle in new[] { BuildGizmoHandle.X, BuildGizmoHandle.Y, BuildGizmoHandle.Z })
            {
                var axis = AxisOf(handle); var (t, distance) = BuildGizmoMath.ClosestOnAxis(origin, direction, centre, axis);
                if (t > .1f * s && t < (Reach + .2f) * s && distance < Grab * s) Offer(handle, distance);
            }
            var toCentre = centre - origin; var along = toCentre.Dot(direction);
            if (along > 0 && (origin + direction * along).DistanceTo(centre) < .16f * s) Offer(BuildGizmoHandle.Free, 0f); // the centre wins over an axis it overlaps
        }
        return best;
    }

    public BuildGizmoHandle UpdateHover(Vector3 origin, Vector3 direction)
    {
        var handle = HitTest(origin, direction);
        if (handle != _hover) { _hover = handle; Paint(); }
        return handle;
    }
}
