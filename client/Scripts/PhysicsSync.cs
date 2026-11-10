using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// Shows the physical objects of the island where the island server's simulation has put them. The server announces the pose of every moving body a few times a
/// second (the "physics" operation); this node moves the objects toward those poses a little each frame, so a rolling ball looks smooth instead of jumping.
/// An object somebody is editing right now is left alone.
/// </summary>
public partial class PhysicsSync : Node
{
    private const float Follow = 14f;       // how quickly an object catches up with the last pose it was told about
    private const float SnapDistance = 25f; // farther than this the object jumps (it was put somewhere else)

    private NetworkManager? _network;
    private readonly Dictionary<Guid, BuildObjectView> _views = [];
    private readonly Dictionary<Guid, (Vector3 Position, Quaternion Rotation, bool Asleep)> _targets = [];
    private double _lookup;

    public int TrackedForTesting => _targets.Count;

    public override void _Ready()
    {
        _network = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
        if (_network is null) return;
        _network.BuildOperationReceived += OnOperation; _network.ConnectionStateChanged += OnState;
    }

    public override void _ExitTree()
    {
        if (_network is null) return;
        _network.BuildOperationReceived -= OnOperation; _network.ConnectionStateChanged -= OnState;
    }

    private void OnState(string state, string detail) { if (state is "switching" or "offline" or "failed") Clear(); }

    public void Clear() { _targets.Clear(); _views.Clear(); }

    /// <summary>Takes one operation; the ones that are not poses are ignored.</summary>
    public void OnOperation(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.GetProperty("operation").GetString() != "physics" || !document.RootElement.TryGetProperty("bodies", out var bodies) || bodies.ValueKind != JsonValueKind.Array) return;
            foreach (var body in bodies.EnumerateArray())
            {
                if (!Guid.TryParse(body.GetProperty("id").GetString(), out var id)) continue;
                var p = body.GetProperty("p"); var q = body.GetProperty("q");
                if (p.GetArrayLength() != 3 || q.GetArrayLength() != 4) continue;
                var position = new Vector3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle());
                var rotation = new Quaternion(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle());
                if (!float.IsFinite(position.X + position.Y + position.Z) || rotation.LengthSquared() < .5f) continue;
                _targets[id] = (position, rotation.Normalized(), body.TryGetProperty("asleep", out var asleep) && asleep.ValueKind == JsonValueKind.True);
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { }
    }

    private BuildObjectView? ViewOf(Guid id)
    {
        if (_views.TryGetValue(id, out var known) && IsInstanceValid(known)) return known;
        _views.Remove(id);
        if (_lookup > 0) return null; // looked a moment ago: objects that are not here yet arrive with their own messages
        foreach (var node in GetTree().GetNodesInGroup("lifeverse_build_objects")) if (node is BuildObjectView view) _views[view.State.Id] = view;
        _lookup = 1;
        return _views.GetValueOrDefault(id);
    }

    public override void _Process(double delta)
    {
        if (_lookup > 0) _lookup -= delta;
        if (_targets.Count == 0) return;
        var weight = 1f - Mathf.Exp(-Follow * (float)delta);
        foreach (var (id, target) in _targets.ToArray())
        {
            var view = ViewOf(id); if (view is null) continue;
            if (view.Selected) continue;
            if (view.Position.DistanceSquaredTo(target.Position) > SnapDistance * SnapDistance) view.DriveTo(target.Position, target.Rotation);
            else view.DriveTo(view.Position.Lerp(target.Position, weight), view.Quaternion.Slerp(target.Rotation, weight));
            if (target.Asleep && view.Position.DistanceSquaredTo(target.Position) < 1e-6f) _targets.Remove(id);
        }
    }

    /// <summary>For tests: moves by one step as if <paramref name="seconds"/> had passed.</summary>
    public void StepForTesting(double seconds) => _Process(seconds);
}
