using System.Text.Json;

namespace LifeVerse.Client.Scripts;

public sealed record ScriptLogView(string Level, string Event, string Text, string Stack, double Milliseconds, string At);
public sealed record ScriptGrantView(string Subject, string Permission);
public sealed record ScriptDiagnosticView(string Severity, int Line, string Message);
public sealed record ScriptRuntimeView(string Status, int Version, bool Reported, string State, string StateText, long Events, long EventsDropped, long Errors, long Violations, long Suspensions, long ApiCalls, long Instructions, long AllocatedBytes,
    long MessagesSent, double CpuMilliseconds, double WorstEventMilliseconds, int ActiveTimers, int StateKeys, string LastError, int LastErrorLine, string SuspendedReason, IReadOnlyList<ScriptLogView> Logs, IReadOnlyList<ScriptGrantView> Grants);
public sealed record ScriptDocFunction(string Category, string Namespace, string Name, string Signature, string Description, string Returns, string Permission, string Limits, string Errors, string Example);
public sealed record ScriptDocEvent(string Name, string When, string Fields, string Example);
public sealed record ScriptReference(IReadOnlyList<ScriptDocFunction> Functions, IReadOnlyList<ScriptDocEvent> Events);

/// <summary>The part of the script API about running: how a script is doing and its console, the full check of a source (errors and warnings), stopping and pausing, and the reference of the language.</summary>
public sealed partial class ScriptApi
{
    private static string Text(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static long Long(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
    private static double Real(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) ? n : 0;

    /// <summary>The state and use of the island of a script, its console and the permissions it holds. <c>Reported</c> is false while the island has not reported anything about it yet.</summary>
    public async Task<ScriptRuntimeView?> RuntimeAsync(Guid objectId, Guid scriptId)
    {
        var (status, body) = await transport("GET", $"{Base(objectId)}/{scriptId}/runtime", null);
        if (status is < 200 or >= 300) return null;
        try
        {
            using var doc = JsonDocument.Parse(body); var root = doc.RootElement; var runtime = root.TryGetProperty("runtime", out var r) && r.ValueKind == JsonValueKind.Object ? r : default;
            var logs = root.TryGetProperty("logs", out var l) && l.ValueKind == JsonValueKind.Array ? l.EnumerateArray().Select(x => new ScriptLogView(Text(x, "level"), Text(x, "eventName"), Text(x, "text"), Text(x, "stackTrace"), Real(x, "milliseconds"), Text(x, "createdAt"))).ToList() : [];
            var grants = root.TryGetProperty("grants", out var g) && g.ValueKind == JsonValueKind.Array ? g.EnumerateArray().Select(x => new ScriptGrantView(Text(x, "subjectId"), Text(x, "permission"))).ToList() : [];
            return new ScriptRuntimeView(Text(root, "status"), (int)Long(root, "version"), runtime.ValueKind == JsonValueKind.Object, Text(runtime, "state"), Text(runtime, "stateText"), Long(runtime, "events"), Long(runtime, "eventsDropped"), Long(runtime, "errors"), Long(runtime, "violations"), Long(runtime, "suspensions"),
                Long(runtime, "apiCalls"), Long(runtime, "instructions"), Long(runtime, "allocatedBytes"), Long(runtime, "messagesSent"), Real(runtime, "cpuMilliseconds"), Real(runtime, "worstEventMilliseconds"), (int)Long(runtime, "activeTimers"), (int)Long(runtime, "stateKeys"),
                Text(runtime, "lastError"), (int)Long(runtime, "lastErrorLine"), Text(runtime, "suspendedReason"), logs, grants);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    /// <summary>Checks a source on the server without saving it: errors (which stop it from being saved) and warnings (typos, unknown events), each with its line, and the events the script handles.</summary>
    public async Task<(bool Valid, IReadOnlyList<ScriptDiagnosticView> Diagnostics, IReadOnlyList<string> Handlers, string Message)> CheckAsync(Guid objectId, string source)
    {
        var (status, body) = await transport("POST", $"{Base(objectId)}/validate", JsonSerializer.Serialize(new { source }, Web));
        if (status is < 200 or >= 300) return (false, [], [], Explain(status, body));
        try
        {
            using var doc = JsonDocument.Parse(body); var root = doc.RootElement; var list = new List<ScriptDiagnosticView>();
            foreach (var (name, severity) in new[] { ("errors", "error"), ("warnings", "warning") })
                if (root.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array) foreach (var item in items.EnumerateArray()) list.Add(new ScriptDiagnosticView(severity, (int)Long(item, "line"), Text(item, "message")));
            var handlers = root.TryGetProperty("handlers", out var h) && h.ValueKind == JsonValueKind.Array ? h.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];
            return (root.GetProperty("valid").GetBoolean(), list, handlers, "");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { return (false, [], [], "Resposta inválida do servidor."); }
    }

    /// <summary>Stops a script until it is saved or restarted.</summary>
    public async Task<ScriptResult> StopAsync(Guid objectId, Guid scriptId)
    {
        var (status, body) = await transport("POST", $"{Base(objectId)}/{scriptId}/stop", "{}");
        return status is >= 200 and < 300 ? new ScriptResult(true, "Script parado. Reiniciar ou salvar liga de novo.", scriptId) : new ScriptResult(false, Explain(status, body));
    }

    /// <summary>Pauses a script (no events, timers stand still) or lets it go on.</summary>
    public async Task<ScriptResult> PauseAsync(Guid objectId, Guid scriptId, bool pause)
    {
        var (status, body) = await transport("POST", $"{Base(objectId)}/{scriptId}/{(pause ? "pause" : "resume")}", "{}");
        if (status == 409) return new ScriptResult(false, "O script está parado: reinicie para ligar de novo.");
        return status is >= 200 and < 300 ? new ScriptResult(true, pause ? "Script pausado." : "Script continua.", scriptId) : new ScriptResult(false, Explain(status, body));
    }

    /// <summary>The reference of the language as the server documents it (what the editor completes from and the manual shows).</summary>
    public async Task<ScriptReference?> ReferenceAsync()
    {
        var (status, body) = await transport("GET", "/api/v1/scripting/reference", null);
        if (status is < 200 or >= 300) return null;
        try
        {
            using var doc = JsonDocument.Parse(body); var root = doc.RootElement;
            var functions = root.GetProperty("functions").EnumerateArray().Select(f => new ScriptDocFunction(Text(f, "category"), Text(f, "namespace"), Text(f, "name"), Text(f, "signature"), Text(f, "description"), Text(f, "returns"), Text(f, "permission"), Text(f, "limits"), Text(f, "errors"), Text(f, "example"))).ToList();
            var events = root.GetProperty("events").EnumerateArray().Select(e => new ScriptDocEvent(Text(e, "name"), Text(e, "when"), Text(e, "fields"), Text(e, "example"))).ToList();
            return new ScriptReference(functions, events);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
}

public sealed partial class ScriptApi
{
    /// <summary>The owner gives the script a permission over the object (or takes it back). The island applies it within about 15 seconds.</summary>
    public async Task<ScriptResult> SetOwnerPermissionAsync(Guid objectId, Guid scriptId, string permission, bool granted)
    {
        var (status, body) = await transport("PUT", $"{Base(objectId)}/{scriptId}/permissions/{permission}", JsonSerializer.Serialize(new { granted }, Web));
        return status is >= 200 and < 300 ? new ScriptResult(true, granted ? $"{permission} concedida: a ilha aplica em até 15 segundos." : $"{permission} retirada.") : new ScriptResult(false, Explain(status, body));
    }
}
