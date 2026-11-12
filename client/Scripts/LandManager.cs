using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Land;

namespace LifeVerse.Client.Scripts;

/// One parcel of the island as the server describes it.
public sealed class LandParcelData
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string OwnerName { get; init; } = string.Empty;
    public string State { get; init; } = "owned";
    public bool Mine { get; init; }
    public double Area { get; init; }
    public int CellCount { get; init; }
    public int MaxObjects { get; init; }
    public int MaxCost { get; init; }
    public int Objects { get; init; }
    public int Cost { get; init; }
    public int Revision { get; init; }
    public HashSet<long> Cells { get; init; } = [];

    // for sale
    public long? SalePrice { get; init; }
    public bool SaleIncludesObjects { get; init; } = true;
    public string SaleDescription { get; init; } = string.Empty;
    // offered for rent
    public long? RentPrice { get; init; }
    public int RentPeriodDays { get; init; }
    public int RentMinPeriods { get; init; }
    public int RentMaxPeriods { get; init; }
    public long RentDeposit { get; init; }
    // the contract running now
    public Guid? RentalId { get; init; }
    public string TenantName { get; init; } = string.Empty;
    public bool TenantIsMe { get; init; }
    public string RentStatus { get; init; } = string.Empty;
    public DateTimeOffset? RentExpiresAt { get; init; }
    public long RentNextCharge { get; init; }
    public bool RentAutoRenew { get; init; }
    public string AccessMode { get; init; } = "public";
    public string BuildMode { get; init; } = "owner";
    public bool AllowScripts { get; init; }
    public int? AutoReturnMinutes { get; init; }
    public int OthersObjects { get; init; }
    public int OthersCost { get; init; }
    public int Builders { get; init; }
    public bool CanEditTerrain { get; init; }

    // how the parcel presents itself
    public double? ArrivalX { get; init; }
    public double? ArrivalZ { get; init; }
    public float ArrivalYaw { get; init; }
    public string TeleportMode { get; init; } = "free";
    public ParcelEnvironment Environment { get; init; } = new();
    public string MusicUrl { get; private set; } = string.Empty;
    public string MusicName { get; private set; } = string.Empty;
    public float MusicVolume { get; private set; } = .6f;
    public bool MusicAutoplay { get; private set; } = true;

    /// Reads { musicUrl, volume, autoplay, name } (or {}); anything unreadable means no music.
    public LandParcelData WithMedia(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("musicUrl", out var url) || url.ValueKind != JsonValueKind.String) return this;
            MusicUrl = url.GetString() ?? string.Empty;
            if (root.TryGetProperty("volume", out var v) && v.TryGetDouble(out var volume)) MusicVolume = (float)Math.Clamp(volume, 0, 1);
            if (root.TryGetProperty("autoplay", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False) MusicAutoplay = a.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) MusicName = n.GetString() ?? string.Empty;
        }
        catch (JsonException) { }
        return this;
    }

    public bool HasArrival => ArrivalX is not null && ArrivalZ is not null;
    public bool EnvironmentAllowed { get; init; } = true;
    /// <summary>Whether proximity voice works on this parcel (the island server drops it when not).</summary>
    public bool AllowVoice { get; init; } = true;
}

/// The territorial grid in the world: knows the parcels of the island (cells of 4 x 4 m, owners, how much is built), draws them over the
/// ground, lets a person drag over the ground to select an area, and says what is there. Everything shown comes from the server; the
/// only thing that changes the land from here is "Criar terreno", which the server checks again.
public partial class LandManager : Node3D
{
    private const int WindowCells = 14;
    private NetworkManager _network = null!;
    private SessionManager _session = null!;
    private IslandTerrain? _terrain;
    private HttpRequest? _http;
    private MeshInstance3D _overlay = null!;
    private readonly List<LandParcelData> _parcels = [];
    private readonly Dictionary<long, LandParcelData> _owners = [];
    private HashSet<long> _selection = [];
    private (double X, double Z)? _anchor;
    private bool _dragging, _active;
    private int _islandSize = 256;
    private long _version;
    private Vector2 _focus;
    private double _sinceCheck;

    /// Something to tell the person (a toast).
    /// <summary>Opens "Sobre este Terreno" for a parcel (set by the main UI); without it the older selection window opens.</summary>
    public Action<Guid>? ParcelAbout { get; set; }
    public event Action<string>? Said;
    /// The parcels or the selection changed.
    public event Action? Changed;
    /// A purchase, rent or refund changed the balance of coins: the top bar should ask for it again.
    public event Action? WalletChanged;
    /// Tests set this to answer requests without a network: (method, path, body) -> (status, body).
    public Func<Godot.HttpClient.Method, string, string?, (int Status, string Body)>? Transport { get; set; }

    public bool Active => _active;
    public IReadOnlyList<LandParcelData> Parcels => _parcels;
    public IReadOnlySet<long> Selection => _selection;
    public int IslandSize => _islandSize;
    public long Version => _version;

    public override void _Ready()
    {
        _network = GetNode<NetworkManager>("/root/NetworkManager"); _session = GetNode<SessionManager>("/root/SessionManager");
        _terrain = GetParent().GetNodeOrNull<IslandTerrain>("IslandTerrain");
        _network.ConnectionStateChanged += OnConnectionState;
        _overlay = new MeshInstance3D { Name = "LandOverlay", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true, Visible = false, ExtraCullMargin = 4000 };
        AddChild(_overlay);
        Presence = new LandPresence { Name = "Presence" }; AddChild(Presence);
        BuildInfoWindow();
        _network.TeleportResult += OnTeleportResult;
    }

    /// The parcel under the avatar: its sky, weather and music.
    public LandPresence Presence { get; private set; } = null!;

    public override void _ExitTree() { if (_network is not null) { _network.ConnectionStateChanged -= OnConnectionState; _network.TeleportResult -= OnTeleportResult; } }

    private void OnConnectionState(string state, string detail) { if (state == "online") Load(); else if (state is "offline" or "failed" or "switching") { Apply("{\"parcels\":[]}"); ClearSelection(); } }

    private void Say(string text) => Said?.Invoke(text);

    // ---- talking to the API ----------------------------------------------------------------------------------

    private void Call(Godot.HttpClient.Method method, string path, string? body, Action<int, string> done)
    {
        if (Transport is not null) { var (status, text) = Transport(method, path, body); done(status, text); return; }
        var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (app is null || !_session.IsAuthenticated || _network.CurrentIslandId == Guid.Empty) return;
        var http = new HttpRequest { Timeout = 15 }; AddChild(http);
        http.RequestCompleted += (result, code, _, bytes) => { http.QueueFree(); done(result == (long)HttpRequest.Result.Success ? (int)code : 0, bytes.GetStringFromUtf8()); };
        var headers = new List<string> { $"Authorization: Bearer {_session.AccessToken}" };
        if (body is not null) headers.Add("Content-Type: application/json");
        if (http.Request($"{app.ApiBaseUrl}{(path.StartsWith("/api/", StringComparison.Ordinal) ? path : $"/api/v1/worlds/islands/{_network.CurrentIslandId}/land{path}")}", [.. headers], method, body ?? string.Empty) != Error.Ok) http.QueueFree();
    }

    /// Asks the server for the parcels of the island.
    public void Load() => Call(Godot.HttpClient.Method.Get, string.Empty, null, (status, body) => { if (status is >= 200 and < 300) Apply(body); });

    /// Reads the server's answer ({ size, version, parcels: [...] }) and redraws.
    public void Apply(string json)
    {
        _parcels.Clear(); _owners.Clear();
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            _islandSize = root.TryGetProperty("size", out var size) && size.TryGetInt32(out var s) ? s : _islandSize;
            _version = root.TryGetProperty("version", out var version) && version.TryGetInt64(out var v) ? v : 0;
            if (root.TryGetProperty("parcels", out var list))
                foreach (var item in list.EnumerateArray())
                {
                    var runs = item.GetProperty("runs").EnumerateArray().Select(r => r.EnumerateArray().Select(n => n.GetInt32()).ToArray()).ToList();
                    string Text(string name) => item.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : string.Empty;
                    int Number(string name) => item.TryGetProperty(name, out var p) && p.TryGetInt32(out var n) ? n : 0;
                    long? Money(string name) => item.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n) ? n : null;
                    double? Real(string name) => item.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var n) ? n : null;
                    bool Flag(string name, bool fallback = false) => item.TryGetProperty(name, out var p) ? p.ValueKind == JsonValueKind.True || (fallback && p.ValueKind != JsonValueKind.False) : fallback;
                    var parcel = new LandParcelData
                    {
                        Id = Guid.TryParse(Text("id"), out var id) ? id : Guid.Empty, Name = Text("name"), Description = Text("description"), OwnerName = Text("ownerName"), State = Text("state"),
                        Mine = item.TryGetProperty("mine", out var mine) && mine.ValueKind == JsonValueKind.True, Area = item.TryGetProperty("area", out var area) && area.TryGetDouble(out var a) ? a : 0,
                        CellCount = Number("cells"), MaxObjects = Number("maxObjects"), MaxCost = Number("maxCost"), Objects = Number("objects"), Cost = Number("cost"), Revision = Number("revision"), Cells = LandMath.FromRuns(runs),
                        SalePrice = Money("salePrice"), SaleIncludesObjects = Flag("saleIncludesObjects", true), SaleDescription = Text("saleDescription"),
                        RentPrice = Money("rentPrice"), RentPeriodDays = Number("rentPeriodDays"), RentMinPeriods = Number("rentMinPeriods"), RentMaxPeriods = Number("rentMaxPeriods"), RentDeposit = Money("rentDeposit") ?? 0,
                        RentalId = Guid.TryParse(Text("rentalId"), out var rentalId) ? rentalId : null, TenantName = Text("tenantName"), TenantIsMe = Flag("tenantIsMe"), RentStatus = Text("rentStatus"),
                        RentExpiresAt = DateTimeOffset.TryParse(Text("rentExpiresAt"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var expires) ? expires : null,
                        RentNextCharge = Money("rentNextCharge") ?? 0, RentAutoRenew = Flag("rentAutoRenew"), AccessMode = Text("accessMode") is { Length: > 0 } mode ? mode : "public",
                        BuildMode = Text("buildMode") is { Length: > 0 } buildMode ? buildMode : "owner", AllowScripts = Flag("allowScripts"), AutoReturnMinutes = item.TryGetProperty("autoReturnMinutes", out var autoReturn) && autoReturn.ValueKind == JsonValueKind.Number ? autoReturn.GetInt32() : null,
                        OthersObjects = Number("othersObjects"), OthersCost = Number("othersCost"), Builders = Number("builders"), CanEditTerrain = Flag("canEditTerrain"),
                        ArrivalX = Real("arrivalX"), ArrivalZ = Real("arrivalZ"), ArrivalYaw = (float)(Real("arrivalYaw") ?? 0), TeleportMode = Text("teleportMode") is { Length: > 0 } tp ? tp : "free", EnvironmentAllowed = Flag("environmentAllowed", true), AllowVoice = Flag("allowVoice", true),
                        Environment = ParcelEnvironment.Parse(Text("environment"))
                    }.WithMedia(Text("media"));
                    _parcels.Add(parcel);
                    foreach (var cell in parcel.Cells) _owners[cell] = parcel;
                }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { _parcels.Clear(); _owners.Clear(); Say("Não foi possível ler os terrenos da ilha."); }
        _terrainRights = LifeVerse.Shared.Land.LandTerrainRights.FromCells(_parcels.Where(p => p.CanEditTerrain).Select(p => (IEnumerable<long>)p.Cells), _session.UserId);
        RefreshAfterChange();
    }

    private LifeVerse.Shared.Land.LandTerrainRights _terrainRights = LifeVerse.Shared.Land.LandTerrainRights.Empty;
    /// The ground this person may sculpt (the parcels they own, rent or build on); island editors may sculpt anywhere and need no mask.
    public LifeVerse.Shared.Land.LandTerrainRights TerrainRights => _terrainRights;
    public bool HasTerrainRights => _terrainRights.Cells > 0;

    public LandParcelData? ParcelAtCell(int cx, int cz) => _owners.GetValueOrDefault(LandMath.Key(cx, cz));
    public LandParcelData? ParcelAt(double x, double z) => ParcelAtCell(LandMath.CellOf(x), LandMath.CellOf(z));

    // ---- the selection tool ----------------------------------------------------------------------------------

    public void SetActive(bool active)
    {
        _active = active; _dragging = false; _anchor = null;
        if (active) { Load(); Say("Terrenos: arraste sobre o chão para selecionar uma área. Esc sai."); }
        else if (!InfoOpen) _selection = [];
        RefreshAfterChange();
    }

    private Camera3D? Camera => GetViewport().GetCamera3D();

    private (double X, double Z)? GroundUnderMouse()
    {
        if (Camera is not { } camera || _terrain is null) return null;
        return _terrain.Raycast(camera, GetViewport().GetMousePosition()) is { } hit ? (hit.X, hit.Z) : null;
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (!_active) return;
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape }) { SetActive(false); GetViewport().SetInputAsHandled(); return; }
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed && GroundUnderMouse() is { } start) { _anchor = start; _dragging = true; DragTo(start); GetViewport().SetInputAsHandled(); }
            else if (!button.Pressed && _dragging) { _dragging = false; FinishSelection(); GetViewport().SetInputAsHandled(); }
        }
        else if (input is InputEventMouseMotion && _dragging && GroundUnderMouse() is { } point) DragTo(point);
    }

    private void DragTo((double X, double Z) point)
    {
        if (_anchor is not { } anchor) return;
        var cells = LandMath.Between(anchor.X, anchor.Z, point.X, point.Z);
        cells.RemoveWhere(k => { var (cx, cz) = LandMath.Unkey(k); return !LandMath.InsideIsland(cx, cz, _islandSize); });
        _selection = cells; RefreshAfterChange();
    }

    /// Tests: drags a selection over the ground between two points, as the mouse would.
    public void DragForTesting(double ax, double az, double bx, double bz) { _anchor = (ax, az); DragTo((bx, bz)); FinishSelection(); _anchor = null; }

    private void FinishSelection() { if (_selection.Count > 0) ShowInfo(); }

    /// Selects these cells (and shows what is there).
    public void SelectCells(HashSet<long> cells, bool showInfo = true) { _selection = cells; RefreshAfterChange(); if (showInfo && cells.Count > 0) ShowInfo(); }

    public void ClearSelection() { _selection = []; RefreshAfterChange(); }

    /// "Sobre o terreno" for a point on the ground: the whole parcel there, or the single cell when nobody owns it.
    public void AboutAt(Vector3 point)
    {
        var parcel = ParcelAt(point.X, point.Z);
        if (parcel is not null && ParcelAbout is not null) { ParcelAbout(parcel.Id); return; } // the window about one parcel, when the game has it
        if (parcel is not null) { SelectCells([.. parcel.Cells]); return; }
        var cell = new HashSet<long> { LandMath.Key(LandMath.CellOf(point.X), LandMath.CellOf(point.Z)) };
        SelectCells(cell);
    }

    // ---- what the selection covers ---------------------------------------------------------------------------

    /// The parcels the selection touches and how many of its cells nobody owns.
    public (List<LandParcelData> Parcels, int Free) Covered()
    {
        var parcels = new List<LandParcelData>(); var free = 0;
        foreach (var cell in _selection)
        {
            if (_owners.TryGetValue(cell, out var parcel)) { if (!parcels.Contains(parcel)) parcels.Add(parcel); }
            else free++;
        }
        return (parcels, free);
    }

    // ---- creating a parcel -----------------------------------------------------------------------------------

    /// Marks the selected free cells off as a new parcel. Only island owners and administrators are allowed; the server decides.
    public void ClaimSelection(string name)
    {
        var (parcels, free) = Covered();
        if (_selection.Count == 0) { Say("Selecione uma área primeiro."); return; }
        if (parcels.Count > 0 || free == 0) { Say("A área selecionada já tem dono. Escolha só terreno livre."); return; }
        if (string.IsNullOrWhiteSpace(name)) { Say("Dê um nome ao terreno."); return; }
        var cells = _selection.Select(k => { var (cx, cz) = LandMath.Unkey(k); return new[] { cx, cz }; }).ToArray();
        Call(Godot.HttpClient.Method.Post, "/claim", JsonSerializer.Serialize(new { name = name.Trim(), cells }), (status, body) =>
        {
            if (status is >= 200 and < 300)
            {
                Say($"Terreno “{name.Trim()}” criado."); var wanted = _selection;
                Call(Godot.HttpClient.Method.Get, string.Empty, null, (s, b) => { if (s is >= 200 and < 300) { Apply(b); var created = _parcels.FirstOrDefault(p => p.Name == name.Trim()); if (created is not null) SelectCells([.. created.Cells]); else SelectCells(wanted); } });
                return;
            }
            var error = ErrorCode(body);
            Say(status switch
            {
                403 => "Só o dono ou os administradores da ilha podem criar terrenos (por enquanto).",
                409 when error == "name_taken" => "Já existe um terreno com esse nome nesta ilha.",
                409 => "Outro terreno já ocupa parte dessa área.",
                400 => error switch { "not_contiguous" => "O terreno precisa ser de uma peça só (os quadrados têm de se encostar pelos lados).", "outside_island" => "A área passa dos limites da ilha.", "too_big" => "A área é grande demais para um terreno.", "bad_name" => "Dê um nome ao terreno.", _ => "Não foi possível criar o terreno." },
                0 => "Sem conexão com o servidor.",
                _ => $"Não foi possível criar o terreno (HTTP {status})."
            });
        });
    }

    public static string ErrorCode(string body) { try { using var doc = JsonDocument.Parse(body); return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? string.Empty : string.Empty; } catch (JsonException) { return string.Empty; } }

    // ---- drawing over the ground -----------------------------------------------------------------------------

    private static readonly Color Own = new("#12F2E0"), Available = new("#3BF45F"), Sale = new("#F4A340"), Restricted = new("#EF4444"), PublicGray = new("#A7AFB5"), Owned = new("#8FB4D9");

    /// The colour of a parcel by what it means to this person.
    public static Color ColorOf(string state) => state switch { "own" or "rented_by_me" => Own, "sale" or "rent" => Sale, "restricted" => Restricted, "public" => PublicGray, _ => Owned };

    private void RefreshAfterChange() { RebuildOverlay(); Changed?.Invoke(); UpdateInfo(); }

    /// How often the client asks the server whether the land changed (seconds); 0 turns the asking off.
    public double PollSeconds { get; set; } = 10;
    private double _sincePoll;
    private bool _polling;

    /// Asks for the land version (a few bytes) and reloads the whole land only when it differs from what we have: other people's sales, rentals,
    /// new parcels and rule changes show up without anyone opening a window.
    public void Poll()
    {
        if (_polling) return;
        _polling = true;
        Call(Godot.HttpClient.Method.Get, "/version", null, (status, body) =>
        {
            _polling = false;
            if (status is < 200 or >= 300) return;
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("version", out var v) && v.TryGetInt64(out var version) && version != _version) Load();
            }
            catch (JsonException) { }
        });
    }

    public override void _Process(double delta)
    {
        if (PollSeconds > 0 && _network is not null && _network.IsAuthenticated) { _sincePoll += delta; if (_sincePoll >= PollSeconds) { _sincePoll = 0; Poll(); } }
        if (!_active && _selection.Count == 0) return;
        _sinceCheck += delta;
        if (_sinceCheck < .5) return;
        _sinceCheck = 0;
        if (Camera is { } camera && new Vector2(camera.GlobalPosition.X, camera.GlobalPosition.Z).DistanceTo(_focus) > 8f) RebuildOverlay();
    }

    private float Ground(float x, float z) => (_terrain?.Map.SampleHeight(x, z) ?? 0f) + .12f;

    private void RebuildOverlay()
    {
        if (_overlay is null) return;
        var show = _active || _selection.Count > 0;
        _overlay.Visible = show;
        if (!show) { _overlay.Mesh = null; return; }
        if (Camera is { } camera) _focus = new Vector2(camera.GlobalPosition.X, camera.GlobalPosition.Z);
        var build = new OverlayBuilder(Ground);
        var cx0 = LandMath.CellOf(_focus.X) - WindowCells; var cx1 = LandMath.CellOf(_focus.X) + WindowCells; var cz0 = LandMath.CellOf(_focus.Y) - WindowCells; var cz1 = LandMath.CellOf(_focus.Y) + WindowCells;
        if (_active)
        {
            for (var cx = cx0; cx <= cx1; cx++)
                for (var cz = cz0; cz <= cz1; cz++)
                {
                    if (!LandMath.InsideIsland(cx, cz, _islandSize)) continue;
                    var key = LandMath.Key(cx, cz);
                    if (_selection.Contains(key)) continue;
                    if (_owners.TryGetValue(key, out var parcel)) build.Cell(cx, cz, new Color(ColorOf(parcel.State), .16f));
                    else build.Cell(cx, cz, new Color(Available, .10f));
                }
            foreach (var parcel in _parcels)
                foreach (var edge in LandMath.Boundary(parcel.Cells)) build.Strip(edge, .35f, new Color(ColorOf(parcel.State), .95f));
        }
        if (_selection.Count > 0)
        {
            foreach (var key in _selection) { var (cx, cz) = LandMath.Unkey(key); build.Cell(cx, cz, new Color(Own, .30f)); }
            if (_selection.Count <= 600)
            {
                var extent = LandMath.Extent(_selection)!.Value;
                for (var cx = extent.MinCx; cx <= extent.MaxCx + 1; cx++) for (var cz = extent.MinCz; cz <= extent.MaxCz; cz++) if (_selection.Contains(LandMath.Key(cx, cz)) || _selection.Contains(LandMath.Key(cx - 1, cz))) build.Strip(new LandEdge(cx, cz, cx, cz + 1), .08f, new Color(Own, .55f));
                for (var cz = extent.MinCz; cz <= extent.MaxCz + 1; cz++) for (var cx = extent.MinCx; cx <= extent.MaxCx; cx++) if (_selection.Contains(LandMath.Key(cx, cz)) || _selection.Contains(LandMath.Key(cx, cz - 1))) build.Strip(new LandEdge(cx, cz, cx + 1, cz), .08f, new Color(Own, .55f));
            }
            foreach (var edge in LandMath.Boundary(_selection)) build.Strip(edge, .5f, new Color(Own, 1f));
        }
        _overlay.Mesh = build.Build();
        _overlay.MaterialOverride = OverlayMaterial;
        _overlay.GlobalTransform = Transform3D.Identity;
    }

    public int OverlayVerticesForTesting => _overlay?.Mesh is ArrayMesh mesh && mesh.GetSurfaceCount() > 0 ? mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length : 0;
    public bool OverlayVisibleForTesting => _overlay is not null && _overlay.Visible;

    private static StandardMaterial3D? _material;
    private static StandardMaterial3D OverlayMaterial => _material ??= new StandardMaterial3D
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled, DisableReceiveShadows = true
    };

    /// Collects the triangles of the overlay: cell fills and strips along edges, both following the ground height.
    private sealed class OverlayBuilder(Func<float, float, float> ground)
    {
        private readonly List<Vector3> _vertices = []; private readonly List<Color> _colors = [];

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            foreach (var v in new[] { a, b, c, a, c, d }) { _vertices.Add(v); _colors.Add(color); }
        }

        private Vector3 Point(float x, float z, float lift = 0) => new(x, ground(x, z) + lift, z);

        /// One cell, split in 2 x 2 so it hugs a sloping ground.
        public void Cell(int cx, int cz, Color color)
        {
            const float size = LandMath.CellSize; var half = size / 2;
            for (var i = 0; i < 2; i++)
                for (var j = 0; j < 2; j++)
                {
                    float x0 = cx * size + i * half, z0 = cz * size + j * half, x1 = x0 + half, z1 = z0 + half;
                    Quad(Point(x0, z0), Point(x1, z0), Point(x1, z1), Point(x0, z1), color);
                }
        }

        /// A ribbon of this width along an edge (in cell corners), one piece per metre.
        public void Strip(LandEdge edge, float width, Color color)
        {
            float x0 = edge.X0 * (float)LandMath.CellSize, z0 = edge.Z0 * (float)LandMath.CellSize, x1 = edge.X1 * (float)LandMath.CellSize, z1 = edge.Z1 * (float)LandMath.CellSize;
            var length = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(z1 - z0)); var pieces = Mathf.Max(1, Mathf.CeilToInt(length));
            var horizontal = Mathf.Abs(z1 - z0) < .001f; var h = width / 2;
            for (var i = 0; i < pieces; i++)
            {
                float t0 = i / (float)pieces, t1 = (i + 1) / (float)pieces;
                float ax = Mathf.Lerp(x0, x1, t0), az = Mathf.Lerp(z0, z1, t0), bx = Mathf.Lerp(x0, x1, t1), bz = Mathf.Lerp(z0, z1, t1);
                if (horizontal) Quad(Point(ax, az - h, .03f), Point(bx, bz - h, .03f), Point(bx, bz + h, .03f), Point(ax, az + h, .03f), color);
                else Quad(Point(ax - h, az, .03f), Point(bx - h, bz, .03f), Point(bx + h, bz, .03f), Point(ax + h, az, .03f), color);
            }
        }

        public ArrayMesh Build()
        {
            var mesh = new ArrayMesh();
            if (_vertices.Count == 0) return mesh;
            var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray(); arrays[(int)Mesh.ArrayType.Color] = _colors.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }
    }
}
