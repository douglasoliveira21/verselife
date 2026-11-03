using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using LifeVerse.Scripting;
using LifeVerse.Shared.Networking;

namespace LifeVerse.IslandServer.Scripts;

public sealed record AvatarPresence(int PeerId, Guid AvatarId, Guid UserId, string Name, float X, float Y, float Z);

/// Connects the Lua script host to this island: loads its objects and scripts from the API, turns avatar actions
/// (touch, walking into an object, chatting) into script events, and applies what scripts ask for through the same
/// authoritative build-operation path players use, so permissions, revisions and replication all still apply.
public sealed class IslandScripting : IScriptWorld
{
    private sealed class CachedObject
    {
        public Guid Id, OwnerUserId; public Guid? GroupId, ParentObjectId; public string Name = "", PrimitiveType = "cube", MaterialJson = "{}", GeometryJson = "{}", PhysicsJson = "{}";
        public Vec3 Position, Rotation, Scale = new(1, 1, 1); public int Revision;
    }

    private readonly ScriptHost _host;
    private readonly System.Net.Http.HttpClient _api;
    private readonly ServerConfiguration _config;
    private readonly Action<string> _broadcastApplied;
    private readonly Action<string, Vec3, string> _say;
    private readonly Action<Guid, string> _tellUser;
    private readonly Action<string, object> _log;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<Guid, CachedObject> _objects = [];

    /// What stops walking avatars, kept in step with the objects (the movement simulation uses it so the server and the players agree on walls and floors).
    public ObstacleField Obstacles { get; } = new();

    private void UpdateObstacle(CachedObject o) =>
        Obstacles.Set(o.Id, ObstacleField.Blocks(o.PhysicsJson) ? ObstacleField.For(o.PrimitiveType, new System.Numerics.Vector3(o.Position.X, o.Position.Y, o.Position.Z), new System.Numerics.Vector3(o.Rotation.X, o.Rotation.Y, o.Rotation.Z), new System.Numerics.Vector3(o.Scale.X, o.Scale.Y, o.Scale.Z)) : null);
    private readonly Dictionary<(Guid Object, Guid Avatar), bool> _inside = [];
    private readonly Dictionary<Guid, Queue<double>> _touches = [];
    private readonly Dictionary<Guid, Queue<double>> _changeTimes = [];
    private readonly Queue<(ScriptDefinition Script, ObjectChange Change)> _changes = new();
    private readonly HashSet<Guid> _inFlight = [];
    private readonly Dictionary<Guid, Dictionary<string, string>> _unsentState = [];
    private double _lastProximity;
    private bool _refreshing;

    public IslandScripting(System.Net.Http.HttpClient api, ServerConfiguration config, Action<string> broadcastApplied, Action<string, Vec3, string> say, Action<Guid, string> tellUser, Action<string, object> log)
    {
        _api = api; _config = config; _broadcastApplied = broadcastApplied; _say = say; _tellUser = tellUser; _log = log;
        _host = new ScriptHost(this);
        _host.Report += report => _tellUser(report.Script.OwnerUserId, $"[{report.Script.Name}] {report.Message}");
        _host.Log += text => _log("script_log", new { text });
    }

    public double Now => _clock.Elapsed.TotalSeconds;
    public IReadOnlyList<ScriptInfo> Scripts => _host.Scripts;

    public ScriptObjectState? GetObject(Guid id) => _objects.TryGetValue(id, out var o) ? new ScriptObjectState(o.Id, o.OwnerUserId, o.Name, o.Position, o.Rotation, o.Scale) : null;

    // ---- loading and persistence -----------------------------------------------------------------------------

    public async Task RefreshAsync(CancellationToken ct)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            using var response = await _api.GetAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/island-content", ct);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = document.RootElement;
            var fresh = new Dictionary<Guid, CachedObject>();
            foreach (var element in root.GetProperty("objects").EnumerateArray()) { var cached = Parse(element); fresh[cached.Id] = cached; }
            // Objects with a change in flight keep their newer local view until the server answers.
            foreach (var id in _inFlight) if (_objects.TryGetValue(id, out var local)) fresh[id] = local;
            _objects.Clear(); Obstacles.Clear(); foreach (var (id, cached) in fresh) { _objects[id] = cached; UpdateObstacle(cached); }
            var definitions = root.GetProperty("scripts").EnumerateArray().Select(s => new ScriptDefinition(s.GetProperty("id").GetGuid(), s.GetProperty("objectId").GetGuid(), s.GetProperty("ownerUserId").GetGuid(), s.GetProperty("name").GetString() ?? "script", s.GetProperty("source").GetString() ?? "", s.GetProperty("version").GetInt32(), s.GetProperty("instructionBudget").GetInt32())).ToList();
            var state = new Dictionary<Guid, Dictionary<string, string>>();
            if (root.TryGetProperty("state", out var states))
                foreach (var scriptState in states.EnumerateObject()) state[Guid.Parse(scriptState.Name)] = scriptState.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "null");
            _host.Sync(definitions, id => state.GetValueOrDefault(id));
            _log("scripts_loaded", new { objects = _objects.Count, scripts = definitions.Count });
        }
        finally { _refreshing = false; }
    }

    public async Task FlushStateAsync(CancellationToken ct)
    {
        foreach (var (id, values) in _host.TakeDirtyState()) _unsentState[id] = values;
        if (_unsentState.Count == 0) return;
        var batch = new { states = _unsentState.Select(pair => new { scriptId = pair.Key, values = pair.Value }).ToArray() };
        using var response = await _api.PutAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/script-state", batch, ct);
        if (response.IsSuccessStatusCode) _unsentState.Clear(); else _log("script_state_save_failed", new { status = (int)response.StatusCode });
    }

    /// Keeps the object cache in step with build operations the server accepted (players' or scripts').
    public void OnObjectApplied(string acceptedJson)
    {
        try
        {
            using var document = JsonDocument.Parse(acceptedJson);
            var root = document.RootElement;
            if (root.TryGetProperty("objectState", out var state) && state.ValueKind == JsonValueKind.Object) { var cached = Parse(state); _objects[cached.Id] = cached; UpdateObstacle(cached); }
            else if (root.TryGetProperty("objectId", out var id) && Guid.TryParse(id.GetString(), out var removed)) { _objects.Remove(removed); Obstacles.Remove(removed); }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
    }

    // ---- events from the world -------------------------------------------------------------------------------

    public void Touch(AvatarPresence who, Guid objectId)
    {
        if (!_objects.TryGetValue(objectId, out var target) || !_host.Wants(objectId, "touch")) return;
        var now = Now;
        var times = _touches.TryGetValue(who.AvatarId, out var queue) ? queue : _touches[who.AvatarId] = new Queue<double>();
        while (times.Count > 0 && now - times.Peek() > 1) times.Dequeue();
        if (times.Count >= 4) return;
        var distance = Distance(who, target);
        if (distance > 12 + Radius(target)) return;
        times.Enqueue(now);
        _host.Enqueue(new ScriptEvent(objectId, "touch", Describe(who, distance)));
    }

    public void OnChat(AvatarPresence speaker, string message)
    {
        foreach (var objectId in _host.ObjectsWanting("chat").ToList())
            if (_objects.TryGetValue(objectId, out var target) && Distance(speaker, target) <= 20)
                _host.Enqueue(new ScriptEvent(objectId, "chat", new Dictionary<string, object?> { ["avatar_id"] = speaker.AvatarId, ["avatar_name"] = speaker.Name, ["message"] = message }));
    }

    /// Called every server tick: timers, walking into or out of objects, queued events and the changes scripts made.
    public void Update(IReadOnlyList<AvatarPresence> avatars)
    {
        var now = Now;
        _host.Tick(now);
        if (now - _lastProximity >= .25) { _lastProximity = now; Proximity(avatars); }
        _host.Process(6);
        DrainChanges();
    }

    private void Proximity(IReadOnlyList<AvatarPresence> avatars)
    {
        var watched = _host.ObjectsWanting("enter").Concat(_host.ObjectsWanting("leave")).Distinct().ToList();
        foreach (var objectId in watched)
        {
            if (!_objects.TryGetValue(objectId, out var target)) continue;
            var radius = Radius(target);
            foreach (var avatar in avatars)
            {
                var distance = Distance(avatar, target);
                var inside = distance <= radius && Math.Abs(avatar.Y - target.Position.Y) <= Math.Max(target.Scale.Y * .5f + 2f, 3f);
                var was = _inside.GetValueOrDefault((objectId, avatar.AvatarId));
                if (inside == was) continue;
                _inside[(objectId, avatar.AvatarId)] = inside;
                _host.Enqueue(new ScriptEvent(objectId, inside ? "enter" : "leave", Describe(avatar, distance)));
            }
            // Avatars that left the island count as having left the object.
            foreach (var key in _inside.Where(pair => pair.Key.Object == objectId && pair.Value && avatars.All(a => a.AvatarId != pair.Key.Avatar)).Select(pair => pair.Key).ToList()) _inside.Remove(key);
        }
    }

    // ---- what scripts ask for --------------------------------------------------------------------------------

    public void ApplyChange(ScriptDefinition script, ObjectChange change)
    {
        var now = Now;
        var times = _changeTimes.TryGetValue(script.Id, out var queue) ? queue : _changeTimes[script.Id] = new Queue<double>();
        while (times.Count > 0 && now - times.Peek() > 1) times.Dequeue();
        if (times.Count >= 5) return; // a script can change its object at most five times per second
        times.Enqueue(now);
        _changes.Enqueue((script, change));
    }

    public void Say(ScriptDefinition script, string text)
    {
        if (_objects.TryGetValue(script.ObjectId, out var source)) _say(source.Name, source.Position, text);
    }

    private void DrainChanges()
    {
        for (var count = _changes.Count; count > 0; count--)
        {
            var (script, change) = _changes.Dequeue();
            if (!_inFlight.Add(script.ObjectId)) { _changes.Enqueue((script, change)); continue; }
            _ = SendChangeAsync(script, change);
        }
    }

    private async Task SendChangeAsync(ScriptDefinition script, ObjectChange change)
    {
        try
        {
            if (!_objects.TryGetValue(script.ObjectId, out var obj)) return;
            var material = JsonSerializer.Deserialize<Dictionary<string, object?>>(obj.MaterialJson) ?? [];
            foreach (var (key, value) in change.Material) material[key] = value;
            var position = change.Position ?? obj.Position; var rotation = change.Rotation ?? obj.Rotation; var scale = change.Scale ?? obj.Scale;
            var body = new
            {
                userId = script.OwnerUserId, operation = "update", objectId = obj.Id, expectedRevision = obj.Revision,
                @object = new
                {
                    primitiveType = obj.PrimitiveType, positionX = position.X, positionY = position.Y, positionZ = position.Z, rotationX = rotation.X, rotationY = rotation.Y, rotationZ = rotation.Z,
                    scaleX = scale.X, scaleY = scale.Y, scaleZ = scale.Z, materialJson = JsonSerializer.Serialize(material), geometryJson = obj.GeometryJson, physicsJson = obj.PhysicsJson,
                    groupId = obj.GroupId, parentObjectId = obj.ParentObjectId, name = obj.Name, expectedRevision = obj.Revision
                }
            };
            using var response = await _api.PostAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/build-operations", body);
            if (!response.IsSuccessStatusCode) { _log("script_change_rejected", new { script = script.Name, status = (int)response.StatusCode }); if ((int)response.StatusCode == 409) _ = RefreshAsync(CancellationToken.None); return; }
            var accepted = await response.Content.ReadAsStringAsync();
            OnObjectApplied(accepted);
            _broadcastApplied(accepted);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException) { _log("script_change_failed", new { script = script.Name, error = exception.Message }); }
        finally { _inFlight.Remove(script.ObjectId); }
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private static float Radius(CachedObject o) => Math.Max(o.Scale.X, o.Scale.Z) * .5f + 1.5f;
    private static float Distance(AvatarPresence a, CachedObject o) => MathF.Sqrt((a.X - o.Position.X) * (a.X - o.Position.X) + (a.Z - o.Position.Z) * (a.Z - o.Position.Z));

    private static Dictionary<string, object?> Describe(AvatarPresence who, float distance) => new()
    {
        ["avatar_id"] = who.AvatarId, ["avatar_name"] = who.Name, ["user_id"] = who.UserId, ["distance"] = distance, ["position"] = new Vec3(who.X, who.Y, who.Z)
    };

    private static CachedObject Parse(JsonElement e)
    {
        Guid? OptionalGuid(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g) ? g : null;
        float Number(string name, float fallback = 0) => e.TryGetProperty(name, out var v) && v.TryGetSingle(out var n) ? n : fallback;
        string Text(string name, string fallback) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
        return new CachedObject
        {
            Id = e.GetProperty("id").GetGuid(), OwnerUserId = OptionalGuid("ownerUserId") ?? Guid.Empty, GroupId = OptionalGuid("groupId"), ParentObjectId = OptionalGuid("parentObjectId"),
            Name = Text("name", "Object"), PrimitiveType = Text("primitiveType", "cube"), MaterialJson = Text("materialJson", "{}"), GeometryJson = Text("geometryJson", "{}"), PhysicsJson = Text("physicsJson", "{}"),
            Position = new Vec3(Number("positionX"), Number("positionY"), Number("positionZ")), Rotation = new Vec3(Number("rotationX"), Number("rotationY"), Number("rotationZ")),
            Scale = new Vec3(Number("scaleX", 1), Number("scaleY", 1), Number("scaleZ", 1)), Revision = e.TryGetProperty("revision", out var r) && r.TryGetInt32(out var revision) ? revision : 1
        };
    }
}
