using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// Saving a construction in the inventory ("Salvar no inventário", "Retirar") and putting one back in the world. A saved
/// construction is plain data (<see cref="BuildPackage"/>); placing it creates its objects one by one with the ordinary build
/// operations, paced under the server's rate limit, so each object is checked again and the whole placing is one undo step.
public partial class BuildEditor
{
    private const float RezInterval = .09f; // about 11 objects a second, under the island server's limit of 20
    private sealed record RezStep(PlacedObject Object, int Index);

    private LineEdit _packageName = null!;
    private HttpRequest? _packageHttp;
    private readonly Queue<RezStep> _rezQueue = new();
    private double _rezClock;
    private bool _takeAfterSave;
    private List<BuildObjectView>? _takeViews;
    private bool _packageBusy;

    /// Tests set this to capture the saved package instead of sending it: (json) -> handled.
    public Func<string, bool>? PackageSink { get; set; }
    public int RezPendingForTesting => _rezQueue.Count;

    private void BuildPackageSection(VBoxContainer box)
    {
        box.AddChild(FieldLabel("Salvar construção no inventário"));
        _packageName = Field("Nome da construção", 160); box.AddChild(_packageName);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6); box.AddChild(row);
        row.AddChild(TextButton("Salvar cópia", "Guarda a seleção no inventário e deixa os objetos no mundo", () => SaveSelection(take: false)));
        row.AddChild(TextButton("Retirar", "Guarda a seleção no inventário e tira os objetos do mundo", () => SaveSelection(take: true)));
    }

    private void PumpRez(double delta)
    {
        if (_rezQueue.Count == 0) return;
        _rezClock += delta;
        while (_rezClock >= RezInterval && _rezQueue.Count > 0) { _rezClock -= RezInterval; CreatePlaced(_rezQueue.Dequeue()); }
    }

    // ---- saving ----------------------------------------------------------------------------------------------

    private void SaveSelection(bool take)
    {
        if (_packageBusy) { _status.Text = "Aguarde: o envio anterior ainda não terminou."; return; }
        if (_selection.Count == 0) { _status.Text = "Selecione os objetos que quer guardar."; return; }
        if (_selection.Count > BuildPackages.MaxObjects) { _status.Text = $"Uma construção guarda até {BuildPackages.MaxObjects} objetos."; return; }
        var notMine = _selection.FirstOrDefault(v => _session.UserId != Guid.Empty && v.State.OwnerUserId != _session.UserId);
        if (notMine is not null) { _status.Text = $"\"{notMine.State.Name}\" não é seu: só dá para guardar objetos seus."; return; }
        var name = string.IsNullOrWhiteSpace(_packageName.Text) ? (_selection.Count == 1 ? _selection[0].State.Name : "Construção") : _packageName.Text.Trim();
        var sources = _selection.Select(v => { var s = FromView(v); return new SourceObject(s.PrimitiveType, s.Name, s.Description, s.PositionX, s.PositionY, s.PositionZ, s.RotationX, s.RotationY, s.RotationZ, s.ScaleX, s.ScaleY, s.ScaleZ, s.MaterialJson, s.GeometryJson, s.PhysicsJson, s.ContentJson, s.GroupId); }).ToList();
        var json = BuildPackages.Serialize(BuildPackages.Create(name, sources));
        _takeAfterSave = take; _takeViews = take ? [.. _selection] : null;
        if (PackageSink is not null) { if (PackageSink(json)) OnPackageSaved(name, sources.Count); return; }

        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is null || app is null || !session.IsAuthenticated) { _status.Text = "Entre na sua conta para guardar no inventário."; return; }
        if (_packageHttp is null) { _packageHttp = new HttpRequest { Timeout = 30 }; AddChild(_packageHttp); _packageHttp.RequestCompleted += OnPackageResponse; }
        _packageBusy = true; _pendingName = name; _pendingCount = sources.Count;
        if (_packageHttp.Request($"{app.ApiBaseUrl}/api/v1/inventory/build-packages", [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"], Godot.HttpClient.Method.Post, json) != Error.Ok)
        { _packageBusy = false; _status.Text = "Não foi possível enviar a construção."; }
        else _status.Text = "Guardando no inventário…";
    }

    private string _pendingName = string.Empty; private int _pendingCount;

    private void OnPackageResponse(long result, long code, string[] headers, byte[] body)
    {
        _packageBusy = false;
        if (code is >= 200 and < 300) { OnPackageSaved(_pendingName, _pendingCount); return; }
        _takeViews = null;
        var reason = "";
        try { using var doc = JsonDocument.Parse(body.GetStringFromUtf8()); if (doc.RootElement.TryGetProperty("errors", out var errors)) reason = errors.EnumerateObject().SelectMany(x => x.Value.EnumerateArray()).Select(x => x.GetString()).FirstOrDefault() ?? ""; }
        catch (JsonException) { }
        _status.Text = result != (long)HttpRequest.Result.Success ? "Sem conexão com o servidor." : $"O servidor recusou a construção{(reason.Length > 0 ? $": {reason}" : $" (HTTP {code})")}.";
    }

    private void OnPackageSaved(string name, int count)
    {
        _status.Text = $"\"{name}\" guardada no inventário ({count} objeto(s)).";
        if (_takeAfterSave && _takeViews is { Count: > 0 } views)
        {
            SelectIds(views.Where(IsInstanceValid).Select(v => v.State.Id));
            DeleteSelection();
            _status.Text = $"\"{name}\" retirada do mundo e guardada no inventário ({count} objeto(s)). Ctrl+Z traz de volta ao mundo.";
        }
        _takeAfterSave = false; _takeViews = null;
        GetParent()?.GetNodeOrNull<InventoryPanel>("InventoryPanel")?.Refresh();
    }

    // ---- placing ---------------------------------------------------------------------------------------------

    /// Fetches a saved construction from the inventory and places it with its base point on `ground`.
    public void RezPackage(Guid itemId, Vector3 ground)
    {
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (!_network.IsAuthenticated && OperationSink is null) { _status.Text = "Entre em uma ilha autenticada antes de colocar construções."; return; }
        if (session is null || app is null || !session.IsAuthenticated) { _status.Text = "Entre na sua conta para usar o inventário."; return; }
        var http = new HttpRequest { Timeout = 30 }; AddChild(http);
        http.RequestCompleted += (_, code, _, body) =>
        {
            http.QueueFree();
            if (code is < 200 or >= 300) { _status.Text = $"Não foi possível abrir a construção (HTTP {code})."; return; }
            RezPackageJson(body.GetStringFromUtf8(), ground);
        };
        if (http.Request($"{app.ApiBaseUrl}/api/v1/inventory/build-packages/{itemId}", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) { http.QueueFree(); _status.Text = "Não foi possível abrir a construção."; }
        else _status.Text = "Abrindo a construção…";
    }

    /// Places a construction given as JSON. `instant` skips the pacing (tests, where there is no rate limit).
    public int RezPackageJson(string json, Vector3 ground, bool instant = false)
    {
        var package = BuildPackages.Parse(json, out var error);
        if (package is null) { _status.Text = $"Construção inválida: {error}"; return 0; }
        if (!_active) SetActive(true);
        var placed = BuildPackages.Place(package, new System.Numerics.Vector3(ground.X, ground.Y, ground.Z));
        var entry = new HistoryEntry("create", []);
        var total = placed.Count;
        var created = new List<Guid>();
        for (var i = 0; i < placed.Count; i++)
        {
            var step = new RezStep(placed[i], i);
            _rezQueue.Enqueue(step);
            _rezCallbacks.Enqueue(state => { entry.Items.Add(new HistoryItem(state.Id, null, Clone(state))); created.Add(state.Id); if (created.Count == total) { SelectIds(created); _status.Text = $"Construção colocada ({total} objeto(s))."; } });
        }
        PushHistory(entry);
        _status.Text = instant ? _status.Text : $"Colocando a construção: {total} objeto(s)…";
        if (instant) while (_rezQueue.Count > 0) CreatePlaced(_rezQueue.Dequeue());
        return total;
    }

    private readonly Queue<Action<BuildObjectState>> _rezCallbacks = new();

    private static BuildObjectState TemplateOf(PackageObject o) => new()
    {
        PrimitiveType = o.PrimitiveType, Name = o.Name, Description = o.Description, RotationX = o.RotationX, RotationY = o.RotationY, RotationZ = o.RotationZ,
        ScaleX = o.ScaleX, ScaleY = o.ScaleY, ScaleZ = o.ScaleZ, MaterialJson = o.MaterialJson, GeometryJson = o.GeometryJson,
        PhysicsJson = string.IsNullOrWhiteSpace(o.PhysicsJson) ? "{\"collision\":true}" : o.PhysicsJson, ContentJson = string.IsNullOrWhiteSpace(o.ContentJson) ? "[]" : o.ContentJson
    };

    private void CreatePlaced(RezStep step)
    {
        var callback = _rezCallbacks.Dequeue();
        CreateFrom(TemplateOf(step.Object.Source), new Vector3(step.Object.Position.X, step.Object.Position.Y, step.Object.Position.Z), step.Object.GroupId, callback);
    }

    private void CancelRez(string why)
    {
        if (_rezQueue.Count == 0 && _rezCallbacks.Count == 0) return;
        _rezQueue.Clear(); _rezCallbacks.Clear();
        _status.Text = why;
    }

    public void SaveSelectionForTesting(bool take) => SaveSelection(take);
    public void SetPackageNameForTesting(string name) => _packageName.Text = name;
}
