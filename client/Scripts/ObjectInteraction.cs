using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// Clicking an object in the world "touches" it: the island server hands the touch to the object's Lua script,
/// if it has one. Hovering shows the object's name. Disabled while a build or terrain tool owns the mouse.
public partial class ObjectInteraction : Node3D
{
    private NetworkManager _network = null!;
    private BuildEditor? _build;
    private TerrainEditor? _terrain;
    private BuildObjectView? _hovered;

    public override void _Ready()
    {
        _network = GetNode<NetworkManager>("/root/NetworkManager");
        _build = GetParent().GetNodeOrNull<BuildEditor>("BuildEditor");
        _terrain = GetParent().GetNodeOrNull<TerrainEditor>("TerrainEditor");
    }

    private bool ToolsActive => (_build?.Active ?? false) || (_terrain?.Active ?? false);

    public override void _Process(double delta)
    {
        var target = ToolsActive || GetViewport().GuiGetHoveredControl() is not null ? null : ViewUnderMouse();
        if (target == _hovered) return;
        SetNameVisible(_hovered, false);
        _hovered = target;
        SetNameVisible(_hovered, true);
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (ToolsActive || input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: false }) return;
        if (ViewUnderMouse() is { } view) _network.TouchObject(view.State.Id);
    }

    private BuildObjectView? ViewUnderMouse()
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return null;
        var mouse = GetViewport().GetMousePosition();
        var origin = camera.ProjectRayOrigin(mouse);
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + camera.ProjectRayNormal(mouse) * 60f, uint.MaxValue & ~BuildGizmo.PhysicsLayer);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (!hit.TryGetValue("collider", out var collider)) return null;
        var node = collider.AsGodotObject() as Node;
        while (node is not null and not BuildObjectView) node = node.GetParent();
        return node as BuildObjectView;
    }

    private static void SetNameVisible(BuildObjectView? view, bool visible)
    {
        if (view is not null && IsInstanceValid(view) && view.GetNodeOrNull<Label3D>("ObjectName") is { } label) label.Visible = visible;
    }
}
