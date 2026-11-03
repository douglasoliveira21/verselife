using System.Globalization;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// Building together: who is editing what (leases the island server hands out), how full the island and the parcel are, sending other
/// people's objects back to their inventories, and the history of an object. The island and the API decide all of it; this only shows
/// the result and asks politely, so what a person sees can never override what the server allows.
public partial class BuildEditor
{
    private sealed record Holder(Guid User, string Name, long ExpiresMs);

    private const double LockRenewSeconds = 8, LimitsRefreshSeconds = 12;
    private readonly Dictionary<Guid, Holder> _lockedBy = [];
    private readonly HashSet<Guid> _myLocks = [];
    private double _lockClock, _limitsClock;
    private Label _limitsLabel = null!, _lockLabel = null!;
    private HttpRequest? _limitsHttp, _auditHttp;
    private AcceptDialog _historyDialog = null!;
    private Label _historyText = null!;

    // ---- who is editing what ---------------------------------------------------------------------------------

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// Someone other than me holds this object right now.
    private bool LockedByOther(BuildObjectView view, out string name)
    {
        name = string.Empty;
        if (!_lockedBy.TryGetValue(view.State.Id, out var holder) || holder.ExpiresMs <= NowMs() || (holder.User == _session.UserId && _session.UserId != Guid.Empty)) return false;
        name = holder.Name; return true;
    }

    public string? LockedByForTesting(Guid id) => _objects.TryGetValue(Resolve(id), out var view) && LockedByOther(view, out var name) ? name : null;

    private bool SelectionLockedByOther(out string name)
    {
        foreach (var view in _selection) if (LockedByOther(view, out name)) return true;
        name = string.Empty; return false;
    }

    /// Keeps my leases in step with my selection: take what I selected, give back what I let go. Everything else is the server's call.
    private void SyncLocks()
    {
        if (!_active) { ReleaseMyLocks(); return; }
        var wanted = _selection.Where(v => !LockedByOther(v, out _)).Select(v => v.State.Id).ToHashSet();
        var give = _myLocks.Where(id => !wanted.Contains(id)).ToArray();
        if (give.Length > 0) { Send(LockCommand("unlock", give)); foreach (var id in give) _myLocks.Remove(id); }
        var take = wanted.Where(id => !_myLocks.Contains(id)).ToArray();
        if (take.Length > 0) { Send(LockCommand("lock", wanted.ToArray())); foreach (var id in take) _myLocks.Add(id); }
    }

    private void ReleaseMyLocks()
    {
        if (_myLocks.Count == 0) return;
        Send(LockCommand("unlock", _myLocks.ToArray())); _myLocks.Clear();
    }

    private static string LockCommand(string operation, Guid[] ids) => JsonSerializer.Serialize(new { operation, objectIds = ids });

    private void PumpCollab(double delta)
    {
        if (!_active) return;
        _lockClock += delta; _limitsClock += delta;
        if (_lockClock >= LockRenewSeconds) { _lockClock = 0; if (_myLocks.Count > 0) Send(LockCommand("lock", _myLocks.ToArray())); }
        if (_limitsClock >= LimitsRefreshSeconds) { _limitsClock = 0; RefreshLimits(); }
    }

    /// A lock or unlock announced by the island (mine or someone else's).
    private void HandleLockMessage(JsonElement root, string operation)
    {
        if (!root.TryGetProperty("objectIds", out var ids) || ids.ValueKind != JsonValueKind.Array) return;
        var user = root.TryGetProperty("userId", out var u) && Guid.TryParse(u.GetString(), out var parsedUser) ? parsedUser : Guid.Empty;
        var name = root.TryGetProperty("userName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "outro jogador" : "outro jogador";
        var expires = root.TryGetProperty("expiresAtMs", out var e) && e.TryGetInt64(out var ms) ? ms : NowMs() + 20_000;
        foreach (var raw in ids.EnumerateArray())
        {
            if (!Guid.TryParse(raw.GetString(), out var id)) continue;
            if (operation == "lock") _lockedBy[id] = new Holder(user, name, expires);
            else if (_lockedBy.TryGetValue(id, out var current) && current.User == user) _lockedBy.Remove(id);
            if (_objects.TryGetValue(id, out var view)) RefreshLockTag(view);
        }
        if (_selection.Count > 0) { FillFields(); if (SelectionLockedByOther(out var who)) _status.Text = $"{who} está editando este objeto: você pode olhar, mas não alterar."; }
    }

    private void RefreshLockTag(BuildObjectView view) => view.SetLockedBy(LockedByOther(view, out var name) ? name : null);

    private void RejectCodeText(string code)
    {
        if (code.StartsWith("locked:", StringComparison.Ordinal)) { _status.Text = $"{code[7..]} está editando este objeto agora. Tente de novo em instantes."; return; }
        _status.Text = code switch
        {
            "forbidden" => "Você não tem permissão para construir aqui.",
            "parcel_forbidden" => "Este terreno não permite que você construa.",
            "not_owner" => "Este objeto não é seu: só o dono, o dono do terreno ou um administrador podem alterá-lo.",
            "not_allowed_to_return" => "Você não pode devolver esses objetos (só o dono do terreno ou um administrador).",
            "cost_limit" => "O custo total da ilha chegou ao limite; use formas mais simples ou apague algo.",
            "object_quota" or "server_rejected_409" => "A ilha atingiu o limite de objetos.",
            "parcel_object_limit" => "O terreno atingiu o limite de objetos.",
            "parcel_cost_limit" => "O terreno atingiu o limite de custo.",
            "invalid_object" => "O servidor recusou algum valor do objeto (tamanho, forma, textura ou conteúdo).",
            "not_found" => "Esse objeto não existe mais.",
            "lock_limit" => "Você está segurando objetos demais ao mesmo tempo.",
            "revision_conflict" => "Outra pessoa alterou este objeto antes; a edição foi recusada.",
            "rate_limit" => "Muitas alterações seguidas; aguarde um instante.",
            "service_unavailable" => "O servidor de construção não respondeu; tente novamente.",
            _ => $"Construção rejeitada: {code}"
        };
    }

    // ---- limits ------------------------------------------------------------------------------------------------

    private void RefreshLimits()
    {
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is null || app is null || !session.IsAuthenticated || _network.CurrentIslandId == Guid.Empty) return;
        var spot = LookPoint();
        if (_limitsHttp is null) { _limitsHttp = new HttpRequest { Timeout = 10 }; AddChild(_limitsHttp); _limitsHttp.RequestCompleted += (_, code, _, body) => { if (code is >= 200 and < 300) ShowLimits(body.GetStringFromUtf8()); }; }
        if (_limitsHttp.GetHttpClientStatus() != Godot.HttpClient.Status.Disconnected) return;
        _limitsHttp.Request($"{app.ApiBaseUrl}/api/v1/worlds/islands/{_network.CurrentIslandId}/build-limits?x={spot.X.ToString(CultureInfo.InvariantCulture)}&z={spot.Z.ToString(CultureInfo.InvariantCulture)}", [$"Authorization: Bearer {session.AccessToken}"]);
    }

    /// The point on the ground the camera looks at (or in front of it): the spot the limits are about.
    private Vector3 LookPoint()
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return Vector3.Zero;
        var center = GetViewport().GetVisibleRect().Size / 2;
        var origin = camera.ProjectRayOrigin(center); var direction = camera.ProjectRayNormal(center);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(origin, origin + direction * 60f, uint.MaxValue & ~BuildGizmo.PhysicsLayer));
        return hit.Count > 0 ? hit["position"].AsVector3() : origin + direction * 8f;
    }

    private static string Short(long value) => value >= 10_000 ? $"{value / 1000.0:0.#} mil" : value.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));

    /// Shows the island's and the parcel's usage from the API's answer.
    public void ShowLimits(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json); var r = document.RootElement;
            var text = $"Ilha: {Short(r.GetProperty("objects").GetInt64())} de {Short(r.GetProperty("maxObjects").GetInt64())} objetos · custo {Short(r.GetProperty("cost").GetInt64())} de {Short(r.GetProperty("maxCost").GetInt64())}";
            if (r.TryGetProperty("parcel", out var parcel) && parcel.ValueKind == JsonValueKind.Object)
                text += $"\nTerreno \"{parcel.GetProperty("name").GetString()}\"{(parcel.GetProperty("mine").GetBoolean() ? " (seu)" : "")}: {Short(parcel.GetProperty("objects").GetInt64())} de {Short(parcel.GetProperty("maxObjects").GetInt64())} objetos · custo {Short(parcel.GetProperty("cost").GetInt64())} de {Short(parcel.GetProperty("maxCost").GetInt64())}";
            _limitsLabel.Text = text;
            var canBuild = r.GetProperty("canBuildHere").GetBoolean();
            _buildHereLabel.Text = canBuild ? "✓ Você pode construir aqui." : "✕ Você não pode construir neste ponto.";
            _buildHereLabel.AddThemeColorOverride("font_color", canBuild ? UiStyle.SuccessBright : UiStyle.Orange);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { _limitsLabel.Text = "Não foi possível ler os limites da ilha."; _buildHereLabel.Text = string.Empty; }
    }

    // ---- returning and history -----------------------------------------------------------------------------------

    private void ReturnSelection()
    {
        if (_selection.Count == 0) { _status.Text = "Selecione os objetos que quer devolver."; return; }
        Send(JsonSerializer.Serialize(new { operation = "return", objectIds = _selection.Select(v => v.State.Id).ToArray() }));
        _status.Text = "Devolvendo ao inventário dos donos… (não dá para desfazer; eles recebem de volta como construção)";
    }

    private void ShowHistory()
    {
        if (_selection.Count != 1) { _status.Text = "Selecione um só objeto para ver o histórico."; return; }
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is null || app is null || !session.IsAuthenticated || _network.CurrentIslandId == Guid.Empty) { _status.Text = "Entre em uma ilha para ver o histórico."; return; }
        if (_auditHttp is null)
        {
            _auditHttp = new HttpRequest { Timeout = 10 }; AddChild(_auditHttp);
            _auditHttp.RequestCompleted += (_, code, _, body) => { if (code is >= 200 and < 300) ShowHistoryText(body.GetStringFromUtf8()); else _status.Text = code == 403 ? "Só o dono, o dono do terreno ou um administrador veem o histórico." : $"Não foi possível abrir o histórico (HTTP {code})."; };
        }
        if (_auditHttp.GetHttpClientStatus() == Godot.HttpClient.Status.Disconnected) _auditHttp.Request($"{app.ApiBaseUrl}/api/v1/worlds/islands/{_network.CurrentIslandId}/objects/{_selection[0].State.Id}/audit", [$"Authorization: Bearer {session.AccessToken}"]);
    }

    private static string OperationLabel(string operation) => operation switch { "create" => "Criado", "update" => "Alterado", "delete" => "Excluído", "return" => "Devolvido", _ => operation };

    /// Lists the audit answer (newest first) in a dialog: what happened, who did it (you, or the start of their id), the revision and when.
    public void ShowHistoryText(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var lines = document.RootElement.EnumerateArray().Select(e =>
            {
                var actor = Guid.TryParse(e.GetProperty("actorUserId").GetString(), out var id) ? (id == _session.UserId ? "você" : id.ToString()[..8]) : "?";
                var at = DateTimeOffset.TryParse(e.GetProperty("at").GetString(), out var when) ? when.ToLocalTime().ToString("dd/MM HH:mm") : "";
                return $"{at}  ·  {OperationLabel(e.GetProperty("operation").GetString() ?? "")} por {actor}  ·  revisão {e.GetProperty("revision").GetInt32()}";
            }).ToList();
            _historyText.Text = lines.Count == 0 ? "Sem registros." : string.Join("\n", lines);
            _historyDialog.PopupCentered();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { _status.Text = "Histórico inválido recebido do servidor."; }
    }

    public string HistoryTextForTesting => _historyText.Text;
    public string LimitsTextForTesting => _limitsLabel.Text + "\n" + _buildHereLabel.Text;
    public string StatusForTesting => _status.Text;
    public void RejectForTesting(string code) { OnRejected(code); }
    public void ReturnSelectionForTesting() => ReturnSelection();
    public int MyLockCountForTesting => _myLocks.Count;
    public void RenewLocksForTesting() { _lockClock = LockRenewSeconds; PumpCollab(0); }
}
