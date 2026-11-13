using System.Text.Json;
using LifeVerse.Scripting;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>Particles and lights that scripts put on their objects, and sounds at chosen points: announced to everybody on the island (and to whoever arrives later), moved with the object, removed with the script.</summary>
public sealed partial class IslandScripting
{
    private readonly Dictionary<string, (Guid Object, EffectSpec Effect)> _activeEffects = [];

    private static string EffectKey(ScriptDefinition script, EffectSpec effect) => $"{script.Id:N}:{effect.Id}";

    public void SetEffect(ScriptDefinition script, EffectSpec effect)
    {
        var key = EffectKey(script, effect);
        if (effect.On) _activeEffects[key] = (script.ObjectId, effect); else _activeEffects.Remove(key);
        if (_objects.TryGetValue(script.ObjectId, out var target) || !effect.On) _broadcastApplied(EffectOp(key, script.ObjectId, effect, _objects.GetValueOrDefault(script.ObjectId)));
    }

    private static string EffectOp(string key, Guid objectId, EffectSpec e, CachedObject? at) =>
        JsonSerializer.Serialize(new { operation = "effect", key, objectId, on = e.On, kind = e.Kind, preset = e.Preset, rate = e.Rate, lifetime = e.Lifetime, size = e.Size, speed = e.Speed, color = e.Color, radius = e.Radius, energy = e.Energy, flicker = e.Flicker, x = at?.Position.X ?? 0, y = (at?.Position.Y ?? 0) + (at?.Scale.Y ?? 0) / 2, z = at?.Position.Z ?? 0 });

    /// <summary>The effects that are on now, as messages for somebody who has just arrived.</summary>
    public IEnumerable<string> EffectOps() => _activeEffects.Where(p => _objects.ContainsKey(p.Value.Object)).Select(p => EffectOp(p.Key, p.Value.Object, p.Value.Effect, _objects[p.Value.Object]));

    /// <summary>An object with effects moved or changed: they follow it.</summary>
    private void FollowEffects(Guid objectId)
    {
        if (!_objects.TryGetValue(objectId, out var at)) return;
        foreach (var (key, active) in _activeEffects.Where(p => p.Value.Object == objectId).ToList()) _broadcastApplied(EffectOp(key, objectId, active.Effect, at));
    }

    /// <summary>Plays a sound the object carries at a point of the island (everybody near hears it, fading with distance).</summary>
    public bool PlaySoundAt(ScriptDefinition script, string soundName, float volume, bool loop, Vec3 position)
    {
        if (!_objects.TryGetValue(script.ObjectId, out var target) || SoundOf(target, soundName) is not { } asset) return false;
        _broadcastApplied(JsonSerializer.Serialize(new { operation = "sound", objectId = Guid.NewGuid(), assetId = asset, x = position.X, y = position.Y, z = position.Z, volume, loop }));
        return true;
    }
}

public sealed partial class IslandScripting
{
    private readonly Dictionary<Guid, Queue<double>> _contactTimes = [];

    /// <summary>A simulated body of an object touched or left something. The scripts of that object hear on_collision_enter / on_collision (and on_land_collision for the ground) or on_collision_exit, at most ten a second per object.</summary>
    public void OnContact(Guid objectId, Guid? other, bool entered, float speed)
    {
        if (entered && speed < .3f) return;                                   // a body resting on the ground is not a hit
        var now = Now; var queue = _contactTimes.TryGetValue(objectId, out var q) ? q : _contactTimes[objectId] = new Queue<double>();
        while (queue.Count > 0 && now - queue.Peek() > 1) queue.Dequeue(); if (queue.Count >= 10) return; queue.Enqueue(now);
        var entity = new Dictionary<string, object?> { ["type"] = other is null ? "ground" : "object", ["id"] = other?.ToString(), ["name"] = other is { } id && _objects.TryGetValue(id, out var o) ? o.Name : other is null ? "Terreno" : "" };
        var data = new Dictionary<string, object?> { ["entity"] = entity, ["speed"] = speed };
        if (!entered) { _host.Enqueue(new ScriptEvent(objectId, "collision_exit", data)); return; }
        _host.Enqueue(new ScriptEvent(objectId, "collision_enter", data)); if (other is null) _host.Enqueue(new ScriptEvent(objectId, "land_collision", data));
    }
}
