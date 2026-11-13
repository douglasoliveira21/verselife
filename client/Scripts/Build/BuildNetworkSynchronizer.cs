using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

/// <summary>
/// The new editor's link to the island. It keeps the replica of the island's objects (one <see cref="BuildObjectView"/> per object, loaded from the API when the
/// person arrives and kept in step with what the island server announces), sends commands to the island server and tells the editor what came back: a create
/// that was accepted, a refusal with its reason, an object changed or removed by somebody else. The server decides everything; this class only waits for its word.
/// </summary>
public partial class BuildNetworkSynchronizer : Node
{
    private const int LoadPerFrame = 40;
    private const double CommandTimeoutSeconds = 12;

    private NetworkManager? _network;
    private SessionManager? _session;
    private HttpRequest? _http, _limitsHttp;
    private Node3D? _worldRoot;
    private Guid _islandId;
    private readonly Dictionary<Guid, BuildObjectView> _views = [];
    private readonly Queue<BuildObjectState> _loadQueue = new();
    private readonly Queue<Action<BuildObjectState>> _createCallbacks = new();
    private readonly Dictionary<Guid, double> _pending = [];
    private int _loadTotal, _loadDone;
    private double _clock;

    /// <summary>Tests capture the commands here instead of sending them over the network.</summary>
    public Func<string, bool>? OperationSink { get; set; }

    public IReadOnlyDictionary<Guid, BuildObjectView> Views => _views;
    public Guid IslandId => _islandId;
    public Guid MyUserId => _session?.UserId ?? Guid.Empty;
    public bool CanSend => OperationSink is not null || _network is { IsAuthenticated: true };
    public int LoadBacklog => _loadQueue.Count;
    public int PendingCommands => _pending.Count;

    /// <summary>An object was created, changed or arrived (second argument true when it is new to the replica).</summary>
    public event Action<BuildObjectState, bool>? ObjectChanged;
    public event Action<Guid>? ObjectRemoved;
    /// <summary>The server refused something; the text is ready to show.</summary>
    public event Action<string>? Rejected;
    /// <summary>The island's usage changed (the numbers come from the server).</summary>
    public event Action<BuildLimits>? LimitsChanged;
    /// <summary>The person bought something: what kind of sale, what it cost and what they received.</summary>
    public event Action<Purchase>? Purchased;
    /// <summary>A lock or unlock announced by the island (mine or somebody else's): the operation and the whole message.</summary>
    public event Action<string, JsonElement>? LockMessage;
    /// <summary>Somebody is dragging objects right now: who, and where the objects are at this instant (nothing saved).</summary>
    public event Action<Guid, IReadOnlyList<LifeVerse.Shared.Building.LivePose>>? LiveMoved;
    /// <summary>The connection to the island came back (the replica is loaded again).</summary>
    public event Action? Reconnected;
    /// <summary>Asks who, other than me, is editing one of these objects (null when none). Set by the lock manager; the island also refuses, this only saves the round trip.</summary>
    public Func<IEnumerable<Guid>, string?>? LockGate { get; set; }

    private bool HeldByOther(IEnumerable<Guid> ids)
    {
        if (LockGate?.Invoke(ids) is not { } holder) return false;
        Rejected?.Invoke(BuildPersistenceManager.RejectionText("locked:" + holder)); return true;
    }
    public sealed record Purchase(string SaleType, long Price, IReadOnlyList<string> Delivered);

    public override void _Ready()
    {
        _network = GetNodeOrNull<NetworkManager>("/root/NetworkManager"); _session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        _worldRoot = GetParent() as Node3D;
        if (_network is null) return;
        _network.BuildOperationReceived += OnOperation; _network.BuildOperationRejected += OnRejected; _network.ConnectionStateChanged += OnConnectionState;
        _http = new HttpRequest { Timeout = 30 }; AddChild(_http); _http.RequestCompleted += OnObjectsLoaded;
        _limitsHttp = new HttpRequest { Timeout = 10 }; AddChild(_limitsHttp); _limitsHttp.RequestCompleted += OnLimitsLoaded;
        if (_network.IsAuthenticated && _network.CurrentIslandId != Guid.Empty) { _islandId = _network.CurrentIslandId; Load(); }
    }

    public override void _ExitTree()
    {
        if (_network is null) return;
        _network.BuildOperationReceived -= OnOperation; _network.BuildOperationRejected -= OnRejected; _network.ConnectionStateChanged -= OnConnectionState;
        Clear();
    }

    /// <summary>Removes every replicated object (this editor is going away, or the person left the island).</summary>
    public void Clear()
    {
        _loadQueue.Clear(); _pending.Clear(); _createCallbacks.Clear();
        foreach (var view in _views.Values) if (IsInstanceValid(view)) view.QueueFree();
        _views.Clear();
    }

    private void OnConnectionState(string state, string detail)
    {
        if (state == "online") { _islandId = _network!.CurrentIslandId; Load(); Reconnected?.Invoke(); }
        else if (state is "offline" or "failed" or "switching") { Clear(); }
    }

    // ---- loading the island's objects ------------------------------------------------------------------------

    public void Load()
    {
        if (_http is null || _islandId == Guid.Empty || _session is not { IsAuthenticated: true }) return;
        var app = GetNode<AppManager>("/root/AppManager");
        _http.Request($"{app.ApiBaseUrl}/api/v1/worlds/islands/{_islandId}/objects", [$"Authorization: Bearer {_session.AccessToken}"]);
    }

    private void OnObjectsLoaded(long result, long responseCode, string[] headers, byte[] body)
    {
        if (responseCode is < 200 or >= 300) { _network?.ReportWorldProgress("objects", 0, -2); return; } // -2: the list of objects could not be loaded
        try
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(body)); var count = 0;
            foreach (var element in document.RootElement.EnumerateArray()) { if (BuildPersistenceManager.ParseState(element.GetRawText()) is { } state) { _loadQueue.Enqueue(state); count++; } }
            _loadTotal = count; _loadDone = 0;
            if (count == 0) _network?.ReportWorldProgress("objects", 0, 0);
        }
        catch (JsonException) { GD.PushWarning("event=build_objects_invalid"); }
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        var before = _loadQueue.Count;
        for (var i = 0; i < LoadPerFrame && _loadQueue.Count > 0; i++) Apply(_loadQueue.Dequeue(), announce: false);
        if (before > 0 && _loadTotal > 0) { _loadDone += before - _loadQueue.Count; _network?.ReportWorldProgress("objects", Math.Min(_loadDone, _loadTotal), _loadTotal); }
        if (_pending.Count > 0)
            foreach (var id in _pending.Where(pair => _clock - pair.Value > CommandTimeoutSeconds).Select(pair => pair.Key).ToArray())
            {
                _pending.Remove(id); if (_views.TryGetValue(id, out var view) && IsInstanceValid(view)) view.Apply(view.State); // the server never answered: show what it last confirmed
                Rejected?.Invoke("O servidor não respondeu a uma alteração; mostrando a última versão confirmada.");
            }
    }

    // ---- what the island announces ---------------------------------------------------------------------------

    public void OnOperation(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            var operation = root.GetProperty("operation").GetString();
            if (operation is "lock" or "unlock") { LockMessage?.Invoke(operation, root.Clone()); return; }
            if (operation == "live") { ReadLive(root); return; }
            if (operation is "sound" or "sound_stop" or "text" or "physics" or "script_dialog" or "script_hud" or "effect") return; // other nodes (or later phases) deal with those; none of them changes the object list
            if (operation == "rez") { if (root.TryGetProperty("objects", out var rezzed)) ApplyList(rezzed); return; }
            if (operation is "structure" or "protect" or "properties") { if (root.TryGetProperty("objects", out var changed)) ApplyList(changed); return; }
            if (operation == "bought")
            {
                if (root.TryGetProperty("objects", out var sold)) ApplyList(sold);
                if (root.TryGetProperty("buyerUserId", out var who) && Guid.TryParse(who.GetString(), out var buyerId) && buyerId == MyUserId && !(root.TryGetProperty("replay", out var replay) && replay.ValueKind == JsonValueKind.True))
                    Purchased?.Invoke(new Purchase(root.TryGetProperty("saleType", out var t) ? t.GetString() ?? "original" : "original", root.TryGetProperty("price", out var price) ? price.GetInt64() : 0, root.TryGetProperty("delivered", out var d) && d.ValueKind == JsonValueKind.Array ? [.. d.EnumerateArray().Select(x => x.GetString() ?? "")] : []));
                return;
            }
            if (root.TryGetProperty("objectState", out var element) && element.ValueKind != JsonValueKind.Null)
            {
                if (BuildPersistenceManager.ParseState(element.GetRawText()) is not { } state) return;
                var isNew = !_views.ContainsKey(state.Id);
                Apply(state, announce: true); _pending.Remove(state.Id);
                if (operation == "create" && state.CreatorUserId == MyUserId && _createCallbacks.Count > 0) _createCallbacks.Dequeue()(state);
            }
            else if (root.TryGetProperty("objectId", out var id) && Guid.TryParse(id.GetString(), out var objectId)) Remove(objectId);
            if (root.TryGetProperty("healed", out var healed)) ApplyList(healed); // what hung under a deleted object was re-hung by the server
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
    }

    private void ApplyList(JsonElement list)
    {
        if (list.ValueKind != JsonValueKind.Array) return;
        foreach (var element in list.EnumerateArray()) if (BuildPersistenceManager.ParseState(element.GetRawText()) is { } state) { Apply(state, announce: true); _pending.Remove(state.Id); }
    }

    private void Apply(BuildObjectState state, bool announce)
    {
        if (_worldRoot is null) return;
        var isNew = !_views.TryGetValue(state.Id, out var view) || !IsInstanceValid(view);
        if (isNew) { view = new BuildObjectView(); _worldRoot.AddChild(view); _views[state.Id] = view; }
        // an answer that is older than what we already show is not applied (updates can arrive out of order)
        else if (state.Revision < view!.State.Revision) return;
        view!.Apply(state);
        if (announce) ObjectChanged?.Invoke(state, isNew);
    }

    private void Remove(Guid id)
    {
        _pending.Remove(id);
        if (!_views.Remove(id, out var view)) return;
        if (IsInstanceValid(view)) view.QueueFree();
        ObjectRemoved?.Invoke(id);
    }

    private void OnRejected(string code)
    {
        _createCallbacks.Clear();
        foreach (var id in _pending.Keys.ToArray()) if (_views.TryGetValue(id, out var view) && IsInstanceValid(view)) view.Apply(view.State); // go back to the confirmed state
        _pending.Clear();
        Rejected?.Invoke(BuildPersistenceManager.RejectionText(code));
    }

    // ---- sending ---------------------------------------------------------------------------------------------

    /// <summary>Sends a command that is not an object edit (lock, unlock).</summary>
    public void ReconnectedForTesting() => Reconnected?.Invoke();
    public bool SendRaw(string commandJson) => Send(commandJson);

    private bool Send(string commandJson)
    {
        if (OperationSink is not null && OperationSink(commandJson)) return true;
        if (_network is not { IsAuthenticated: true }) { Rejected?.Invoke("Entre em uma ilha autenticada antes de construir."); return false; }
        _network.SubmitBuildOperation(commandJson); return true;
    }

    /// <summary>Asks the server to create an object. <paramref name="created"/> runs when the server confirms it (with the real, persistent state).</summary>
    public bool Create(BuildObjectState template, Action<BuildObjectState>? created = null)
    {
        template.Revision = 0;
        if (!Send(BuildPersistenceManager.Command("create", null, 0, BuildPersistenceManager.Payload(template)))) return false;
        _createCallbacks.Enqueue(created ?? (_ => { }));
        return true;
    }

    /// <summary>Asks the server to change an object. The view already shows the local preview; the answer confirms or takes it back.</summary>
    public bool Update(BuildObjectState after)
    {
        if (HeldByOther([after.Id])) return false;
        if (!Send(BuildPersistenceManager.Command("update", after.Id, after.Revision, BuildPersistenceManager.Payload(after)))) return false;
        _pending[after.Id] = _clock; return true;
    }

    /// <summary>Asks the server to change the links of several objects at once (all or nothing): the group and the parent of each, with the revision it was seen at.</summary>
    public bool Structure(IEnumerable<(Guid Id, Guid? Group, Guid? Parent, int Revision)> links)
    {
        var list = links.ToList(); if (list.Count == 0) return false;
        if (HeldByOther(list.Select(l => l.Id))) return false;
        var json = System.Text.Json.JsonSerializer.Serialize(new { operation = "structure", links = list.Select(l => new { objectId = l.Id, groupId = l.Group, parentId = l.Parent, expectedRevision = l.Revision }) }, BuildPersistenceManager.Web);
        if (!Send(json)) return false;
        foreach (var link in list) _pending[link.Id] = _clock; return true;
    }

    /// <summary>Changes who may do what with the objects (rights of the next owner, sharing, group, sale, click action). Only what is in <paramref name="properties"/> changes; the server decides who may.</summary>
    public bool Properties(IEnumerable<Guid> ids, System.Text.Json.Nodes.JsonObject properties)
    {
        var list = ids.Distinct().ToList(); if (list.Count == 0 || properties.Count == 0) return false;
        if (HeldByOther(list)) return false;
        var json = System.Text.Json.JsonSerializer.Serialize(new { operation = "properties", objectIds = list, expectedRevision = 0, properties = System.Text.Json.Nodes.JsonNode.Parse(properties.ToJsonString()) }, BuildPersistenceManager.Web);
        if (!Send(json)) return false;
        foreach (var id in list) _pending[id] = _clock; return true;
    }

    /// <summary>Asks the server to sell the object to the person: the server charges the price that is on it now and answers with who owns it afterwards. The key makes a repeated press the same purchase.</summary>
    public bool Buy(Guid id, long expectedPrice, string idempotencyKey)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { operation = "buy", objectId = id, expectedRevision = 0, properties = new { idempotencyKey, expectedPrice } }, BuildPersistenceManager.Web);
        return Send(json);
    }

    /// <summary>Protects (or unprotects) objects against accidental editing: while protected they cannot be moved, changed, linked or deleted. The server checks who may.</summary>
    public bool Protect(IEnumerable<Guid> ids, bool locked)
    {
        var list = ids.Distinct().ToList(); if (list.Count == 0) return false;
        if (HeldByOther(list)) return false;
        if (!Send(System.Text.Json.JsonSerializer.Serialize(new { operation = "protect", objectIds = list, expectedRevision = 0, locked }, BuildPersistenceManager.Web))) return false;
        foreach (var id in list) _pending[id] = _clock; return true;
    }

    public bool Delete(Guid id)
    {
        if (!_views.TryGetValue(id, out var view)) return false;
        if (HeldByOther([id])) return false;
        if (!Send(BuildPersistenceManager.Command("delete", id, view.State.Revision, null))) return false;
        _pending[id] = _clock; return true;
    }

    // ---- live drags ------------------------------------------------------------------------------------------------

    private void ReadLive(JsonElement root)
    {
        if (!root.TryGetProperty("userId", out var u) || !Guid.TryParse(u.GetString(), out var user) || user == MyUserId || !root.TryGetProperty("poses", out var poses) || poses.ValueKind != JsonValueKind.Array) return;
        try { var list = JsonSerializer.Deserialize<List<LifeVerse.Shared.Building.LivePose>>(poses.GetRawText(), BuildPersistenceManager.Web); if (list is { Count: > 0 }) LiveMoved?.Invoke(user, list); }
        catch (JsonException) { }
    }

    /// <summary>Shows the others where my objects are during a drag. The server only passes it on for objects I hold the lease of; nothing is saved.</summary>
    public bool SendLive(IEnumerable<LifeVerse.Shared.Building.LivePose> poses)
    {
        var list = poses.Take(LifeVerse.Shared.Building.LivePoses.MaxObjects).ToList(); if (list.Count == 0) return false;
        return Send(JsonSerializer.Serialize(new { operation = "live", poses = list }, BuildPersistenceManager.Web));
    }

    // ---- the island's budget ---------------------------------------------------------------------------------

    public readonly record struct BuildLimits(long Objects, long MaxObjects, long Cost, long MaxCost, bool CanBuildHere, string? ParcelName, long ParcelObjects, long ParcelMaxObjects, long ParcelCost, long ParcelMaxCost);

    /// <summary>Reads the usage and the limits of the island (and of the parcel at a point) from the server.</summary>
    public void RefreshLimits(Vector3 at)
    {
        if (_limitsHttp is null || _islandId == Guid.Empty || _session is not { IsAuthenticated: true }) return;
        if (_limitsHttp.GetHttpClientStatus() != Godot.HttpClient.Status.Disconnected) return;
        var app = GetNode<AppManager>("/root/AppManager");
        _limitsHttp.Request($"{app.ApiBaseUrl}/api/v1/worlds/islands/{_islandId}/build-limits?x={at.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}&z={at.Z.ToString(System.Globalization.CultureInfo.InvariantCulture)}", [$"Authorization: Bearer {_session.AccessToken}"]);
    }

    private void OnLimitsLoaded(long result, long responseCode, string[] headers, byte[] body)
    {
        if (responseCode is < 200 or >= 300) return;
        if (ParseLimits(Encoding.UTF8.GetString(body)) is { } limits) LimitsChanged?.Invoke(limits);
    }

    public static BuildLimits? ParseLimits(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json); var r = document.RootElement;
            string? parcelName = null; long po = 0, pmo = 0, pc = 0, pmc = 0;
            if (r.TryGetProperty("parcel", out var parcel) && parcel.ValueKind == JsonValueKind.Object)
            {
                parcelName = parcel.TryGetProperty("name", out var n) ? n.GetString() : null;
                po = parcel.GetProperty("objects").GetInt64(); pmo = parcel.GetProperty("maxObjects").GetInt64(); pc = parcel.GetProperty("cost").GetInt64(); pmc = parcel.GetProperty("maxCost").GetInt64();
            }
            return new BuildLimits(r.GetProperty("objects").GetInt64(), r.GetProperty("maxObjects").GetInt64(), r.GetProperty("cost").GetInt64(), r.GetProperty("maxCost").GetInt64(),
                r.TryGetProperty("canBuildHere", out var can) && can.ValueKind == JsonValueKind.True, parcelName, po, pmo, pc, pmc);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    // ---- test hooks -------------------------------------------------------------------------------------------

    public void ApplyStatesForTesting(IEnumerable<BuildObjectState> states) { foreach (var state in states) Apply(state, announce: false); }
    public void QueueStatesForTesting(IEnumerable<BuildObjectState> states) { foreach (var state in states) _loadQueue.Enqueue(state); }
    public void RejectForTesting(string code) => OnRejected(code);
    public void SetWorldRoot(Node3D root) => _worldRoot = root;
    public void SetIslandForTesting(Guid island) => _islandId = island;
}
