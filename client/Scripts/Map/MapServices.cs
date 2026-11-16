using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts.Map;

/// <summary>
/// The talking to the server that both maps share: ordinary requests (text), and pictures (bytes, with the version they were asked for). One place, so the world map and the mini-map never keep two different ideas of
/// the same island. Tests answer instead of the network.
/// </summary>
public partial class MapApiClient : Node
{
    /// <summary>Tests: (method, path, body) -> (status, body). A negative status is a server that has not answered.</summary>
    public Func<string, string, string?, (int Status, string Body)>? Transport { get; set; }
    /// <summary>Tests: path -> (status, bytes, etag). A negative status is a server that has not answered.</summary>
    public Func<string, (int Status, byte[] Bytes, string? Etag)>? BytesTransport { get; set; }

    public void Send(Godot.HttpClient.Method method, string path, string? body, Action<int, string> done)
    {
        if (Transport is not null) { var (status, text) = Transport(method.ToString().ToUpperInvariant(), path, body); if (status >= 0) done(status, text); return; }
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (app is null || session is null || !session.IsAuthenticated) { done(401, string.Empty); return; }
        var http = new HttpRequest { Timeout = 15 }; AddChild(http);
        http.RequestCompleted += (result, code, _, bytes) => { http.QueueFree(); done(result == (long)HttpRequest.Result.Success ? (int)code : 0, bytes.GetStringFromUtf8()); };
        string[] headers = body is null ? [$"Authorization: Bearer {session.AccessToken}"] : [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"];
        if (http.Request($"{app.ApiBaseUrl}{path}", headers, method, body ?? string.Empty) != Error.Ok) { http.QueueFree(); done(0, string.Empty); }
    }

    /// <summary>Fetches a picture of the API. Returns a handle that cancels the request (its answer is then dropped).</summary>
    public Action FetchBytes(string path, Action<int, byte[], string?> done)
    {
        if (BytesTransport is not null) { var (status, bytes, etag) = BytesTransport(path); if (status >= 0) done(status, bytes, etag); return () => { }; }
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (app is null || session is null || !session.IsAuthenticated) { done(401, [], null); return () => { }; }
        var http = new HttpRequest { Timeout = 20, BodySizeLimit = 2 * 1024 * 1024 }; AddChild(http); var cancelled = false;
        http.RequestCompleted += (result, code, headers, bytes) =>
        {
            http.QueueFree(); if (cancelled) return;
            var etag = headers.Select(h => h.Split(':', 2)).Where(p => p.Length == 2 && p[0].Trim().Equals("ETag", StringComparison.OrdinalIgnoreCase)).Select(p => p[1].Trim().Trim('"')).FirstOrDefault();
            done(result == (long)HttpRequest.Result.Success ? (int)code : 0, bytes, etag);
        };
        if (http.Request($"{app.ApiBaseUrl}{path}", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) { http.QueueFree(); done(0, [], null); return () => { }; }
        return () => { cancelled = true; if (GodotObject.IsInstanceValid(http)) { http.CancelRequest(); http.QueueFree(); } };
    }
}

/// <summary>
/// The pictures of the islands from above. Kept in memory (the most recently used ones, up to a limit) and on disk (so the next session starts with them), each with the version it was made at: the atlas says the
/// current version of every island, and a picture is only fetched again when its version differs. An island with no picture is remembered for a while so it is not asked again and again.
/// </summary>
public partial class MapTileCache : Node
{
    public const int DefaultMemoryTiles = 96, MaxParallel = 3;
    public static readonly TimeSpan NoPictureFor = TimeSpan.FromMinutes(5);
    private sealed record Entry(string Version, Texture2D Texture, long LastUsed);

    public MapApiClient Api { get; set; } = null!;
    /// <summary>Where pictures are kept on disk (tests change it). Empty: no disk cache.</summary>
    public string DiskFolder { get; set; } = "user://map_cache";
    public int MemoryTiles { get; set; } = DefaultMemoryTiles;
    /// <summary>The time the cache reads (tests set it).</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    private readonly Dictionary<Guid, Entry> _memory = [];
    private readonly Dictionary<Guid, DateTimeOffset> _none = [];
    private readonly Dictionary<Guid, List<Action<Texture2D?>>> _waiting = [];
    private readonly Dictionary<Guid, Action> _cancel = [];
    private readonly Queue<(Guid Island, string Version)> _queue = new();
    private long _tick;

    public int CachedCount => _memory.Count;
    public int PendingCount => _waiting.Count;
    public int FetchesStarted { get; private set; }
    public bool Has(Guid island, string version) => _memory.TryGetValue(island, out var e) && e.Version == version;

    /// <summary>The picture of an island at that version: at once when it is kept, otherwise the answer comes later (null when there is none). Asking again while it is on its way adds to the same request.</summary>
    public void Get(Guid island, string version, Action<Texture2D?> done)
    {
        if (_memory.TryGetValue(island, out var entry) && entry.Version == version) { _memory[island] = entry with { LastUsed = ++_tick }; done(entry.Texture); return; }
        if (_none.TryGetValue(island, out var until) && Clock() < until) { done(null); return; }
        if (FromDisk(island, version) is { } stored) { Keep(island, version, stored); done(stored); return; }
        if (_waiting.TryGetValue(island, out var list)) { list.Add(done); return; }
        _waiting[island] = [done]; _queue.Enqueue((island, version)); Pump();
    }

    /// <summary>The pictures that are still on their way and are no longer wanted (the player moved the map) are dropped; their answers are ignored.</summary>
    public void CancelExcept(IReadOnlySet<Guid> wanted)
    {
        foreach (var island in _waiting.Keys.Where(i => !wanted.Contains(i)).ToList()) { if (_cancel.Remove(island, out var cancel)) cancel(); _waiting.Remove(island); }
        var keep = _queue.Where(q => wanted.Contains(q.Island)).ToList(); _queue.Clear(); foreach (var q in keep) _queue.Enqueue(q);
    }

    public void Clear() { foreach (var cancel in _cancel.Values) cancel(); _cancel.Clear(); _waiting.Clear(); _queue.Clear(); _memory.Clear(); _none.Clear(); }

    private void Pump()
    {
        while (_cancel.Count < MaxParallel && _queue.Count > 0)
        {
            var (island, version) = _queue.Dequeue(); if (!_waiting.ContainsKey(island)) continue; FetchesStarted++;
            var cancel = Api.FetchBytes(MapApi.TilePath(island), (status, bytes, etag) => Arrived(island, version, status, bytes, etag)); if (_waiting.ContainsKey(island)) _cancel[island] = cancel;
        }
    }

    private void Arrived(Guid island, string asked, int status, byte[] bytes, string? etag)
    {
        _cancel.Remove(island); if (!_waiting.Remove(island, out var callbacks)) { Pump(); return; }
        Texture2D? texture = null;
        if (status is >= 200 and < 300 && Decode(bytes) is { } decoded) { texture = decoded; var version = string.IsNullOrEmpty(etag) ? asked : etag; Keep(island, version, decoded); ToDisk(island, version, bytes); }
        else if (status == 404) _none[island] = Clock() + NoPictureFor;
        foreach (var callback in callbacks) callback(texture);
        Pump();
    }

    private void Keep(Guid island, string version, Texture2D texture)
    {
        _memory[island] = new Entry(version, texture, ++_tick); _none.Remove(island);
        while (_memory.Count > Math.Max(1, MemoryTiles)) { var oldest = _memory.MinBy(kv => kv.Value.LastUsed).Key; _memory.Remove(oldest); }
    }

    public static Texture2D? Decode(byte[] bytes)
    {
        if (bytes.Length < 16 || bytes[0] != 0x89 || bytes[1] != 0x50) return null; var image = new Image();
        return image.LoadPngFromBuffer(bytes) == Error.Ok && image.GetWidth() is > 0 and <= 2048 && image.GetHeight() is > 0 and <= 2048 ? ImageTexture.CreateFromImage(image) : null;
    }

    private string? PathOf(Guid island, string extension) => string.IsNullOrEmpty(DiskFolder) ? null : $"{DiskFolder}/{island:N}.{extension}";

    private Texture2D? FromDisk(Guid island, string version)
    {
        if (PathOf(island, "png") is not { } png || PathOf(island, "ver") is not { } ver || !Godot.FileAccess.FileExists(png) || !Godot.FileAccess.FileExists(ver)) return null;
        using (var file = Godot.FileAccess.Open(ver, Godot.FileAccess.ModeFlags.Read)) if (file is null || file.GetAsText().Trim() != version) return null;
        using var data = Godot.FileAccess.Open(png, Godot.FileAccess.ModeFlags.Read); return data is null ? null : Decode(data.GetBuffer((long)data.GetLength()));
    }

    private void ToDisk(Guid island, string version, byte[] bytes)
    {
        if (PathOf(island, "png") is not { } png || PathOf(island, "ver") is not { } ver) return;
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(DiskFolder));
        using (var data = Godot.FileAccess.Open(png, Godot.FileAccess.ModeFlags.Write)) data?.StoreBuffer(bytes);
        using (var mark = Godot.FileAccess.Open(ver, Godot.FileAccess.ModeFlags.Write)) mark?.StoreString(version);
    }
}

/// <summary>
/// The atlas of the world: which islands there are for this player, where they stand and how they are. Asked once and kept; asking again (a new search, another part of the map) replaces it. A request that is
/// overtaken by a newer one is dropped. The world map and the mini-map both read this, so they agree.
/// </summary>
public partial class MapAtlasService : Node
{
    public MapApiClient Api { get; set; } = null!;
    public AtlasPage Page { get; private set; } = AtlasPage.Empty;
    public bool Loading { get; private set; }
    public int LastStatus { get; private set; }
    public string LastPath { get; private set; } = string.Empty;
    private int _seq;

    /// <summary>The atlas changed (a new answer arrived).</summary>
    public event Action? Changed;
    /// <summary>The last request failed (status 0: no connection).</summary>
    public event Action<int>? Failed;
    /// <summary>One island of the atlas changed (a favorite, say) without a new answer from the server.</summary>
    public event Action? Updated;

    public void Replace(AtlasIsland island)
    {
        var at = Page.Items.ToList().FindIndex(i => i.Id == island.Id); if (at < 0) return;
        var items = Page.Items.ToList(); items[at] = island; Page = Page with { Items = items }; Updated?.Invoke();
    }

    public AtlasIsland? Find(Guid id) => Page.Items.FirstOrDefault(i => i.Id == id);

    public void Load(string? text = null, string? category = null, IEnumerable<string>? ratings = null, (int MinX, int MaxX, int MinY, int MaxY)? cells = null, int limit = 300)
    {
        var path = MapApi.AtlasPath(text, category, ratings, cells, limit); var seq = ++_seq; Loading = true; LastPath = path;
        Api.Send(Godot.HttpClient.Method.Get, path, null, (status, body) =>
        {
            if (seq != _seq) return; Loading = false; LastStatus = status;
            if (status is < 200 or >= 300) { Failed?.Invoke(status); return; }
            Page = MapApi.ParseAtlas(body); Changed?.Invoke();
        });
    }

    /// <summary>Forgets a request that is still on its way (the map was closed or the search changed).</summary>
    public void Cancel() { _seq++; Loading = false; }
}
