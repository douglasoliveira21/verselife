using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using LifeVerse.Scripting;
using LifeVerse.Shared.Networking;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>An object as the physics simulation needs it: where it is, how big, and its physics settings.</summary>
public sealed record PhysicsBodyDefinition(Guid Id, string PrimitiveType, Vec3 Position, Vec3 Rotation, Vec3 Scale, string PhysicsJson, bool Blocks);

public sealed record AvatarPresence(int PeerId, Guid AvatarId, Guid UserId, string Name, float X, float Y, float Z);

/// Connects the Lua script host to this island: loads its objects and scripts from the API, turns avatar actions
/// (touch, walking into an object, chatting) into script events, and applies what scripts ask for through the same
/// authoritative build-operation path players use, so permissions, revisions and replication all still apply.
public sealed partial class IslandScripting : IScriptWorld
{
    private sealed class CachedObject
    {
        public Guid Id, OwnerUserId; public Guid? GroupId, ParentObjectId; public string Name = "", PrimitiveType = "cube", MaterialJson = "{}", GeometryJson = "{}", PhysicsJson = "{}", ContentJson = "[]";
        public Vec3 Position, Rotation, Scale = new(1, 1, 1); public int Revision;
    }

    private readonly ScriptHost _host;
    private readonly System.Net.Http.HttpClient _api;
    private readonly ServerConfiguration _config;
    private readonly Action<string> _broadcastApplied;
    private readonly Action<string, Vec3, string, float> _say;
    private readonly Action<Guid, string> _tellUser;
    private readonly Action<string, object> _log;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<Guid, CachedObject> _objects = [];

    /// What stops walking avatars, kept in step with the objects (the movement simulation uses it so the server and the players agree on walls and floors).
    public ObstacleField Obstacles { get; } = new();

    /// <summary>Goes up whenever an object is added, changed or removed; the physics simulation looks at it to know when to read the objects again.</summary>
    public int ObjectsVersion { get; private set; }

    public IEnumerable<PhysicsBodyDefinition> Bodies() => _objects.Values.Select(o => new PhysicsBodyDefinition(o.Id, o.PrimitiveType, o.Position, o.Rotation, o.Scale, o.PhysicsJson, ObstacleField.Blocks(o.PhysicsJson, o.PrimitiveType)));

    private void UpdateObstacle(CachedObject o) { ObjectsVersion++; UpdateObstacleCore(o); }

    private void UpdateObstacleCore(CachedObject o) =>
        Obstacles.Set(o.Id, ObstacleField.Blocks(o.PhysicsJson, o.PrimitiveType) ? ObstacleField.For(o.PrimitiveType, new System.Numerics.Vector3(o.Position.X, o.Position.Y, o.Position.Z), new System.Numerics.Vector3(o.Rotation.X, o.Rotation.Y, o.Rotation.Z), new System.Numerics.Vector3(o.Scale.X, o.Scale.Y, o.Scale.Z)) : null);
    private readonly Dictionary<(Guid Object, Guid Avatar), bool> _inside = [];
    private readonly Dictionary<Guid, Queue<double>> _touches = [];
    private readonly Dictionary<Guid, Queue<double>> _changeTimes = [];
    private readonly Queue<(ScriptDefinition Script, ObjectChange Change)> _changes = new();
    private readonly HashSet<Guid> _inFlight = [];
    private readonly Dictionary<Guid, Dictionary<string, string>> _unsentState = [];
    private double _lastProximity;
    private bool _refreshing;

    public IslandScripting(System.Net.Http.HttpClient api, ServerConfiguration config, Action<string> broadcastApplied, Action<string, Vec3, string, float> say, Action<Guid, string> tellUser, Action<string, object> log)
    {
        _api = api; _config = config; _broadcastApplied = broadcastApplied; _say = say; _tellUser = tellUser; _log = log;
        _host = new ScriptHost(this); _host.Request += OnScriptRequest;
        _host.Report += report => _tellUser(report.Script.OwnerUserId, $"[{report.Script.Name}] {report.Message}");
        _host.Log += text => _log("script_log", new { text });
    }

    public double Now => _clock.Elapsed.TotalSeconds;
    public IReadOnlyList<ScriptInfo> Scripts => _host.Scripts;
    public ScriptLifecycle? StateOf(Guid scriptId) => _host.StateOf(scriptId);

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
            // objects the server removed by itself (a temporary one whose time is up, a return by the land) vanish for everybody on the island
            foreach (var gone in _objects.Keys.Where(id => !fresh.ContainsKey(id)).ToList()) { _texts.Remove(gone); _broadcastApplied(JsonSerializer.Serialize(new { operation = "delete", objectId = gone })); }
            var previousOwners = _objects.ToDictionary(pair => pair.Key, pair => pair.Value.OwnerUserId); var previousContents = _objects.ToDictionary(pair => pair.Key, pair => pair.Value.ContentJson);
            _objects.Clear(); Obstacles.Clear(); ObjectsVersion++; foreach (var (id, cached) in fresh) { _objects[id] = cached; UpdateObstacle(cached); }
            var definitions = root.GetProperty("scripts").EnumerateArray().Select(s => new ScriptDefinition(s.GetProperty("id").GetGuid(), s.GetProperty("objectId").GetGuid(), s.GetProperty("ownerUserId").GetGuid(), s.GetProperty("name").GetString() ?? "script", s.GetProperty("source").GetString() ?? "", s.GetProperty("version").GetInt32(), s.GetProperty("instructionBudget").GetInt32(), s.TryGetProperty("paused", out var paused) && paused.ValueKind == JsonValueKind.True)).ToList();
            if (root.TryGetProperty("grants", out var savedGrants) && savedGrants.ValueKind == JsonValueKind.Array)
            {
                var ownerGrants = new List<(Guid, string)>();
                foreach (var grant in savedGrants.EnumerateArray())
                {
                    if (!Guid.TryParse(grant.GetProperty("scriptId").GetString(), out var grantScript) || !Guid.TryParse(grant.GetProperty("subjectId").GetString(), out var grantSubject)) continue;
                    var permission = grant.GetProperty("permission").GetString() ?? "";
                    if (grantSubject == Guid.Empty) ownerGrants.Add((grantScript, permission)); else if (!_grantsLoaded) _host.Permissions.Grant(grantScript, grantSubject, permission);   // what avatars allowed is kept by the island itself after the first read; what the owner allows comes from the editor every time
                }
                _host.Permissions.ReplaceOwnerGrants(ownerGrants); _grantsLoaded = true;
            }
            var state = new Dictionary<Guid, Dictionary<string, string>>();
            if (root.TryGetProperty("state", out var states))
                foreach (var scriptState in states.EnumerateObject()) state[Guid.Parse(scriptState.Name)] = scriptState.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "null");
            if (root.TryGetProperty("islandName", out var islandName) && islandName.ValueKind == JsonValueKind.String) _regionName = islandName.GetString() ?? _regionName;
            foreach (var (id, fresher) in fresh) if (previousOwners.TryGetValue(id, out var before) && before != fresher.OwnerUserId) _host.OwnerChanged(id, fresher.OwnerUserId);
            _host.Sync(definitions, id => state.GetValueOrDefault(id));
            NoteContents(previousContents);
            if (!_regionStarted) { _regionStarted = true; _host.RegionStarted(); }
            _log("scripts_loaded", new { objects = _objects.Count, scripts = definitions.Count });
        }
        finally { _refreshing = false; }
    }

    public async Task FlushStateAsync(CancellationToken ct)
    {
        foreach (var (id, values) in _host.TakeDirtyState()) _unsentState[id] = values;
        if (_unsentState.Count == 0) { await ReportRuntimeAsync(ct); return; }
        var batch = new { states = _unsentState.Select(pair => new { scriptId = pair.Key, values = pair.Value }).ToArray() };
        using var response = await _api.PutAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/script-state", batch, ct);
        if (response.IsSuccessStatusCode) _unsentState.Clear(); else _log("script_state_save_failed", new { status = (int)response.StatusCode });
        await ReportRuntimeAsync(ct);
    }

    /// Keeps the object cache in step with build operations the server accepted (players' or scripts').
    public void OnObjectApplied(string acceptedJson)
    {
        try
        {
            using var document = JsonDocument.Parse(acceptedJson);
            var root = document.RootElement;
            if (root.TryGetProperty("objectState", out var state) && state.ValueKind == JsonValueKind.Object) { var cached = Parse(state); _objects[cached.Id] = cached; UpdateObstacle(cached); FollowEffects(cached.Id); }
            else if (root.TryGetProperty("objectId", out var id) && Guid.TryParse(id.GetString(), out var removed)) { _objects.Remove(removed); Obstacles.Remove(removed); ObjectsVersion++; }
            foreach (var listName in new[] { "objects", "healed" }) if (root.TryGetProperty(listName, out var placed) && placed.ValueKind == JsonValueKind.Array) foreach (var one in placed.EnumerateArray()) { var cached = Parse(one); _objects[cached.Id] = cached; UpdateObstacle(cached); } // constructions placed, linksets changed, objects protected or re-hung: every object it carries
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
    }


    // ---- sound and floating text, asked for by scripts -------------------------------------------------------

    private readonly Dictionary<Guid, (string Text, string Color)> _texts = [];

    /// <summary>The sound of the object with that name (or the first when no name is given), from what the object carries: the asset id, or null.</summary>
    private static Guid? SoundOf(CachedObject target, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(target.ContentJson) ? "[]" : target.ContentJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            Guid? first = null;
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("category", out var category) || category.GetString() != "sounds" || !entry.TryGetProperty("assetId", out var asset) || !Guid.TryParse(asset.GetString(), out var id)) continue;
                first ??= id;
                if (entry.TryGetProperty("name", out var entryName) && string.Equals(entryName.GetString(), name, StringComparison.OrdinalIgnoreCase)) return id;
            }
            return string.IsNullOrWhiteSpace(name) ? first : null;
        }
        catch (JsonException) { return null; }
    }

    public bool PlaySound(ScriptDefinition script, string soundName, float volume, bool loop)
    {
        if (!_objects.TryGetValue(script.ObjectId, out var target) || SoundOf(target, soundName) is not { } asset) return false;
        _broadcastApplied(JsonSerializer.Serialize(new { operation = "sound", objectId = target.Id, assetId = asset, x = target.Position.X, y = target.Position.Y, z = target.Position.Z, volume, loop }));
        return true;
    }

    public void StopSound(ScriptDefinition script) => _broadcastApplied(JsonSerializer.Serialize(new { operation = "sound_stop", objectId = script.ObjectId }));

    public void SetText(ScriptDefinition script, string text, string color)
    {
        if (!_objects.ContainsKey(script.ObjectId)) return;
        if (text.Length == 0) _texts.Remove(script.ObjectId); else _texts[script.ObjectId] = (text, color);
        _broadcastApplied(TextOp(script.ObjectId, text, color));
    }

    /// <summary>The floating texts the island shows now, as messages for somebody who has just arrived.</summary>
    public IEnumerable<string> TextOps() => _texts.Where(pair => _objects.ContainsKey(pair.Key)).Select(pair => TextOp(pair.Key, pair.Value.Text, pair.Value.Color));

    private string TextOp(Guid id, string text, string color) { var o = _objects[id]; return JsonSerializer.Serialize(new { operation = "text", objectId = id, text, color, x = o.Position.X, y = o.Position.Y + o.Scale.Y / 2, z = o.Position.Z }); }
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
                _host.Enqueue(new ScriptEvent(objectId, "chat", new Dictionary<string, object?> { ["channel"] = 0, ["avatar_id"] = speaker.AvatarId, ["avatar_name"] = speaker.Name, ["sender"] = speaker.Name, ["avatar"] = new Dictionary<string, object?> { ["id"] = speaker.AvatarId, ["name"] = speaker.Name }, ["message"] = message }));
    }

    /// Called every server tick: timers, walking into or out of objects, queued events and the changes scripts made.
    public void Update(IReadOnlyList<AvatarPresence> avatars)
    {
        var now = Now; _avatars = avatars; DrainHttp(); DrainPayments(); RestoreHuds(avatars);
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
        if (_objects.TryGetValue(script.ObjectId, out var source)) _say(source.Name, source.Position, text, 30f);
    }

    private void DrainChanges()
    {
        for (var count = _changes.Count; count > 0; count--)
        {
            var (script, change) = _changes.Dequeue();
            if (!_inFlight.Add(change.TargetId ?? script.ObjectId)) { _changes.Enqueue((script, change)); continue; }
            _ = SendChangeAsync(script, change);
        }
    }

    private async Task SendChangeAsync(ScriptDefinition script, ObjectChange change)
    {
        try
        {
            var targetId = change.TargetId ?? script.ObjectId;
            if (!_objects.TryGetValue(targetId, out var obj) || (change.TargetId is not null && obj.OwnerUserId != script.OwnerUserId)) return; // another part of the linkset must belong to the owner of the script
            var material = JsonSerializer.Deserialize<Dictionary<string, object?>>(obj.MaterialJson) ?? [];
            foreach (var (key, value) in change.Material) material[key] = value;
            var position = change.Position ?? obj.Position; var rotation = change.Rotation ?? obj.Rotation; var scale = change.Scale ?? obj.Scale;
            var body = new
            {
                userId = script.OwnerUserId, operation = "update", objectId = obj.Id, expectedRevision = obj.Revision,
                @object = new
                {
                    primitiveType = obj.PrimitiveType, positionX = position.X, positionY = position.Y, positionZ = position.Z, rotationX = rotation.X, rotationY = rotation.Y, rotationZ = rotation.Z,
                    scaleX = scale.X, scaleY = scale.Y, scaleZ = scale.Z, materialJson = JsonSerializer.Serialize(material), geometryJson = obj.GeometryJson, physicsJson = PhysicsAfter(obj.PhysicsJson, change.Physics, script),
                    groupId = obj.GroupId, parentObjectId = obj.ParentObjectId, name = change.Name ?? obj.Name, expectedRevision = obj.Revision
                }
            };
            using var response = await _api.PostAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/build-operations", body);
            if (!response.IsSuccessStatusCode) { _log("script_change_rejected", new { script = script.Name, status = (int)response.StatusCode }); if ((int)response.StatusCode == 409) _ = RefreshAsync(CancellationToken.None); return; }
            var accepted = await response.Content.ReadAsStringAsync();
            OnObjectApplied(accepted);
            _broadcastApplied(accepted);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException) { _log("script_change_failed", new { script = script.Name, error = exception.Message }); }
        finally { _inFlight.Remove(change.TargetId ?? script.ObjectId); }
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private static float Radius(CachedObject o) => Math.Max(o.Scale.X, o.Scale.Z) * .5f + 1.5f;
    private static float Distance(AvatarPresence a, CachedObject o) => MathF.Sqrt((a.X - o.Position.X) * (a.X - o.Position.X) + (a.Z - o.Position.Z) * (a.Z - o.Position.Z));

    private static Dictionary<string, object?> Describe(AvatarPresence who, float distance) => new()
    {
        ["avatar_id"] = who.AvatarId, ["avatar_name"] = who.Name, ["user_id"] = who.UserId, ["distance"] = distance, ["position"] = new Vec3(who.X, who.Y, who.Z), ["avatar"] = new Dictionary<string, object?> { ["id"] = who.AvatarId, ["name"] = who.Name }
    };

    private static CachedObject Parse(JsonElement e)
    {
        Guid? OptionalGuid(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g) ? g : null;
        float Number(string name, float fallback = 0) => e.TryGetProperty(name, out var v) && v.TryGetSingle(out var n) ? n : fallback;
        string Text(string name, string fallback) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
        return new CachedObject
        {
            Id = e.GetProperty("id").GetGuid(), OwnerUserId = OptionalGuid("ownerUserId") ?? Guid.Empty, GroupId = OptionalGuid("groupId"), ParentObjectId = OptionalGuid("parentObjectId"),
            Name = Text("name", "Object"), PrimitiveType = Text("primitiveType", "cube"), MaterialJson = Text("materialJson", "{}"), GeometryJson = Text("geometryJson", "{}"), PhysicsJson = Text("physicsJson", "{}"), ContentJson = Text("contentJson", "[]"),
            Position = new Vec3(Number("positionX"), Number("positionY"), Number("positionZ")), Rotation = new Vec3(Number("rotationX"), Number("rotationY"), Number("rotationZ")),
            Scale = new Vec3(Number("scaleX", 1), Number("scaleY", 1), Number("scaleZ", 1)), Revision = e.TryGetProperty("revision", out var r) && r.TryGetInt32(out var revision) ? revision : 1
        };
    }
}
