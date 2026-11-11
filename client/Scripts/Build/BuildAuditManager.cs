using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Build;

/// <summary>
/// The two things a group building together needs besides editing: sending other people's objects back to their inventories (the island and the API decide who may) and reading what
/// happened to an object (who did what, in which revision, when). Both only ask; the server's answer is what is shown.
/// </summary>
public partial class BuildAuditManager : Node
{
    private BuildNetworkSynchronizer _sync = null!;
    private BuildSelectionManager _selection = null!;
    private HttpRequest? _http;

    public event Action<string>? Said;
    public event Action<string>? HistoryReady;

    public void Initialize(BuildNetworkSynchronizer sync, BuildSelectionManager selection) { _sync = sync; _selection = selection; }

    /// <summary>"Devolver": asks the server to send the selected objects back to their owners' inventories (as a construction). It cannot be undone.</summary>
    public bool ReturnSelection()
    {
        if (_selection.Count == 0) { Said?.Invoke("Selecione os objetos que quer devolver."); return false; }
        if (!_sync.SendRaw(JsonSerializer.Serialize(new { operation = "return", objectIds = _selection.Ids.ToArray() }))) return false;
        Said?.Invoke("Devolvendo ao inventário dos donos… (não dá para desfazer; eles recebem de volta como construção)"); return true;
    }

    public void ShowHistory()
    {
        if (_selection.Count != 1) { Said?.Invoke("Selecione um só objeto para ver o histórico."); return; }
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is not { IsAuthenticated: true } || app is null || _sync.IslandId == Guid.Empty) { Said?.Invoke("Entre em uma ilha para ver o histórico."); return; }
        if (_http is null)
        {
            _http = new HttpRequest { Timeout = 10 }; AddChild(_http);
            _http.RequestCompleted += (_, code, _, body) =>
            {
                if (code is >= 200 and < 300) { var text = Format(body.GetStringFromUtf8(), _sync.MyUserId); if (text is null) Said?.Invoke("Histórico inválido recebido do servidor."); else HistoryReady?.Invoke(text); }
                else Said?.Invoke(code == 403 ? "Só o dono, o dono do terreno ou um administrador veem o histórico." : $"Não foi possível ler o histórico (HTTP {code}).");
            };
        }
        if (_http.GetHttpClientStatus() == Godot.HttpClient.Status.Disconnected) _http.Request($"{app.ApiBaseUrl}/api/v1/worlds/islands/{_sync.IslandId}/objects/{_selection.Ids[0]}/audit", [$"Authorization: Bearer {session.AccessToken}"]);
    }

    private static string OperationLabel(string operation) => operation switch { "create" => "Criado", "update" => "Alterado", "delete" => "Excluído", "return" => "Devolvido", "structure" => "Vínculo alterado", "protect" => "Proteção alterada", "properties" => "Propriedades alteradas", "buy" => "Comprado", "rez" => "Colocado", _ => operation };

    /// <summary>The audit answer (newest first) as lines: when, what, who (you, or the start of their id) and the revision. Null when the answer is not what was expected.</summary>
    public static string? Format(string json, Guid me)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var lines = document.RootElement.EnumerateArray().Select(e =>
            {
                var actor = Guid.TryParse(e.GetProperty("actorUserId").GetString(), out var id) ? (id == me ? "você" : id.ToString()[..8]) : "?";
                var at = DateTimeOffset.TryParse(e.GetProperty("at").GetString(), out var when) ? when.ToLocalTime().ToString("dd/MM HH:mm") : "";
                return $"{at}  ·  {OperationLabel(e.GetProperty("operation").GetString() ?? "")} por {actor}  ·  revisão {e.GetProperty("revision").GetInt32()}";
            }).ToList();
            return lines.Count == 0 ? "Sem registros." : string.Join("\n", lines);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
}
