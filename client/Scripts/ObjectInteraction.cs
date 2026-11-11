using LifeVerse.Client.Build;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// Clicking an object in the world "touches" it: the island server hands the touch to the object's Lua script,
/// if it has one. Hovering shows the object's name. Disabled while a build or terrain tool owns the mouse.
public partial class ObjectInteraction : Node3D
{
    private NetworkManager _network = null!;

    private TerrainEditor? _terrain;

    public override void _Ready()
    {
        _network = GetNode<NetworkManager>("/root/NetworkManager");

        _terrain = GetParent().GetNodeOrNull<TerrainEditor>("TerrainEditor");
    }

    private bool ToolsActive => BuildHost.IsActive(GetParent()) || (_terrain?.Active ?? false);

    public override void _UnhandledInput(InputEvent input)
    {
        if (ToolsActive || input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: false }) return;
        var actions = GetParent().GetNodeOrNull<BuildClickActions>("BuildClickActions");
        var exclude = new Godot.Collections.Array<Rid>();
        // an object that ignores clicks lets them through to whatever is behind it
        for (var pass = 0; pass < 4; pass++)
        {
            if (ViewUnderMouse(exclude) is not { } view) return;
            if (BuildClickActions.Ignores(view)) { foreach (var body in view.FindChildren("*", "CollisionObject3D", true, false).OfType<CollisionObject3D>()) exclude.Add(body.GetRid()); continue; }
            if (actions is not null) actions.Perform(view); else _network.TouchObject(view.State.Id);
            return;
        }
    }

    private BuildObjectView? ViewUnderMouse(Godot.Collections.Array<Rid>? exclude = null)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return null;
        var mouse = GetViewport().GetMousePosition();
        var origin = camera.ProjectRayOrigin(mouse);
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + camera.ProjectRayNormal(mouse) * 60f, uint.MaxValue);
        if (exclude is not null) query.Exclude = exclude;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (!hit.TryGetValue("collider", out var collider)) return null;
        var node = collider.AsGodotObject() as Node;
        while (node is not null and not BuildObjectView) node = node.GetParent();
        return node as BuildObjectView;
    }

}
