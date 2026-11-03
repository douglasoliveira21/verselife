using Godot;
using LifeVerse.Shared.Vehicles;

namespace LifeVerse.Client.Scripts;

/// All vehicles of the island the player is on: creates a view when the server announces one, moves them with
/// each snapshot and answers who drives what and which car is close enough to get into.
public partial class VehicleClient : Node
{
    private readonly Dictionary<int, VehicleView> _views = [];

    public int Count => _views.Count;
    public VehicleView? Get(int id) => _views.GetValueOrDefault(id);
    public VehicleView? DrivenBy(int peer) => peer == 0 ? null : _views.Values.FirstOrDefault(v => v.DriverPeer == peer);

    public void Spawn(int id, string name, string type, Vector3 position, float yaw, Node? parent = null)
    {
        parent ??= GetTree().CurrentScene;
        if (_views.ContainsKey(id) || parent is null) return;
        var view = new VehicleView();
        parent.AddChild(view);
        view.Setup(id, name, type);
        view.SnapTo(position, yaw);
        _views[id] = view;
    }

    public void Apply(IReadOnlyList<VehicleSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            if (!_views.TryGetValue(snapshot.Id, out var view)) continue;
            view.TargetPosition = new Vector3(snapshot.X, snapshot.Y, snapshot.Z);
            view.TargetYaw = snapshot.Yaw; view.TargetPitch = snapshot.Pitch; view.Speed = snapshot.Speed; view.DriverPeer = snapshot.DriverPeer;
        }
    }

    /// The closest vehicle within range of a position, or null.
    public VehicleView? Nearest(Vector3 position, float range)
    {
        VehicleView? best = null; var bestDistance = range * range;
        foreach (var view in _views.Values)
        {
            var distance = view.GlobalPosition.DistanceSquaredTo(position);
            if (distance <= bestDistance) { best = view; bestDistance = distance; }
        }
        return best;
    }

    public void Clear()
    {
        foreach (var view in _views.Values) if (IsInstanceValid(view)) view.QueueFree();
        _views.Clear();
    }
}
