using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Camera;

namespace LifeVerse.Client.Scripts.Camera;

/// <summary>What the camera looks at when told to look at something: the thing, the point of it to keep in the middle and how far to stand to see it whole.</summary>
public static class CameraFocus
{
    /// <summary>Tests and the island: where another player's avatar is (by its number).</summary>
    public static Func<int, Node3D?>? RemoteNode { get; set; }

    /// <summary>The box of everything drawn under a node, in the world: its middle and the radius of a sphere around it. Null when nothing is drawn.</summary>
    public static (Vector3 Center, float Radius)? BoundsOf(Node3D node)
    {
        Aabb? total = null;
        void Add(Node n)
        {
            if (n is VisualInstance3D { Visible: true } visual && n is GeometryInstance3D)
            {
                var box = visual.GlobalTransform * visual.GetAabb();
                total = total is { } t ? t.Merge(box) : box;
            }
            foreach (var child in n.GetChildren()) Add(child);
        }
        Add(node);
        return total is { } all ? (all.GetCenter(), Mathf.Max(all.Size.Length() / 2f, .005f)) : null;
    }

    /// <summary>The thing a right click would have found, as something to look at. Null when there is nothing to look at (a menu, a panel).</summary>
    public static (Node3D? Node, Vector3 World, float Radius)? Resolve(ContextTarget target, PlayerController player)
    {
        switch (target)
        {
            case AttachmentTarget worn when player.Rig.AttachmentHolder(worn.Id) is { } holder:
                var held = BoundsOf(holder); return (holder, held?.Center ?? holder.GlobalPosition, held?.Radius ?? .1f);
            case SelfTarget: return (player, player.GlobalPosition + new Vector3(0, .95f, 0), 1.1f);
            case AvatarTarget other:
                var node = (RemoteNode ?? (peer => player.GetNodeOrNull<NetworkManager>("/root/NetworkManager")?.RemoteNode(peer)))(other.Peer);
                return (node, (node?.GlobalPosition ?? other.Position) + new Vector3(0, .95f, 0), 1.1f);
            case ObjectTarget @object:
                var view = BoundsOf(@object.View); return (@object.View, view?.Center ?? @object.View.GlobalPosition, view?.Radius ?? .5f);
            case VehicleTarget car:
                var body = BoundsOf(car.View); return (car.View, body?.Center ?? car.View.GlobalPosition, body?.Radius ?? 2f);
            case SeatTarget seat: return (seat.Seat, seat.Seat.GlobalPosition, .8f);
            case GroundTarget ground: return (null, ground.Point, 2f);
        }
        return null;
    }

    /// <summary>Puts the camera on a target: close enough to see it whole, and on it while it moves.</summary>
    public static bool FocusOn(PlayerController player, ContextTarget target)
    {
        if (Resolve(target, player) is not { } found) return false;
        // while building, the editing camera is the one the person sees: it looks at the thing
        if (player.GetParent() is { } world && LifeVerse.Client.Build.BuildHost.Editor(world) is { OwnsTheMouse: true } editor) { editor.EditCamera.Focus(found.World, found.Radius); return true; }
        var distance = CameraFocusManager.DistanceFor(found.Radius, player.FieldOfView, CameraSettings.Current);
        if (found.Node is { } node && target is not GroundTarget) player.FocusOn(node, node.ToLocal(found.World), distance); else player.FocusOnPoint(found.World, distance);
        return true;
    }
}
