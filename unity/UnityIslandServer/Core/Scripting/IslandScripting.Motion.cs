using LifeVerse.Scripting;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>What scripts hear about simulated bodies: on_physics_update (four times a second while the body moves) and on_velocity_changed (when its velocity changed by at least half a metre per second since the last time it was told).</summary>
public sealed partial class IslandScripting
{
    public const float VelocityChangeThreshold = .5f, RestingSpeed = .05f;
    public const int MaxWatchedBodies = 200;
    private readonly Dictionary<Guid, Vec3> _toldVelocity = [];
    private readonly HashSet<Guid> _moving = [];

    private void WatchMotion()
    {
        if (VelocityOf is null) return;
        var wanting = _host.ObjectsWanting("physics_update").Concat(_host.ObjectsWanting("velocity_changed")).Distinct().Take(MaxWatchedBodies).ToList();
        foreach (var id in _toldVelocity.Keys.Concat(_moving).Where(k => !wanting.Contains(k)).ToList()) { _toldVelocity.Remove(id); _moving.Remove(id); }
        foreach (var id in wanting)
        {
            if (!_objects.TryGetValue(id, out var at) || VelocityOf(id) is not { } v) { _toldVelocity.Remove(id); _moving.Remove(id); continue; }
            var speed = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            var was = _toldVelocity.TryGetValue(id, out var old) ? old : default;
            var data = new Dictionary<string, object?> { ["velocity"] = new Dictionary<string, object?> { ["x"] = v.X, ["y"] = v.Y, ["z"] = v.Z }, ["speed"] = speed, ["position"] = new Dictionary<string, object?> { ["x"] = at.Position.X, ["y"] = at.Position.Y, ["z"] = at.Position.Z } };
            var moving = speed > RestingSpeed; if (moving || _moving.Contains(id)) _host.Enqueue(new ScriptEvent(id, "physics_update", data)); if (moving) _moving.Add(id); else _moving.Remove(id);      // the last update tells that it stopped
            var change = MathF.Sqrt((v.X - was.X) * (v.X - was.X) + (v.Y - was.Y) * (v.Y - was.Y) + (v.Z - was.Z) * (v.Z - was.Z));
            if (change >= VelocityChangeThreshold) { _toldVelocity[id] = moving ? v : default; _host.Enqueue(new ScriptEvent(id, "velocity_changed", data)); }
        }
    }
}
