using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Loading;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// Runs the loading of entering an island: after signing in, or when travelling to another one. It does not do the work itself: it watches what the existing
/// systems really do (the API entry ticket, the island server starting, the ENet connection and its authentication, the terrain, the first position the island
/// confirms for the avatar, the objects of the island) and reports each to the <see cref="LoadingManager"/>, which the <see cref="LoadingScreen"/> shows.
/// The person gets the controls back only when the island has confirmed every part, and a failure shows what happened with the actions that can really be taken.
/// </summary>
public partial class WorldEntryCoordinator : Node
{
    public const double WorldReadyTimeoutSeconds = 45, ConnectTimeoutSeconds = 20;
    private double _connecting = -1;

    private NetworkManager _network = null!;
    private LoadingScreen _screen = null!;
    private HttpRequest? _info;
    private Guid _island = Guid.Empty;
    private string _islandName = string.Empty, _category = string.Empty;
    private Guid _previousIsland = Guid.Empty;
    private bool _previousConnected, _snapshotOk, _snapshotMissing, _terrainOk, _spawnOk, _objectsFailed, _awaitingReady;
    private long _objectsDone, _objectsTotal = -1, _assetsDone, _assetsTotal = -1;
    private bool _assetsFailed;
    private double _sinceOnline;
    private bool _online, _tripActive, _tripCanRetry, _arrivalOk;
    private Action? _tripRetry, _tripBack, _tripLeave, _tripCancel;

    public LoadingManager Manager { get; } = new();
    public Action? RetryLocate { get; set; }
    public Action? LeaveToMenu { get; set; }

    public bool Active => Manager.Active || (_screen?.IsOpen ?? false);
    public LoadingScreen Screen => _screen;

    public override void _Ready()
    {
        _network = GetNode<NetworkManager>("/root/NetworkManager");
        _screen = new LoadingScreen(); AddChild(_screen);
        _screen.RetryRequested += Retry; _screen.BackRequested += GoBack; _screen.LeaveRequested += Leave; _screen.CancelRequested += () => _tripCancel?.Invoke();
        _network.ConnectionStateChanged += OnState; _network.WorldProgress += OnProgress;
        Manager.LoadingCompleted += OnCompleted; Manager.LoadingFailed += OnFailed;
    }

    public override void _ExitTree()
    {
        if (_network is null) return;
        _network.ConnectionStateChanged -= OnState; _network.WorldProgress -= OnProgress;
    }

    // ---- starting -------------------------------------------------------------------------------------------

    private static StepDefinition[] EntrySteps() =>
    [
        new("session", "Verificando sua sessão", .5), new("locate", "Localizando sua ilha", .5, "session"), new("server", "Preparando servidor", 2, "locate"), new("connect", "Conectando à ilha", 2, "server"),
        new("avatar", "Carregando seu avatar", 1.5, "connect"), new("env", "Carregando ambiente", 2, "connect"), new("assets", "Carregando recursos da ilha", 3, "connect"), new("objects", "Sincronizando objetos", 2, "connect"), new("ready", "Preparando sua entrada", 1, "avatar", "env", "assets", "objects")
    ];

    private static StepDefinition[] TeleportSteps() =>
    [
        new("locate", "Verificando destino e acesso", 1), new("server", "Preparando ilha", 2, "locate"), new("connect", "Estabelecendo conexão", 2, "server"), new("auth", "Transferindo sessão", 1, "connect"),
        new("env", "Carregando ambiente", 2, "auth"), new("assets", "Carregando recursos da ilha", 3, "auth"), new("objects", "Sincronizando objetos", 2, "auth"), new("avatar", "Posicionando avatar", 1.5, "auth"), new("ready", "Finalizando teletransporte", 1, "avatar", "env", "assets", "objects")
    ];

    private void ResetFacts()
    {
        _snapshotOk = _snapshotMissing = _terrainOk = _spawnOk = _objectsFailed = _awaitingReady = _online = _arrivalOk = false; _objectsDone = 0; _objectsTotal = -1; _assetsDone = 0; _assetsTotal = -1; _assetsFailed = false; _sinceOnline = 0; _connecting = -1;
    }

    /// <summary>Opens the loading for the first entry, while the server is still being asked where the person starts. The island arrives later through <see cref="OnState"/>.</summary>
    public void BeginLocating()
    {
        if (Manager.Active) return;
        ResetFacts(); _island = Guid.Empty; _islandName = string.Empty; _category = string.Empty;
        StartSession(LoadingMode.Entry);
        Manager.Begin("session"); Sync();
        Manager.Begin("locate");
    }

    /// <summary>The server could not say where to go: the loading stops with the reason and offers to try again.</summary>
    public void LocateFailed(string message) { if (Manager.Active) Manager.Fail("locate", message); }

    private void StartSession(LoadingMode mode)
    {
        _previousIsland = _tripActive ? _network.CurrentIslandId : _network.PreviousIslandId; _previousConnected = _previousIsland != Guid.Empty;
        var title = mode == LoadingMode.Entry ? "Entrando no LifeVerse..." : "Viajando para outra ilha...";
        Manager.Start(mode, title, _islandName, mode == LoadingMode.Entry ? EntrySteps() : TeleportSteps());
        _coverUrl = string.Empty; _screen.Open(Manager, _category); _screen.SetTitle(title);
        LockControls(true);
    }

    private void LockControls(bool locked) { if (_network.LocalPlayer is { } player && GodotObject.IsInstanceValid(player)) player.ControlsLocked = locked; }

    // ---- what the existing systems report -------------------------------------------------------------------

    private void OnState(string state, string detail)
    {
        if (state == "requesting_ticket")
        {
            if (!Manager.Active)
            {

                { ResetFacts(); _island = _network.TargetIslandId; _islandName = string.Empty; StartSession(_network.IsAuthenticated ? LoadingMode.Teleport : LoadingMode.Entry); }
            }
            if (_island == Guid.Empty || _island != _network.TargetIslandId) { _island = _network.TargetIslandId; LoadIslandInfo(); }
            Manager.Begin("session"); Sync(); Manager.Complete("locate"); Manager.Begin("server");
            return;
        }
        if (!Manager.Active) return;
        switch (state)
        {
            case "waiting_island": Manager.Begin("server"); break;
            case "connecting": Manager.Complete("server"); Manager.Begin("connect"); if (_connecting < 0) _connecting = 0; break;
            case "authenticating": Manager.Complete("server"); Manager.Begin("connect"); break;
            case "online": _connecting = -1; _online = true; _sinceOnline = 0; Manager.Complete("connect"); Manager.Complete("auth"); Sync(); break;
            case "failed": case "incompatible": _connecting = -1; FailRunning(detail); break;
            case "offline": _connecting = -1; FailRunning("A conexão com a ilha foi perdida."); break;
        }
    }

    private void OnProgress(string key, long done, long total)
    {
        switch (key)
        {
            case "snapshot": if (total == -2) _snapshotMissing = true; else _snapshotOk = true; break;
            case "terrain": _terrainOk = true; break;
            case "spawn": _spawnOk = true; break;
            case "objects": if (total == -2) _objectsFailed = true; else { _objectsDone = done; _objectsTotal = total; } break;
            case "assets": if (total == -2) _assetsFailed = true; else { _assetsDone = done; _assetsTotal = total; } break;
        }
        Sync();
    }

    /// <summary>Applies what is known to the steps whose turn has come. Facts that arrive early (the snapshot comes before the connection finishes) wait here.</summary>
    private void Sync()
    {
        if (!Manager.Active) return;
        var network = GetNode<SessionManager>("/root/SessionManager");
        if (Manager.Step("session") is { State: StepState.Pending or StepState.Running }) { if (network.IsAuthenticated) Manager.Complete("session"); else { Manager.Fail("session", "Sua sessão expirou. Entre novamente."); return; } }
        if (!_online) return;
        var parts = (_snapshotOk || _snapshotMissing ? 1 : 0) + (_terrainOk ? 1 : 0);
        Manager.Report("env", parts, 2); if (_terrainOk && (_snapshotOk || _snapshotMissing)) Manager.Complete("env");
        if (_objectsFailed) { Manager.Fail("objects", "Não foi possível carregar os objetos da ilha."); return; }
        if (_assetsFailed) { Manager.Fail("assets", GetParent()?.GetNodeOrNull<AssetLoadingService>("AssetLoadingService")?.LastError ?? "Não foi possível baixar os recursos da ilha."); return; }
        if (_assetsTotal >= 0) { Manager.Report("assets", _assetsDone, _assetsTotal); if (_assetsDone >= _assetsTotal) Manager.Complete("assets"); } else Manager.Begin("assets");
        if (_objectsTotal >= 0) { Manager.Report("objects", _objectsDone, _objectsTotal); if (_objectsDone >= _objectsTotal) Manager.Complete("objects"); } else Manager.Begin("objects");
        if (_spawnOk) Manager.Complete("avatar"); else Manager.Begin("avatar");
        if (Manager.Step("avatar")!.State == StepState.Completed && Manager.Step("env")!.State == StepState.Completed && Manager.Step("objects")!.State == StepState.Completed && Manager.Step("assets")!.State == StepState.Completed && (!_tripActive || _arrivalOk)) { Manager.Begin("ready"); _awaitingReady = true; }
    }

    public override void _Process(double delta)
    {
        if (!Manager.Active) return;
        if (_awaitingReady) { _awaitingReady = false; Manager.Complete("ready"); return; } // one frame later: the avatar and the objects exist in the scene
        if (_connecting >= 0 && !_online) { _connecting += delta; if (_connecting > ConnectTimeoutSeconds) { _connecting = -1; FailRunning("O servidor da ilha não respondeu dentro do tempo esperado."); return; } }
        if (!_online) return;
        _sinceOnline += delta;
        if (_sinceOnline > WorldReadyTimeoutSeconds && Manager.Current is { } stuck) Manager.Fail(stuck.Id, "A ilha não terminou de enviar o mundo dentro do tempo esperado.");
    }

    private void FailRunning(string message)
    {
        var running = Manager.Steps.FirstOrDefault(s => s.State == StepState.Running) ?? Manager.Current;
        if (running is not null) Manager.Fail(running.Id, message);
    }

    // ---- finishing ------------------------------------------------------------------------------------------

    private void EndTrip() { _tripActive = false; _tripRetry = _tripBack = _tripLeave = _tripCancel = null; }

    private void OnCompleted()
    {
        var teleport = Manager.Mode == LoadingMode.Teleport;
        var welcome = teleport ? (string.IsNullOrEmpty(_islandName) ? "Você chegou à ilha." : $"Você chegou à ilha {_islandName}") : "Bem-vindo ao LifeVerse!";
        _screen.Finish(welcome, () => LockControls(false)); EndTrip();
    }

    private void OnFailed(LoadingStep step, string error)
    {
        var where = string.IsNullOrEmpty(_islandName) ? "à ilha" : $"à ilha {_islandName}";
        var title = step.Id switch { "session" => "Sua sessão não pôde ser verificada.", "locate" => "Não foi possível encontrar a sua ilha.", "objects" => "Não foi possível carregar os objetos.", "assets" => "Não foi possível carregar os recursos da ilha.", _ => $"Não foi possível conectar {where}." };
        var canRetry = _tripActive ? _tripCanRetry : step.Id != "session"; // a session that expired needs a new sign in, not another attempt
        var canGoBack = _tripActive ? _previousIsland != Guid.Empty : Manager.Mode == LoadingMode.Teleport && _previousConnected && _network.IsAuthenticated && _network.PreviousIslandId == _previousIsland; // a trip can go back to where it started; otherwise only while that connection is still open
        _screen.ShowError(title, error, canRetry, canGoBack);
    }

    private void Retry()
    {
        if (!Manager.Retry()) return;
        if (_tripActive) { ResetFacts(); _screen.Open(Manager, _category); _screen.SetTitle(Manager.Title); Manager.Begin("locate"); _tripRetry?.Invoke(); return; }
        ResetFacts(); _screen.Open(Manager, _category); _screen.SetTitle(Manager.Title);
        if (_island == Guid.Empty) { Manager.Begin("session"); Sync(); Manager.Begin("locate"); RetryLocate?.Invoke(); return; }
        Manager.Begin("session"); Sync(); Manager.Complete("locate"); Manager.Begin("server");
        var error = _network.JoinIsland(_island);
        if (error != Error.Ok) Manager.Fail("server", error == Error.Unauthorized ? "Sua sessão não permite entrar agora. Entre novamente." : $"Não foi possível pedir a entrada: {error}.");
    }

    private void GoBack() { if (_tripActive) { var back = _tripBack; EndTrip(); _screen.HideNow(); LockControls(false); back?.Invoke(); return; } Manager.Cancel(); _network.RestorePreviousIsland(); _screen.HideNow(); LockControls(false); }

    private void Leave() { if (_tripActive) { var leave = _tripLeave; EndTrip(); leave?.Invoke(); } Manager.Cancel(); _screen.HideNow(); LockControls(false); LeaveToMenu?.Invoke(); }

    // ---- the destination ------------------------------------------------------------------------------------

    private void LoadIslandInfo()
    {
        if (_island == Guid.Empty) return;
        _info ??= new HttpRequest { Timeout = 10 };
        if (_info.GetParent() is null) { AddChild(_info); _info.RequestCompleted += OnInfo; }
        if (_info.GetHttpClientStatus() != Godot.HttpClient.Status.Disconnected) return;
        _info.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/worlds/islands/{_island}");
    }

    private void OnInfo(long result, long code, string[] headers, byte[] body)
    {
        if (code is < 200 or >= 300) return;
        try
        {
            using var document = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(body)); var root = document.RootElement;
            _islandName = root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? string.Empty : string.Empty;
            _category = root.TryGetProperty("category", out var category) && category.ValueKind == JsonValueKind.String ? category.GetString() ?? string.Empty : string.Empty;
            Manager.SetDestination(_islandName); _screen.SetIsland(_islandName, _category);
            if (root.TryGetProperty("coverImageUrl", out var cover) && cover.ValueKind == JsonValueKind.String) LoadCover(cover.GetString() ?? string.Empty);
        }
        catch (JsonException) { }
    }

    // ---- test hooks -----------------------------------------------------------------------------------------

    public void BeginForTesting(LoadingMode mode, string island) { ResetFacts(); _islandName = island; StartSession(mode); Manager.Begin("session"); Sync(); }
    public void WaitForTesting(double seconds) { _sinceOnline += seconds; _Process(0); }
    public void ConnectWaitForTesting(double seconds) { _Process(seconds); }
    public void StateForTesting(string state, string detail) => OnState(state, detail);
    public void ProgressForTesting(string key, long done, long total) => OnProgress(key, done, total);
    public void SetIslandForTesting(Guid island, string name) { _island = island; _islandName = name; Manager.SetDestination(name); _screen.SetIsland(name, null); }
    public void RetryForTesting() => Retry();
    public void LeaveForTesting() => Leave();
    public void BackForTesting() => GoBack();
}

public partial class WorldEntryCoordinator
{
    public bool TripActive => _tripActive;

    /// <summary>
    /// Opens the loading of a trip to another island. The trip itself (reserve, ticket, connect) is run by the <see cref="TeleportCoordinator"/>, which reports each step here;
    /// what the island does after the connection (terrain, objects, avatar) is followed like in any entry, and the trip is only complete when the destination confirmed the arrival.
    /// </summary>
    public void BeginTrip(Guid island, string name, Action retry, Action back, Action leave, Action cancel)
    {
        ResetFacts(); _island = island; _islandName = name;
        _tripActive = true; _tripCanRetry = true; _tripRetry = retry; _tripBack = back; _tripLeave = leave; _tripCancel = cancel;
        StartSession(LoadingMode.Teleport);
        Manager.Begin("locate"); _screen.SetCancelVisible(true);
        if (string.IsNullOrEmpty(name)) LoadIslandInfo();
    }

    /// <summary>The destination accepted the trip (it exists, the person may enter, there is room).</summary>
    public void TripPrepared() { Manager.Complete("locate"); Manager.Begin("server"); }

    /// <summary>The destination server is up and the ticket is in hand: from here the connection to the old island is given up, so cancelling is no longer offered.</summary>
    public void TripTicketReady() { Manager.Complete("server"); _screen.SetCancelVisible(false); }

    /// <summary>The destination (not the client) said the person arrived.</summary>
    public void TripArrivalConfirmed() { _arrivalOk = true; Sync(); }

    public void TripFailed(string stepId, string message, bool canRetry)
    {
        _tripCanRetry = canRetry; _screen.SetCancelVisible(false);
        if (Manager.Active) Manager.Fail(stepId, message);
    }

    public void TripCancelledByUser() { Manager.Cancel(); EndTrip(); _screen.HideNow(); LockControls(false); }
    public bool ArrivalOkForTesting => _arrivalOk;
}

public partial class WorldEntryCoordinator
{
    private HttpRequest? _coverHttp;
    private string _coverUrl = string.Empty;
    public const int CoverMaxAgeDays = 7;

    /// <summary>The island's cover image: from disk when it was downloaded in the last days, else downloaded (a link outside the API is fetched without the session), and shown as the preview and the backdrop. Without a cover the LifeVerse logo stays.</summary>
    private void LoadCover(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url == _coverUrl) return;
        _coverUrl = url;
        var path = $"user://covers/{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)))[..32].ToLowerInvariant()}.bin";
        if (Godot.FileAccess.FileExists(path) && (Time.GetUnixTimeFromSystem() - Godot.FileAccess.GetModifiedTime(path)) < CoverMaxAgeDays * 86400.0 && ShowCover(Godot.FileAccess.GetFileAsBytes(path))) return;
        var absolute = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (!absolute && !url.StartsWith('/')) return;
        _coverHttp ??= new HttpRequest { Timeout = 15, BodySizeLimit = 8 * 1024 * 1024 };
        if (_coverHttp.GetParent() is null) AddChild(_coverHttp);
        if (_coverHttp.GetHttpClientStatus() != Godot.HttpClient.Status.Disconnected) return;
        var asked = url;
        void Done(long result, long code, string[] headers, byte[] body)
        {
            _coverHttp!.RequestCompleted -= Done;
            if (asked != _coverUrl || code is < 200 or >= 300 || !ShowCover(body)) return;
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://covers"));
            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write); file?.StoreBuffer(body);
        }
        _coverHttp.RequestCompleted += Done;
        var headersToSend = absolute ? Array.Empty<string>() : new[] { $"Authorization: Bearer {GetNode<SessionManager>("/root/SessionManager").AccessToken}" };
        if (_coverHttp.Request(absolute ? url : GetNode<AppManager>("/root/AppManager").ApiBaseUrl + url, headersToSend) != Error.Ok) _coverHttp.RequestCompleted -= Done;
    }

    private bool ShowCover(byte[] bytes)
    {
        if (TextureCache.Decode(bytes) is not { } texture) return false;
        _screen.SetIsland(_islandName, _category, texture); return true;
    }

    public string CoverUrlForTesting => _coverUrl;
    public bool ShowCoverForTesting(byte[] bytes) => ShowCover(bytes);
}
