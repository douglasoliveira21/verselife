using System.Net.Http.Json;
using System.Text.Json;
using LifeVerse.Scripting;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>What the second layer of the Lua API needs from this island: the region, the linkset, speaking at a range, private lines, and the dialogs and permission requests shown to avatars.</summary>
public sealed partial class IslandScripting
{
    private IReadOnlyList<AvatarPresence> _avatars = [];
    private string _regionName = "Ilha";
    private bool _regionStarted;
    private readonly Dictionary<Guid, (Guid Script, Guid Avatar, string? Permission, double Expires, string Kind)> _requests = [];

    /// <summary>Sends one JSON message to one avatar (the island server wires it to the avatar's connection).</summary>
    public Action<Guid, string>? SendToAvatar { get; set; }

    public IReadOnlyList<ScriptObjectState> GetLinkset(Guid objectId)
    {
        if (!_objects.TryGetValue(objectId, out var start)) return [];
        var root = start; for (var depth = 0; depth < 16 && root.ParentObjectId is { } parent && _objects.TryGetValue(parent, out var up); depth++) root = up;
        var members = _objects.Values.Where(o => o.Id != root.Id && RootOf(o) == root.Id).OrderBy(o => o.Id).Select(State).ToList();
        return [State(root), .. members];
    }

    private Guid RootOf(CachedObject o)
    {
        var current = o; for (var depth = 0; depth < 16 && current.ParentObjectId is { } parent && _objects.TryGetValue(parent, out var up); depth++) current = up;
        return current.Id;
    }

    private static ScriptObjectState State(CachedObject o) => new(o.Id, o.OwnerUserId, o.Name, o.Position, o.Rotation, o.Scale, o.ParentObjectId);

    public RegionInfo? GetRegion() => new(_config.IslandId, _regionName, _avatars.Count);

    /// <summary>Whisper (10 m), say (the usual local chat) and shout (100 m) from the object.</summary>
    public void Chat(ScriptDefinition script, ChatKind kind, string text)
    {
        if (!_objects.TryGetValue(script.ObjectId, out var source)) return;
        _say(source.Name, source.Position, text, kind switch { ChatKind.Whisper => 10f, ChatKind.Shout => 100f, _ => 30f });
    }

    /// <summary>A private line to an avatar that is on the island (it shows in their chat as a message from the object).</summary>
    public bool TellAvatar(ScriptDefinition script, Guid avatarId, string text)
    {
        if (_avatars.FirstOrDefault(a => a.AvatarId == avatarId) is not { } avatar) return false;
        _tellUser(avatar.UserId, $"[{(_objects.TryGetValue(script.ObjectId, out var source) ? source.Name : "Objeto")}] {text}"); return true;
    }

    // ---- dialogs and permission requests ----------------------------------------------------------------------

    private void OnScriptRequest(ScriptRequest request)
    {
        var now = Now;
        // somebody who is not on the island cannot be asked; a dialog is only shown to somebody near the object (a permission can be asked from further away)
        var avatar = _avatars.FirstOrDefault(a => a.AvatarId == request.AvatarId);
        if (avatar is null || SendToAvatar is null) return;
        if (request.Kind is "dialog" or "payment" && _objects.TryGetValue(request.ObjectId, out var source) && Distance(avatar, source) > 40) return;
        foreach (var stale in _requests.Where(r => r.Value.Expires < now).Select(r => r.Key).ToList()) _requests.Remove(stale);
        if (_requests.Count(r => r.Value.Avatar == request.AvatarId) >= 6) return; // nobody is buried under dialogs
        _requests[request.RequestId] = (request.ScriptId, request.AvatarId, request.Permission, now + request.TimeoutSeconds, request.Kind);
        SendToAvatar(request.AvatarId, JsonSerializer.Serialize(new { operation = "script_dialog", id = request.RequestId, kind = request.Kind, title = request.Title, message = request.Message, buttons = request.Buttons, input = request.WithInput, timeout = request.TimeoutSeconds, objectName = request.ObjectName }));
    }

    /// <summary>The answer of an avatar to something a script asked. Checked against what was asked of that very avatar: made-up, repeated or somebody else's answers do nothing.</summary>
    public void OnDialogResponse(AvatarPresence who, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            if (TryHudAction(who, root)) return;
            if (!root.TryGetProperty("id", out var idElement) || !Guid.TryParse(idElement.GetString(), out var id)) return;
            var response = root.TryGetProperty("response", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
            var input = root.TryGetProperty("input", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
            if (!_requests.TryGetValue(id, out var request) || request.Avatar != who.AvatarId || request.Expires < Now) return;
            _requests.Remove(id);
            if (request.Permission is { } permission) _host.ResolvePermission(request.Script, who.AvatarId, permission, response == "Permitir");
            else if (request.Kind == "payment") _host.RespondPayment(id, who.AvatarId, response);
            else _host.RespondDialog(id, who.AvatarId, response, input);
        }
        catch (JsonException) { }
    }

    /// <summary>The island is going down: scripts hear <c>on_region_shutdown</c> and their state is saved.</summary>
    public async Task ShutdownAsync()
    {
        _host.RegionShutdown();
        try { await FlushStateAsync(CancellationToken.None); } catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { _log("script_state_flush_failed", new { error = exception.Message }); }
    }

}

public sealed partial class IslandScripting
{
    private bool _grantsLoaded;
    private readonly Dictionary<Guid, double> _logCursor = [];

    /// <summary>Tells the API how every script is doing (state, counters, the console lines since the last report, the permissions in force) so the owner sees it in the editor. A failed report is simply tried again next time.</summary>
    public async Task ReportRuntimeAsync(CancellationToken ct)
    {
        var metrics = _host.AllMetrics(); if (metrics.Count == 0) return;
        var logs = new List<object>(); var advance = new Dictionary<Guid, double>(); var epoch = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(Now);
        foreach (var script in metrics.Take(400))
        {
            var cursor = _logCursor.GetValueOrDefault(script.ScriptId, double.NegativeInfinity);
            foreach (var line in _host.LogsOf(script.ScriptId, 200).Where(l => l.At > cursor).Take(50))
            {
                logs.Add(new { scriptId = line.ScriptId, level = line.Level.ToString().ToLowerInvariant(), @event = line.Event, text = line.Text, stack = line.StackTrace, milliseconds = line.Milliseconds, at = epoch + TimeSpan.FromSeconds(line.At) });
                advance[script.ScriptId] = Math.Max(advance.GetValueOrDefault(script.ScriptId, double.NegativeInfinity), line.At);
            }
        }
        var batch = new
        {
            runtime = metrics.Take(400).Select(m => new { scriptId = m.ScriptId, state = m.State.ToString(), stateText = m.StateText, events = m.Events, eventsDropped = m.EventsDropped, errors = m.Errors, violations = m.Violations, suspensions = m.Suspensions, apiCalls = m.ApiCalls, instructions = m.Instructions, allocatedBytes = m.AllocatedBytes, messagesSent = m.MessagesSent, cpuMilliseconds = m.CpuMilliseconds, worstEventMilliseconds = m.WorstEventMilliseconds, activeTimers = m.ActiveTimers, stateKeys = m.StateKeys, lastError = m.LastError, lastErrorLine = m.LastErrorLine, suspendedReason = m.SuspendedReason }).ToArray(),
            logs = logs.ToArray(),
            grants = _host.Permissions.All().Where(g => g.Subject != Guid.Empty).Take(2000).Select(g => new { scriptId = g.Script, subjectId = g.Subject, permission = g.Permission }).ToArray()
        };
        using var response = await _api.PutAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/script-runtime", batch, ct);
        if (!response.IsSuccessStatusCode) { _log("script_runtime_report_failed", new { status = (int)response.StatusCode }); return; }
        foreach (var (id, at) in advance) _logCursor[id] = at;
    }
}

public sealed partial class IslandScripting
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(Guid Script, Guid Request, int Status, string Body, string? Error)> _httpDone = new();
    private int _httpInFlight;

    /// <summary>Starts a request for a script: the API checks the permission, the address and the limits and makes the request; the answer is queued and handed to the script on the next tick (scripts only run on the main thread).</summary>
    public bool StartHttp(ScriptDefinition script, Guid requestId, HttpSpec spec)
    {
        if (Interlocked.Increment(ref _httpInFlight) > 32) { Interlocked.Decrement(ref _httpInFlight); return false; }
        _ = Task.Run(async () =>
        {
            try
            {
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var response = await _api.PostAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/script-http", new { scriptId = script.Id, url = spec.Url, method = spec.Method, headers = spec.Headers, body = spec.Body }, cancel.Token);
                var text = await response.Content.ReadAsStringAsync(cancel.Token); using var document = JsonDocument.Parse(text); var root = document.RootElement;
                string? error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : response.IsSuccessStatusCode ? null : "request_failed";
                _httpDone.Enqueue((script.Id, requestId, root.TryGetProperty("status", out var s) && s.TryGetInt32(out var status) ? status : 0, root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "", error));
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException) { _httpDone.Enqueue((script.Id, requestId, 0, "", "request_failed")); }
            finally { Interlocked.Decrement(ref _httpInFlight); }
        });
        return true;
    }

    private void DrainHttp() { while (_httpDone.TryDequeue(out var done)) _host.CompleteHttp(done.Script, done.Request, done.Status, done.Body, done.Error); }
}

public sealed partial class IslandScripting
{
    /// <summary>Pushes a simulated body (action: impulse, velocity, angular, gravity). Set by the server where the physics simulation lives; null in tests that have none.</summary>
    public Func<Guid, string, Vec3, bool>? PhysicsAction { get; set; }
    public Func<Guid, Vec3?>? VelocityOf { get; set; }

    public bool ApplyPhysics(ScriptDefinition script, string action, Vec3 value) => PhysicsAction?.Invoke(script.ObjectId, action, value) ?? false;
    public Vec3? GetVelocity(Guid objectId) => VelocityOf?.Invoke(objectId);

    /// <summary>The physics settings of the object with what the script asked changed, written the way the build tools write them. Settings the rules refuse (a physical ghost, for instance) leave the object as it was.</summary>
    private string PhysicsAfter(string current, PhysicsChange? change, ScriptDefinition script)
    {
        if (change is null) return current;
        var settings = LifeVerse.Shared.Building.PhysicsRules.Read(current);
        settings = settings with { Collision = change.Collision ?? settings.Collision, Physical = change.Physical ?? settings.Physical, Mass = change.Mass ?? settings.Mass };
        if (change.Collision == false) settings = settings with { Physical = false };      // a ghost cannot be a physical body
        var written = LifeVerse.Shared.Building.PhysicsRules.Write(current, settings);
        if (LifeVerse.Shared.Building.PhysicsRules.Validate(written) is { } problem) { _log("script_physics_refused", new { script = script.Name, problem }); return current; }
        return written;
    }
}

public sealed partial class IslandScripting
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(Guid Script, Guid Request, Guid? Transaction, string? Error)> _paymentsDone = new();

    public string? AvatarName(Guid avatarId) => _avatars.FirstOrDefault(a => a.AvatarId == avatarId)?.Name;

    /// <summary>The player pressed Pagar: the API moves the money (and decides if it can). The result is queued and handed to the script on the next tick, only if the economy confirmed it.</summary>
    public bool StartPayment(ScriptDefinition script, Guid requestId, Guid avatarId, long amount)
    {
        if (_avatars.FirstOrDefault(a => a.AvatarId == avatarId) is not { } payer) return false;
        _ = Task.Run(async () =>
        {
            try
            {
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var response = await _api.PostAsJsonAsync($"{_config.ApiBaseUrl}/api/v1/platform/servers/{_config.InstanceId}/script-payment", new { scriptId = script.Id, payerUserId = payer.UserId, amount, requestId = requestId.ToString("N") }, cancel.Token);
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel.Token)); var root = document.RootElement;
                var status = root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                var transaction = root.TryGetProperty("transactionId", out var t) && Guid.TryParse(t.GetString(), out var parsed) ? parsed : (Guid?)null;
                if (response.IsSuccessStatusCode && status is "completed" or "already_processed") _paymentsDone.Enqueue((script.Id, requestId, transaction, null));
                else _paymentsDone.Enqueue((script.Id, requestId, null, root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : "failed"));
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException) { _paymentsDone.Enqueue((script.Id, requestId, null, "unavailable")); }
        });
        return true;
    }

    private void DrainPayments() { while (_paymentsDone.TryDequeue(out var done)) _host.CompletePayment(done.Script, done.Request, done.Transaction, done.Error); }
}

public sealed partial class IslandScripting
{
    /// <summary>The items an object carries in its Conteúdo (kind, name and asset), as the build tools save them.</summary>
    private static List<ScriptItem> ItemsOf(string contentJson)
    {
        var items = new List<ScriptItem>();
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(contentJson) ? "[]" : contentJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return items;
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                string Text(string name) => entry.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var name = Text("name"); if (name.Length == 0) continue;
                items.Add(new ScriptItem(name.Length > 80 ? name[..80] : name, Text("category") is { Length: > 0 } category ? category : "item", Guid.TryParse(Text("assetId"), out var asset) ? asset : null));
                if (items.Count >= 200) break;
            }
        }
        catch (JsonException) { }
        return items;
    }

    public IReadOnlyList<ScriptItem> GetContents(Guid objectId) => _objects.TryGetValue(objectId, out var o) ? ItemsOf(o.ContentJson) : [];

    private void NoteContents(Dictionary<Guid, string> before)
    {
        foreach (var (id, cached) in _objects)
        {
            if (!before.TryGetValue(id, out var old) || old == cached.ContentJson) continue;
            var was = ItemsOf(old); var now = ItemsOf(cached.ContentJson);
            _host.ContentsChanged(id, [.. now.Where(n => !was.Any(w => w.Name == n.Name && w.AssetId == n.AssetId))], [.. was.Where(w => !now.Any(n => n.Name == w.Name && n.AssetId == w.AssetId))]);
        }
    }
}

public sealed partial class IslandScripting
{
    /// <summary>Set by the server: play a gesture on an avatar (name, seconds) / stop it.</summary>
    public Func<Guid, string, double, bool>? AnimationPlayer { get; set; }
    public Action<Guid>? AnimationStopper { get; set; }

    public bool PlayAnimation(ScriptDefinition script, Guid avatarId, string name, double seconds) => AnimationPlayer?.Invoke(avatarId, name, seconds) ?? false;
    public void StopAnimation(ScriptDefinition script, Guid avatarId) => AnimationStopper?.Invoke(avatarId);

    /// <summary>A gesture a script started ended (finished, interrupted because the avatar moved, stopped or replaced): the script that started it hears it.</summary>
    public void AnimationEnded(Guid avatarId, string name, string reason) => _host.AnimationEnded(avatarId, name, reason);
}
