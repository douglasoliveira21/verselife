using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace LifeVerse.Api.Modules.Scripting;

public sealed class LuaRuntimeOptions
{
    public string WorkerPath { get; init; } = "lua";
    public int TimeoutMilliseconds { get; init; } = 250;
    public int MaxSourceBytes { get; init; } = 64 * 1024;
    public int MaxInstructions { get; init; } = 10000;
    public int MaxMemoryKb { get; init; } = 256;
    public int MaxEventsPerSecond { get; init; } = 20;
}

public sealed record LuaValidationResult(bool Valid, string? Error, int EstimatedInstructions);
public sealed record LuaExecutionResult(string Status, string Output, string? Error, int Instructions);

public static class LuaSafetyValidator
{
    private static readonly string[] Events = ["on_start", "on_touch", "on_collision", "on_enter", "on_leave", "on_timer", "on_chat", "on_message", "on_owner_changed", "on_object_rezzed", "on_object_removed"];
    private static readonly string[] Forbidden = ["os.", "io.", "debug", "require", "dofile", "loadfile", "package", "socket", "http", "process", "ffi", "userdata", "collectgarbage"];
    public static IReadOnlyList<string> AllowedEvents => Events;
    public static LuaValidationResult Validate(string source, LuaRuntimeOptions options)
    {
        if (string.IsNullOrWhiteSpace(source)) return new(false, "Script is empty.", 0); if (Encoding.UTF8.GetByteCount(source) > options.MaxSourceBytes) return new(false, "Script exceeds the source quota.", 0);
        var lowered = source.ToLowerInvariant(); var forbidden = Forbidden.FirstOrDefault(lowered.Contains); if (forbidden is not null) return new(false, $"Forbidden Lua API: {forbidden}", 0); if (lowered.Contains("while true") || lowered.Contains("repeat\n") && !lowered.Contains("until")) return new(false, "Potential infinite loop rejected.", options.MaxInstructions + 1);
        var instructions = source.Count(c => c is '\n' or ';') + source.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Length; if (instructions > options.MaxInstructions) return new(false, "Instruction budget exceeded.", instructions); return new(true, null, instructions);
    }
    public static bool IsEvent(string value) => Events.Contains(value, StringComparer.Ordinal);
}

public sealed class LuaSandboxService(LuaRuntimeOptions options, ILogger<LuaSandboxService> logger)
{
    private readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> eventWindows = new();

    public bool TryAcquireEvent(Guid scriptId)
    {
        var queue = eventWindows.GetOrAdd(scriptId, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            var now = DateTimeOffset.UtcNow;
            while (queue.Count > 0 && now - queue.Peek() > TimeSpan.FromSeconds(1)) queue.Dequeue();
            if (queue.Count >= options.MaxEventsPerSecond) return false;
            queue.Enqueue(now);
            return true;
        }
    }

    public async Task<LuaExecutionResult> ExecuteAsync(string source, string eventName, string payloadJson, CancellationToken ct)
    {
        var validation = LuaSafetyValidator.Validate(source, options); if (!validation.Valid) return new("rejected", string.Empty, validation.Error, validation.EstimatedInstructions); if (!LuaSafetyValidator.IsEvent(eventName)) return new("rejected", string.Empty, "Unsupported event.", 0);
        var start = new ProcessStartInfo { FileName = options.WorkerPath, Arguments = "--sandbox --stdin", UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }; start.Environment.Clear(); start.Environment["LUA_PATH"] = ""; start.Environment["LUA_CPATH"] = ""; start.Environment["LIFEVERSE_LUA_MEMORY_KB"] = options.MaxMemoryKb.ToString(); start.Environment["LIFEVERSE_LUA_TIMEOUT_MS"] = options.TimeoutMilliseconds.ToString();
        Process? process = null;
        try
        {
            process = Process.Start(start); if (process is null) return new("worker_unavailable", string.Empty, "Lua worker could not start.", 0); using (process) { var request = JsonSerializer.Serialize(new { source, eventName, payloadJson }); await process.StandardInput.WriteAsync(request.AsMemory(), ct); await process.StandardInput.WriteLineAsync(); await process.StandardInput.FlushAsync(ct); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(options.TimeoutMilliseconds); var output = await process.StandardOutput.ReadToEndAsync(timeout.Token); await process.WaitForExitAsync(timeout.Token); if (process.ExitCode != 0) return new("failed", output, await process.StandardError.ReadToEndAsync(ct), validation.EstimatedInstructions); return new("completed", output, null, validation.EstimatedInstructions); }
        }
        catch (OperationCanceledException) { try { if (process is not null && !process.HasExited) process.Kill(entireProcessTree: true); } catch { } return new("timeout", string.Empty, "Lua execution timed out and was cancelled.", options.MaxInstructions); }
        catch (Exception exception) { logger.LogWarning("Lua worker failure: {Error}", exception.Message); return new("worker_unavailable", string.Empty, "Lua worker unavailable.", 0); }
    }
}
