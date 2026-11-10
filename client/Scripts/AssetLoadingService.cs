using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Loading;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// Gets the assets an island needs (3D models and images) before the person enters, and keeps them on disk. The API lists them (<c>assets/manifest</c>: id, hash, size and
/// how far from the arrival point each is used); the ones around the arrival point load first and report real progress in bytes, the rest keep loading after the entry.
/// Everything is stored by content hash in <c>user://assets</c>: a file that is already there and still hashes to the announced value is not downloaded again, a file that
/// does not (damaged, changed) is replaced, and a download that arrives with the wrong hash is refused and tried once more. Loaded files are handed to the texture and model
/// caches one per frame, so the game does not freeze while a large island arrives.
/// </summary>
public partial class AssetLoadingService : Node
{
    public const int Parallel = 3, BackgroundParallel = 2;
    public const long MaxCacheBytes = 512L * 1024 * 1024;

    private readonly Dictionary<Guid, AssetEntry> _known = [];
    private readonly List<(HttpRequest Request, long Size)> _active = [];
    private NetworkManager _network = null!;
    private Guid _island;
    private long _generation, _doneBytes, _totalBytes, _lastReported = -1;
    private bool _reporting;

    public string CacheDirectory { get; set; } = "user://assets";
    /// <summary>Test seam: replaces the HTTP calls. Receives the path (with query) and answers with a status and the bytes.</summary>
    public Func<string, Task<(long Status, byte[] Body)>>? Transport { get; set; }
    public int CacheHits { get; private set; }
    public int Downloads { get; private set; }
    public int Refused { get; private set; }
    public long DownloadedBytes { get; private set; }
    public string? LastError { get; private set; }
    public bool BackgroundRunning { get; private set; }
    public int FedToCaches { get; private set; }
    public AssetPlanResult? Plan { get; private set; }

    public override void _Ready()
    {
        _network = GetNode<NetworkManager>("/root/NetworkManager");
        _network.ConnectionStateChanged += OnState;
        Prune();
    }

    public override void _ExitTree() { if (_network is not null) _network.ConnectionStateChanged -= OnState; _generation++; }

    private void OnState(string state, string detail)
    {
        if (state == "online" && _network.CurrentIslandId != Guid.Empty) _ = StartAsync(_network.CurrentIslandId);
        else if (state is "switching" or "offline" or "failed") { _generation++; _reporting = false; }
    }

    // ---- the cache on disk ----------------------------------------------------------------------------------

    private string PathFor(string hash) => $"{CacheDirectory}/{hash.ToLowerInvariant()}.bin";

    private bool TryRead(string hash, out byte[] bytes)
    {
        bytes = [];
        var path = PathFor(hash);
        if (!Godot.FileAccess.FileExists(path)) return false;
        bytes = Godot.FileAccess.GetFileAsBytes(path);
        if (bytes.Length > 0 && AssetPlanner.HashMatches(hash, bytes)) return true;
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path)); // damaged or replaced: it is downloaded again
        bytes = [];
        return false;
    }

    private void Write(string hash, byte[] bytes)
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(CacheDirectory));
        var path = PathFor(hash); var temp = path + ".part";
        using (var file = Godot.FileAccess.Open(temp, Godot.FileAccess.ModeFlags.Write)) { if (file is null) return; file.StoreBuffer(bytes); }
        DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(temp), ProjectSettings.GlobalizePath(path)); // a half-written file never has the final name
    }

    /// <summary>The bytes of an asset this island announced, when they are on disk and still right (for the caches that fetch on demand).</summary>
    public byte[]? TryReadCached(Guid assetId) => _known.TryGetValue(assetId, out var entry) && TryRead(entry.Hash, out var bytes) ? bytes : null;

    /// <summary>Keeps what an on-demand download brought, if it is something this island announced and the bytes are right.</summary>
    public void StoreIfKnown(Guid assetId, byte[] bytes)
    {
        if (_known.TryGetValue(assetId, out var entry) && AssetPlanner.HashMatches(entry.Hash, bytes) && !Godot.FileAccess.FileExists(PathFor(entry.Hash))) Write(entry.Hash, bytes);
    }

    /// <summary>Deletes the oldest files when the cache is over its limit.</summary>
    private void Prune()
    {
        try
        {
            using var dir = DirAccess.Open(CacheDirectory); if (dir is null) return;
            var files = new List<(string Name, long Size, ulong Time)>();
            foreach (var name in dir.GetFiles()) { var full = $"{CacheDirectory}/{name}"; files.Add((name, (long)Godot.FileAccess.GetFileAsBytes(full).Length, Godot.FileAccess.GetModifiedTime(full))); }
            var total = files.Sum(f => f.Size); if (total <= MaxCacheBytes) return;
            foreach (var file in files.OrderBy(f => f.Time)) { dir.Remove(file.Name); total -= file.Size; if (total <= MaxCacheBytes * .8) break; }
        }
        catch (Exception exception) { GD.PushWarning($"event=asset_cache_prune_failed error={exception.GetType().Name}"); }
    }

    // ---- loading --------------------------------------------------------------------------------------------

    private async Task<(long Status, byte[] Body)> Get(string path, long size)
    {
        if (Transport is not null) return await Transport(path);
        var session = GetNode<SessionManager>("/root/SessionManager"); var app = GetNode<AppManager>("/root/AppManager");
        var request = new HttpRequest { Timeout = 60, BodySizeLimit = 130 * 1024 * 1024 }; AddChild(request);
        var entry = (request, size); _active.Add(entry);
        try
        {
            if (request.Request(app.ApiBaseUrl + path, [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) return (0, []);
            var done = await ToSignal(request, HttpRequest.SignalName.RequestCompleted);
            return ((long)done[0].AsInt64() == (long)HttpRequest.Result.Success ? done[1].AsInt64() : 0, done[3].AsByteArray());
        }
        finally { _active.Remove(entry); request.QueueFree(); }
    }

    public async Task StartAsync(Guid island)
    {
        var generation = ++_generation; _island = island; _doneBytes = 0; _totalBytes = 0; _lastReported = -1; _reporting = false; BackgroundRunning = false; LastError = null;
        CacheHits = 0; Downloads = 0; Refused = 0; DownloadedBytes = 0; FedToCaches = 0; _known.Clear(); Plan = null;
        var x = _network.ArrivalHint is { } hint ? $"?x={hint.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}&z={hint.Z.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : string.Empty;
        var (status, body) = await Get($"/api/v1/worlds/islands/{island}/assets/manifest{x}", 0);
        if (generation != _generation) return;
        if (status == 404) { _network.ReportWorldProgress("assets", 0, 0); return; } // an older server without a manifest: assets load when an object needs them, as before
        List<AssetEntry> entries;
        try { entries = ParseManifest(Encoding.UTF8.GetString(body)); }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or InvalidOperationException) { status = 0; entries = []; }
        // without the list the island can still be entered: the assets load when an object needs them, as before
        if (status is < 200 or >= 300) { LastError = "Não foi possível obter a lista de recursos da ilha."; GD.PushWarning($"event=asset_manifest_failed status={status}"); _network.ReportWorldProgress("assets", 0, 0); return; }
        foreach (var entry in entries) _known[entry.AssetId] = entry;
        Plan = AssetPlanner.Plan(entries); _totalBytes = Plan.EssentialBytes;
        _reporting = true; _network.ReportWorldProgress("assets", 0, _totalBytes);
        if (!await RunAsync(Plan.Essential, Parallel, generation, essential: true)) return;
        if (generation != _generation) return;
        _reporting = false; _network.ReportWorldProgress("assets", _totalBytes, _totalBytes);
        if (Plan.Deferred.Count == 0) return;
        BackgroundRunning = true; _ = BackgroundAsync(Plan.Deferred, generation); // the entry does not wait for the far assets
    }

    private async Task BackgroundAsync(IReadOnlyList<AssetEntry> deferred, long generation)
    {
        await RunAsync(deferred, BackgroundParallel, generation, essential: false);
        if (generation == _generation) BackgroundRunning = false;
    }

    public static List<AssetEntry> ParseManifest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var list = new List<AssetEntry>();
        foreach (var item in document.RootElement.GetProperty("assets").EnumerateArray())
            list.Add(new AssetEntry(Guid.Parse(item.GetProperty("assetId").GetString()!), item.GetProperty("kind").GetString() == "model" ? AssetKind.Model : AssetKind.Texture, item.GetProperty("hash").GetString() ?? string.Empty,
                item.GetProperty("size").GetInt64(), item.GetProperty("mime").GetString() ?? string.Empty, item.GetProperty("distance").GetDouble()));
        return list;
    }

    private async Task<bool> RunAsync(IReadOnlyList<AssetEntry> entries, int workers, long generation, bool essential)
    {
        var next = 0; var failed = false;
        async Task Worker()
        {
            while (!failed && generation == _generation)
            {
                var index = next++; if (index >= entries.Count) return;
                var entry = entries[index];
                var bytes = await EnsureAsync(entry, generation);
                if (generation != _generation) return;
                if (bytes is null)
                {
                    if (!essential) continue; // a far asset that cannot be fetched now will be fetched when an object needs it
                    failed = true; LastError = $"Não foi possível baixar {(entry.Kind == AssetKind.Model ? "um modelo 3D" : "uma imagem")} da ilha ({entry.AssetId.ToString()[..8]}).";
                    _reporting = false; _network.ReportWorldProgress("assets", _doneBytes, -2); return;
                }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); // one asset handed over per frame
                if (generation != _generation) return;
                Feed(entry, bytes);
                if (essential) { _doneBytes += entry.Size; Report(); }
            }
        }
        await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => Worker()));
        return !failed && generation == _generation;
    }

    /// <summary>The verified bytes of one asset: from disk when they are there and right, else downloaded (and checked against the hash, with one more try).</summary>
    private async Task<byte[]?> EnsureAsync(AssetEntry entry, long generation)
    {
        if (TryRead(entry.Hash, out var cached)) { CacheHits++; return cached; }
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (status, body) = await Get($"/api/v1/worlds/islands/{_island}/assets/{entry.AssetId}/file", entry.Size);
            if (generation != _generation) return null;
            if (status is < 200 or >= 300 || body.Length == 0) continue;
            if (!AssetPlanner.HashMatches(entry.Hash, body)) { Refused++; continue; } // damaged on the way, or not what the island announced
            Downloads++; DownloadedBytes += body.Length; Write(entry.Hash, body);
            return body;
        }
        return null;
    }

    private void Feed(AssetEntry entry, byte[] bytes)
    {
        var parent = GetParent(); if (parent is null) return;
        if (entry.Kind == AssetKind.Texture && parent.GetNodeOrNull<TextureCache>("TextureCache") is { } textures) { textures.Preload(entry.AssetId, bytes); FedToCaches++; }
        else if (entry.Kind == AssetKind.Model && parent.GetNodeOrNull<ModelCache>("ModelCache") is { } models) { models.Preload(entry.AssetId, bytes); FedToCaches++; }
    }

    private void Report()
    {
        if (!_reporting) return;
        var partial = _active.Sum(a => Math.Min(a.Request.GetDownloadedBytes(), a.Size));
        var value = Math.Min(_totalBytes, _doneBytes + partial);
        if (value == _lastReported) return;
        _lastReported = value; _network.ReportWorldProgress("assets", value, _totalBytes);
    }

    public override void _Process(double delta) { if (_reporting && _active.Count > 0) Report(); }

    /// <summary>Test hook: offers wrong bytes for an announced asset and says whether they were stored.</summary>
    public bool StoreIfKnownTest(Guid assetId) { var before = _known.TryGetValue(assetId, out var entry) && Godot.FileAccess.FileExists(PathFor(entry.Hash)); StoreIfKnown(assetId, Encoding.UTF8.GetBytes("outra coisa")); return _known.TryGetValue(assetId, out var after) && !before && Godot.FileAccess.FileExists(PathFor(after.Hash)); }

    public void SetKnownForTesting(IEnumerable<AssetEntry> entries) { foreach (var entry in entries) _known[entry.AssetId] = entry; }
}
