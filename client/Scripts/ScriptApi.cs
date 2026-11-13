using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

public sealed record ScriptSummary(Guid Id, string Name, int Version, string Status);
public sealed record ScriptDetail(Guid Id, string Name, string Source, int Version, string Status);
public sealed record ScriptRun(string EventName, string Status, int Instructions, string Error, string At);
public sealed record ScriptTemplate(string Name, string EventName, string Source);
public sealed record ScriptResult(bool Ok, string Message, Guid Id = default);

/// <summary>
/// The scripts that live inside an object (Lua, run by the island server on that object): list, read with the source, create, save, delete and the last runs. Every
/// call goes through one transport, so tests can answer for the server; the real one is <see cref="HttpTransport"/>.
/// </summary>
public sealed partial class ScriptApi(Func<string, string, string?, Task<(long Status, string Body)>> transport, Func<Guid> island)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private string Base(Guid objectId) => $"/api/v1/worlds/{island()}/objects/{objectId}/scripts";

    private static string Explain(long status, string body)
    {
        if (status == 0) return "Não foi possível falar com o servidor.";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object) foreach (var field in errors.EnumerateObject()) foreach (var message in field.Value.EnumerateArray()) return message.GetString() ?? "Script recusado.";
                if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String) return error.GetString() switch { "no_modify" => "Este objeto não pode ser modificado: o script não pode ser alterado.", var other => other ?? "Recusado." };
            }
        }
        catch (JsonException) { }
        return status switch { 401 => "Sessão expirada.", 403 => "Você não pode editar scripts desta ilha.", 404 => "Script não encontrado.", _ => $"O servidor recusou ({status})." };
    }

    public async Task<List<ScriptSummary>?> ListAsync(Guid objectId)
    {
        var (status, body) = await transport("GET", Base(objectId), null);
        if (status is < 200 or >= 300) return null;
        try
        {
            using var doc = JsonDocument.Parse(body); var list = new List<ScriptSummary>();
            foreach (var item in doc.RootElement.EnumerateArray()) list.Add(new ScriptSummary(item.GetProperty("id").GetGuid(), item.GetProperty("name").GetString() ?? "Script", item.GetProperty("version").GetInt32(), item.GetProperty("status").GetString() ?? "active"));
            return list;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    public async Task<ScriptDetail?> GetAsync(Guid objectId, Guid scriptId)
    {
        var (status, body) = await transport("GET", $"{Base(objectId)}/{scriptId}", null);
        if (status is < 200 or >= 300) return null;
        try { using var doc = JsonDocument.Parse(body); var r = doc.RootElement; return new ScriptDetail(r.GetProperty("id").GetGuid(), r.GetProperty("name").GetString() ?? "Script", r.GetProperty("source").GetString() ?? "", r.GetProperty("version").GetInt32(), r.GetProperty("status").GetString() ?? "active"); }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    /// <summary>Creates a script in the object and gives back its id; the message says why not (rights, source rejected by the safety rules…).</summary>
    public async Task<ScriptResult> CreateAsync(Guid objectId, string name, string source, string eventName)
    {
        var (status, body) = await transport("POST", Base(objectId), JsonSerializer.Serialize(new { name, source, eventName }, Web));
        if (status is < 200 or >= 300) return new ScriptResult(false, Explain(status, body));
        try { using var doc = JsonDocument.Parse(body); return new ScriptResult(true, "Script criado.", doc.RootElement.GetProperty("id").GetGuid()); }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { return new ScriptResult(false, "Resposta inválida do servidor."); }
    }

    /// <summary>Saves the name and the source; a stopped script runs again. The island picks the new version up within about 15 seconds.</summary>
    public async Task<ScriptResult> SaveAsync(Guid objectId, Guid scriptId, string name, string source)
    {
        var (status, body) = await transport("PUT", $"{Base(objectId)}/{scriptId}", JsonSerializer.Serialize(new { name, source }, Web));
        return status is >= 200 and < 300 ? new ScriptResult(true, "Script salvo. A ilha aplica a nova versão em até 15 segundos.", scriptId) : new ScriptResult(false, Explain(status, body));
    }

    /// <summary>Checks a source on the server without saving it: ok, or the first error with its line (0 when it has none).</summary>
    public async Task<(bool Valid, int Line, string Message)> ValidateAsync(Guid objectId, string source)
    {
        var (status, body) = await transport("POST", $"{Base(objectId)}/validate", JsonSerializer.Serialize(new { source }, Web));
        if (status is < 200 or >= 300) return (false, 0, Explain(status, body));
        try
        {
            using var doc = JsonDocument.Parse(body); var root = doc.RootElement;
            if (root.GetProperty("valid").GetBoolean()) return (true, 0, "Sem erros de sintaxe.");
            var first = root.GetProperty("errors")[0];
            return (false, first.GetProperty("line").GetInt32(), first.GetProperty("message").GetString() ?? "Erro no script.");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException) { return (false, 0, "Resposta inválida do servidor."); }
    }

    /// <summary>Restarts one script (<paramref name="scriptId"/>) or all of the object's scripts; the source is never touched.</summary>
    public async Task<ScriptResult> ResetAsync(Guid objectId, Guid? scriptId = null)
    {
        var (status, body) = await transport("POST", scriptId is { } id ? $"{Base(objectId)}/{id}/reset" : $"{Base(objectId)}/reset", "{}");
        if (status is < 200 or >= 300) return new ScriptResult(false, Explain(status, body));
        var count = 0; try { using var doc = JsonDocument.Parse(body); count = doc.RootElement.GetProperty("reset").GetInt32(); } catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
        return new ScriptResult(true, count == 1 ? "1 script reiniciado." : $"{count} scripts reiniciados.");
    }

    public async Task<ScriptResult> DeleteAsync(Guid objectId, Guid scriptId)
    {
        var (status, body) = await transport("DELETE", $"{Base(objectId)}/{scriptId}", null);
        return status is >= 200 and < 300 ? new ScriptResult(true, "Script removido do objeto.") : new ScriptResult(false, Explain(status, body));
    }

    public async Task<List<ScriptRun>> RunsAsync(Guid objectId, Guid scriptId)
    {
        var (status, body) = await transport("GET", $"{Base(objectId)}/{scriptId}/executions", null); var runs = new List<ScriptRun>();
        if (status is < 200 or >= 300) return runs;
        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var item in doc.RootElement.EnumerateArray()) runs.Add(new ScriptRun(item.GetProperty("eventName").GetString() ?? "", item.GetProperty("status").GetString() ?? "", item.GetProperty("instructionCount").GetInt32(), item.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "", item.TryGetProperty("createdAt", out var t) ? t.GetString() ?? "" : ""));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
        return runs;
    }

    public async Task<List<ScriptTemplate>> TemplatesAsync()
    {
        var (status, body) = await transport("GET", "/api/v1/scripting/templates", null); var list = new List<ScriptTemplate>();
        if (status is >= 200 and < 300)
            try { using var doc = JsonDocument.Parse(body); foreach (var item in doc.RootElement.EnumerateArray()) list.Add(new ScriptTemplate(item.GetProperty("name").GetString() ?? "modelo", item.GetProperty("eventName").GetString() ?? "on_start", item.GetProperty("source").GetString() ?? "")); }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
        return list.Count > 0 ? list : [DefaultTemplate];
    }

    public static readonly ScriptTemplate DefaultTemplate = new("Porta ao toque", "on_touch",
        "-- Roda no servidor da ilha, neste objeto.\n-- Eventos: on_start, on_touch, on_enter, on_timer, on_chat, on_message\nfunction on_touch(event)\n  object.say(\"Olá! Você me tocou.\")\nend\n");

    /// <summary>The real transport: one HTTP call with the session token.</summary>
    public static Func<string, string, string?, Task<(long, string)>> HttpTransport(Node owner)
    {
        return async (method, path, body) =>
        {
            var session = owner.GetNode<SessionManager>("/root/SessionManager"); var app = owner.GetNode<AppManager>("/root/AppManager");
            var request = new HttpRequest { Timeout = 20 }; owner.AddChild(request);
            try
            {
                var verb = method switch { "GET" => Godot.HttpClient.Method.Get, "PUT" => Godot.HttpClient.Method.Put, "DELETE" => Godot.HttpClient.Method.Delete, _ => Godot.HttpClient.Method.Post };
                var error = request.Request(app.ApiBaseUrl + path, [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"], verb, body ?? string.Empty);
                if (error != Error.Ok) return (0L, "{}");
                var done = await owner.ToSignal(request, HttpRequest.SignalName.RequestCompleted);
                return ((long)done[0].AsInt64() == (long)HttpRequest.Result.Success ? done[1].AsInt64() : 0L, Encoding.UTF8.GetString(done[3].AsByteArray()));
            }
            finally { request.QueueFree(); }
        };
    }
}
