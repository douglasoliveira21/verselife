using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Build;

/// <summary>
/// Constructions: saving the selection in the inventory ("Guardar"), taking it out of the world ("Retirar"), and putting one back: a see-through preview follows the mouse over the ground
/// and a click places it. Placing creates the objects one by one with the ordinary build operations (each checked again by the server), paced under the island's rate limit, as one
/// undo step; a construction that cannot be copied is placed by the server in one go. The ready-made constructions ("Predefinidos") use the same path.
/// </summary>
public partial class BuildPackageManager : Node
{
    public const float RezInterval = .09f; // about 11 objects a second, under the island server's limit of 20
    public const int BurstLimit = 10;
    private const string GhostLook = "{\"color\":\"#7FE8FF\",\"transparency\":0.4,\"emission\":\"#12F2E0\",\"emissionStrength\":0.7}";

    private BuildNetworkSynchronizer _sync = null!;
    private BuildSelectionManager _selection = null!;
    private BuildUndoRedoManager _history = null!;
    private Func<Vector2, Vector3?> _spotAt = _ => null;
    private Action<string> _say = _ => { };
    private Action _deleteSelection = () => { };
    private Action _ensureActive = () => { };
    private Node3D? _previewRoot;
    private Action<Vector3>? _previewConfirm;
    private readonly Queue<Action> _queue = new();
    private double _clock;
    private bool _busy, _takeAfterSave;
    private List<Guid>? _takeIds;
    private HttpRequest? _http;
    private string _pendingName = string.Empty; private int _pendingCount;

    /// <summary>Tests capture the saved package instead of sending it: (json) -> handled.</summary>
    public Func<string, bool>? PackageSink { get; set; }
    public bool PreviewActive => _previewRoot is not null;
    public int PreviewParts { get; private set; }
    public Vector3 PreviewAt => _previewRoot?.Position ?? Vector3.Zero;
    public int Pending => _queue.Count;
    public event Action? Saved;

    public void Initialize(BuildNetworkSynchronizer sync, BuildSelectionManager selection, BuildUndoRedoManager history, Func<Vector2, Vector3?> spotAt, Action<string> say, Action deleteSelection, Action ensureActive)
    { _sync = sync; _selection = selection; _history = history; _spotAt = spotAt; _say = say; _deleteSelection = deleteSelection; _ensureActive = ensureActive; }

    public override void _Process(double delta)
    {
        if (_queue.Count > 0) { _clock += delta; while (_clock >= RezInterval && _queue.Count > 0) { _clock -= RezInterval; _queue.Dequeue()(); } }
        if (_previewRoot is not null && IsInstanceValid(_previewRoot))
        {
            var spot = GetViewport().GuiGetHoveredControl() is null ? _spotAt(GetViewport().GetMousePosition()) : null;
            _previewRoot.Visible = spot is not null; if (spot is { } where) _previewRoot.Position = where - new Vector3(0, .5f, 0);
        }
    }

    // ---- saving ---------------------------------------------------------------------------------------------------------

    /// <summary>Guardar (and Retirar, which also takes the objects out of the world once they are safe in the inventory). Only the person's own objects.</summary>
    public void SaveSelection(string name, bool take)
    {
        if (_busy) { _say("Aguarde: o envio anterior ainda não terminou."); return; }
        var views = _selection.Views().ToList();
        if (views.Count == 0) { _say("Selecione os objetos que quer guardar."); return; }
        if (views.Count > BuildPackages.MaxObjects) { _say($"Uma construção guarda até {BuildPackages.MaxObjects} objetos."); return; }
        var me = _sync.MyUserId;
        if (views.FirstOrDefault(v => me != Guid.Empty && v.State.OwnerUserId != me) is { } notMine) { _say($"\"{notMine.State.Name}\" não é seu: só dá para guardar objetos seus."); return; }
        name = string.IsNullOrWhiteSpace(name) ? (views.Count == 1 ? views[0].State.Name : "Construção") : name.Trim();
        var sources = views.Select(v => { var s = v.State; return new SourceObject(s.PrimitiveType, s.Name, s.Description, s.PositionX, s.PositionY, s.PositionZ, s.RotationX, s.RotationY, s.RotationZ, s.ScaleX, s.ScaleY, s.ScaleZ, s.MaterialJson, s.GeometryJson, s.PhysicsJson, s.ContentJson, s.GroupId, s.Id, s.ParentObjectId, s.PivotX, s.PivotY, s.PivotZ); }).ToList();
        var json = BuildPackages.Serialize(BuildPackages.Create(name, sources));
        _takeAfterSave = take; _takeIds = take ? views.Select(v => v.State.Id).ToList() : null;
        if (PackageSink is not null) { if (PackageSink(json)) OnSaved(name, sources.Count); return; }
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is not { IsAuthenticated: true } || app is null) { _say("Entre na sua conta para guardar no inventário."); return; }
        if (_http is null) { _http = new HttpRequest { Timeout = 30 }; AddChild(_http); _http.RequestCompleted += OnResponse; }
        _busy = true; _pendingName = name; _pendingCount = sources.Count;
        if (_http.Request($"{app.ApiBaseUrl}/api/v1/inventory/build-packages", [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"], Godot.HttpClient.Method.Post, json) != Error.Ok) { _busy = false; _say("Não foi possível enviar a construção."); }
        else _say("Guardando no inventário…");
    }

    private void OnResponse(long result, long code, string[] headers, byte[] body)
    {
        _busy = false;
        if (code is >= 200 and < 300) { OnSaved(_pendingName, _pendingCount); return; }
        _takeIds = null; var reason = "";
        try { using var doc = JsonDocument.Parse(body.GetStringFromUtf8()); if (doc.RootElement.TryGetProperty("errors", out var errors)) reason = string.Join(" ", errors.EnumerateObject().SelectMany(x => x.Value.EnumerateArray()).Select(x => x.GetString())); }
        catch (JsonException) { }
        _say(result != (long)HttpRequest.Result.Success ? "Sem conexão com o servidor." : $"O servidor recusou a construção{(reason.Length > 0 ? $": {reason}" : $" (HTTP {code})")}.");
    }

    private void OnSaved(string name, int count)
    {
        _say($"\"{name}\" guardada no inventário ({count} objeto(s)).");
        if (_takeAfterSave && _takeIds is { Count: > 0 } ids)
        {
            _selection.Set(ids.Where(id => _sync.Views.ContainsKey(id))); _deleteSelection();
            _say($"\"{name}\" retirada do mundo e guardada no inventário ({count} objeto(s)). Ctrl+Z traz de volta ao mundo.");
        }
        _takeAfterSave = false; _takeIds = null; Saved?.Invoke();
        GetParent()?.GetParent()?.GetNodeOrNull<InventoryPanel>("InventoryPanel")?.Refresh();
    }

    // ---- placing ---------------------------------------------------------------------------------------------------------

    /// <summary>Fetches a construction of the inventory and shows its preview; the click places it. One that cannot be copied is placed by the server where <paramref name="fallback"/> is.</summary>
    public void PreviewPackage(Guid itemId, Vector3 fallback)
    {
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (!_sync.CanSend) { _say("Entre em uma ilha autenticada antes de colocar construções."); return; }
        if (session is not { IsAuthenticated: true } || app is null) { _say("Entre na sua conta para usar o inventário."); return; }
        var http = new HttpRequest { Timeout = 30 }; AddChild(http);
        http.RequestCompleted += (_, code, _, body) =>
        {
            http.QueueFree();
            if (code == 403 && body.GetStringFromUtf8().Contains("use_rez", StringComparison.Ordinal)) { RezByServer(itemId, fallback); return; }
            if (code is < 200 or >= 300) { _say($"Não foi possível abrir a construção (HTTP {code})."); return; }
            var json = body.GetStringFromUtf8();
            if (BuildPackages.Parse(json, out var error) is not { } package) { _say($"Construção inválida: {error}"); return; }
            StartPreview(package, ground => PlaceJson(json, ground));
        };
        if (http.Request($"{app.ApiBaseUrl}/api/v1/inventory/build-packages/{itemId}", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) { http.QueueFree(); _say("Não foi possível abrir a construção."); }
        else _say("Abrindo a construção…");
    }

    /// <summary>A construction that cannot be copied: the server creates every object itself (all or none) and uses the item up. There is no undo; "Devolver" sends it back.</summary>
    public void RezByServer(Guid itemId, Vector3 ground)
    {
        if (!_sync.CanSend) { _say("Entre em uma ilha autenticada antes de colocar construções."); return; }
        _say("Pedindo ao servidor para colocar a construção…");
        _sync.SendRaw(JsonSerializer.Serialize(new { operation = "rez", packageItemId = itemId, originX = ground.X, originY = ground.Y, originZ = ground.Z, expectedRevision = 0 }));
    }

    /// <summary>Places a construction given as JSON with its base on <paramref name="ground"/>; <paramref name="instant"/> skips the pacing (tests). Returns how many objects.</summary>
    public int PlaceJson(string json, Vector3 ground, bool instant = false)
    {
        var package = BuildPackages.Parse(json, out var error);
        if (package is null) { _say($"Construção inválida: {error}"); return 0; }
        _ensureActive();
        var placed = BuildPackages.Place(package, new System.Numerics.Vector3(ground.X, ground.Y, ground.Z));
        var total = placed.Count; var changes = new List<BuildUndoRedoManager.Change>(); var ids = new List<Guid>(); var copyOf = new Dictionary<int, Guid>();
        var actions = new List<Action>();
        for (var i = 0; i < total; i++)
        {
            var index = i; var item = placed[i]; var change = new BuildUndoRedoManager.Change(); changes.Add(change);
            var template = TemplateOf(item.Source); template.PositionX = item.Position.X; template.PositionY = item.Position.Y; template.PositionZ = item.Position.Z; template.GroupId = item.GroupId;
            actions.Add(() =>
            {
                if (!_sync.Create(template, created =>
                {
                    _history.Created(change, created); ids.Add(created.Id); copyOf[index] = created.Id;
                    if (ids.Count == total) { _history.Record("colocar construção", changes); _selection.Set(ids); HangCopies(placed, copyOf); _say($"Construção colocada ({total} objeto(s))."); }
                })) _queue.Clear();
            });
        }
        if (instant || actions.Count <= BurstLimit) foreach (var action in actions) action();
        else { foreach (var action in actions) _queue.Enqueue(action); _say($"Colocando a construção: {total} objeto(s)…"); }
        return total;
    }

    /// <summary>The copies were created loose in a group of their own; this hangs each under the copy of its parent, like the saved construction.</summary>
    private void HangCopies(IReadOnlyList<PlacedObject> placed, Dictionary<int, Guid> copyOf)
    {
        var links = new List<(Guid Id, Guid? Group, Guid? Parent, int Revision)>();
        foreach (var set in placed.Select((p, i) => (p, i)).Where(x => x.p.GroupId is not null).GroupBy(x => x.p.GroupId!.Value))
        {
            var members = set.ToList(); var indexes = members.Select(m => m.i).ToHashSet();
            var top = members.FirstOrDefault(m => m.p.Source.Parent is not { } up || !indexes.Contains(up)); if (top.p is null || !copyOf.TryGetValue(top.i, out var topCopy)) continue;
            foreach (var (p, i) in members)
            {
                if (i == top.i || !copyOf.TryGetValue(i, out var mine)) continue;
                var parent = p.Source.Parent is { } up && indexes.Contains(up) && copyOf.TryGetValue(up, out var parentCopy) ? parentCopy : topCopy;
                var revision = _sync.Views.TryGetValue(mine, out var view) ? view.State.Revision : 1;
                links.Add((mine, set.Key, parent, revision));
            }
        }
        if (links.Count > 0) _sync.Structure(links);
    }

    private static BuildObjectState TemplateOf(PackageObject o) => new()
    {
        PrimitiveType = o.PrimitiveType, Name = o.Name, Description = o.Description, RotationX = o.RotationX, RotationY = o.RotationY, RotationZ = o.RotationZ,
        ScaleX = o.ScaleX, ScaleY = o.ScaleY, ScaleZ = o.ScaleZ, MaterialJson = o.MaterialJson, GeometryJson = o.GeometryJson,
        PhysicsJson = string.IsNullOrWhiteSpace(o.PhysicsJson) ? "{\"collision\":true}" : o.PhysicsJson, ContentJson = string.IsNullOrWhiteSpace(o.ContentJson) ? "[]" : o.ContentJson,
        PivotX = o.PivotX, PivotY = o.PivotY, PivotZ = o.PivotZ
    };

    // ---- preview ---------------------------------------------------------------------------------------------------------

    /// <summary>Shows a see-through copy of the construction following the mouse; <paramref name="confirm"/> gets the ground point on the click.</summary>
    public bool StartPreview(BuildPackage package, Action<Vector3> confirm)
    {
        _ensureActive();
        if (!_sync.CanSend) { _say("Entre em uma ilha autenticada antes de construir."); return false; }
        CancelPreview(null);
        _previewRoot = new Node3D { Name = "ConstructionPreview", Visible = false }; GetParent()!.GetParent()!.AddChild(_previewRoot);
        foreach (var o in package.Objects)
        {
            var view = new BuildObjectView(); _previewRoot.AddChild(view);
            view.Apply(new BuildObjectState { Id = Guid.NewGuid(), PrimitiveType = o.PrimitiveType == "mesh" ? "cube" : o.PrimitiveType, Name = string.Empty, MaterialJson = GhostLook, PhysicsJson = "{\"collision\":false}", GeometryJson = o.PrimitiveType == "mesh" ? "{}" : o.GeometryJson,
                PositionX = o.X, PositionY = package.BaseOffset + o.Y, PositionZ = o.Z, RotationX = o.RotationX, RotationY = o.RotationY, RotationZ = o.RotationZ, ScaleX = o.ScaleX, ScaleY = o.ScaleY, ScaleZ = o.ScaleZ });
        }
        PreviewParts = package.Objects.Length; _previewConfirm = confirm;
        var min = new Vector3(package.Objects.Min(o => o.X - o.ScaleX / 2), package.Objects.Min(o => o.Y - o.ScaleY / 2), package.Objects.Min(o => o.Z - o.ScaleZ / 2));
        var max = new Vector3(package.Objects.Max(o => o.X + o.ScaleX / 2), package.Objects.Max(o => o.Y + o.ScaleY / 2), package.Objects.Max(o => o.Z + o.ScaleZ / 2));
        var size = max - min; string D(float v) => v.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        _say($"Prévia de \"{package.Name}\": {package.Objects.Length} objeto(s), {D(size.X)} × {D(size.Y)} × {D(size.Z)} m. Clique para colocar; Esc cancela.");
        return true;
    }

    /// <summary>A click while a preview is shown: places it there. Returns true when the click was for the preview.</summary>
    public bool TryConfirm(Vector2 screen)
    {
        if (_previewRoot is null) return false;
        if (_spotAt(screen) is not { } spot) { _say("Clique no terreno ou em um objeto para colocar."); return true; }
        ConfirmAt(spot - new Vector3(0, .5f, 0)); return true;
    }

    public void ConfirmAt(Vector3 ground) { var confirm = _previewConfirm; CancelPreview(null); confirm?.Invoke(ground); }

    public bool CancelPreview(string? message)
    {
        var had = _previewRoot is not null;
        if (_previewRoot is not null && IsInstanceValid(_previewRoot)) _previewRoot.QueueFree();
        _previewRoot = null; _previewConfirm = null; PreviewParts = 0;
        if (had && message is not null) _say(message);
        return had;
    }

    public void CancelPlacing(string why) { if (_queue.Count == 0) return; _queue.Clear(); _say(why); }

    /// <summary>A ready-made construction of "Predefinidos": preview first, then the click places it.</summary>
    public void PlacePreset(string id)
    {
        if (BuildPresets.Find(id) is not { } preset) return;
        var json = BuildPackages.Serialize(preset.Package);
        StartPreview(preset.Package, ground => PlaceJson(json, ground));
    }
}
