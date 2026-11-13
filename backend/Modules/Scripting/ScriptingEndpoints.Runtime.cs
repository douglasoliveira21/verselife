using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Scripting;

public sealed record ScriptRuntimeItem(Guid ScriptId, string State, string StateText, long Events, long EventsDropped, long Errors, long Violations, long Suspensions, long ApiCalls, long Instructions, long AllocatedBytes, long MessagesSent,
    double CpuMilliseconds, double WorstEventMilliseconds, int ActiveTimers, int StateKeys, string? LastError, int LastErrorLine, string? SuspendedReason);
public sealed record ScriptLogItem(Guid ScriptId, string Level, string Event, string Text, string? Stack, double Milliseconds, DateTimeOffset At);
public sealed record ScriptGrantItem(Guid ScriptId, Guid SubjectId, string Permission);
public sealed record ScriptRuntimeReport(ScriptRuntimeItem[]? Runtime, ScriptLogItem[]? Logs, ScriptGrantItem[]? Grants);

/// <summary>What the island reports about how its scripts are doing, and what the owner sees of it: the state, the use of the island, the console and the permissions given to each script.</summary>
public static partial class ScriptingEndpoints
{
    private const int KeepLogLines = 200, MaxRuntimeItems = 400, MaxLogItems = 600, MaxGrantItems = 2000;
    private static readonly string[] Levels = ["info", "warning", "error"];

    /// <summary>The island server (service key) reports every script: state and counters, the console lines since the last report and the permissions in force. Everything is checked against the scripts of that island.</summary>
    internal static async Task<IResult> SaveRuntimeAsync(Guid instanceId, ScriptRuntimeReport report, HttpContext context, IConfiguration config, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Platform.PlatformEndpoints.HasValidServiceKey(context, config)) return Results.Unauthorized();
        var server = await db.ServerInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == instanceId && x.StoppedAt == null, ct);
        if (server is null) return Results.NotFound();
        if ((report.Runtime?.Length ?? 0) > MaxRuntimeItems || (report.Logs?.Length ?? 0) > MaxLogItems || (report.Grants?.Length ?? 0) > MaxGrantItems) return Results.BadRequest();
        var now = clock.GetUtcNow(); var runtime = report.Runtime ?? []; var logs = report.Logs ?? []; var grants = report.Grants ?? [];
        var ids = runtime.Select(x => x.ScriptId).Concat(logs.Select(x => x.ScriptId)).Concat(grants.Select(x => x.ScriptId)).Distinct().ToList();
        var allowed = (await db.ObjectScripts.AsNoTracking().Where(script => ids.Contains(script.Id) && db.BuildObjects.Any(o => o.Id == script.ObjectId && o.IslandId == server.IslandId)).Select(script => script.Id).ToListAsync(ct)).ToHashSet();

        var rows = await db.ScriptRuntime.Where(x => allowed.Contains(x.ScriptId)).ToDictionaryAsync(x => x.ScriptId, ct);
        foreach (var item in runtime.Where(x => allowed.Contains(x.ScriptId)))
        {
            if (!rows.TryGetValue(item.ScriptId, out var row)) { row = new ScriptRuntimeStatus { ScriptId = item.ScriptId, State = "", StateText = "" }; db.ScriptRuntime.Add(row); rows[item.ScriptId] = row; }
            row.State = Clip(item.State, 24); row.StateText = Clip(item.StateText, 40); row.Events = item.Events; row.EventsDropped = item.EventsDropped; row.Errors = item.Errors; row.Violations = item.Violations; row.Suspensions = item.Suspensions; row.ApiCalls = item.ApiCalls;
            row.Instructions = item.Instructions; row.AllocatedBytes = item.AllocatedBytes; row.MessagesSent = item.MessagesSent; row.CpuMilliseconds = item.CpuMilliseconds; row.WorstEventMilliseconds = item.WorstEventMilliseconds; row.ActiveTimers = item.ActiveTimers; row.StateKeys = item.StateKeys;
            row.LastError = Clip(item.LastError, 2000); row.LastErrorLine = item.LastErrorLine; row.SuspendedReason = Clip(item.SuspendedReason, 500); row.UpdatedAt = now;
        }
        foreach (var line in logs.Where(x => allowed.Contains(x.ScriptId)))
            db.ScriptLogs.Add(new ScriptLogLine { Id = Guid.NewGuid(), ScriptId = line.ScriptId, Level = Levels.Contains(line.Level) ? line.Level : "info", EventName = Clip(line.Event, 40), Text = Clip(line.Text, 500), StackTrace = Clip(line.Stack, 500), Milliseconds = line.Milliseconds, CreatedAt = line.At > now ? now : line.At });
        // the permissions of the scripts that were reported replace what was stored for them
        var reported = runtime.Select(x => x.ScriptId).Where(allowed.Contains).ToHashSet();
        if (reported.Count > 0)
        {
            var stored = await db.ScriptGrants.Where(x => reported.Contains(x.ScriptId) && x.SubjectId != Guid.Empty).ToListAsync(ct);   // the owner's grants are set only from the editor
            var wanted = grants.Where(g => reported.Contains(g.ScriptId) && g.SubjectId != Guid.Empty && g.Permission.Length is > 0 and <= 32).Select(g => (g.ScriptId, g.SubjectId, g.Permission)).ToHashSet();
            // what the owner took back stays gone while the island still reports it; once a report no longer has it the island has read the revocation
            var revoked = await db.ScriptGrantRevocations.Where(x => reported.Contains(x.ScriptId)).ToListAsync(ct);
            db.ScriptGrantRevocations.RemoveRange(revoked.Where(r => !wanted.Contains((r.ScriptId, r.SubjectId, r.Permission))));
            wanted.ExceptWith(revoked.Select(r => (r.ScriptId, r.SubjectId, r.Permission)));
            db.ScriptGrants.RemoveRange(stored.Where(g => !wanted.Contains((g.ScriptId, g.SubjectId, g.Permission))));
            var have = stored.Select(g => (g.ScriptId, g.SubjectId, g.Permission)).ToHashSet();
            foreach (var grant in wanted.Where(g => !have.Contains(g))) db.ScriptGrants.Add(new ScriptGrant { ScriptId = grant.ScriptId, SubjectId = grant.SubjectId, Permission = grant.Permission, GrantedAt = now });
        }
        await db.SaveChangesAsync(ct);
        // only the most recent lines of each script are kept
        foreach (var id in logs.Select(x => x.ScriptId).Where(allowed.Contains).Distinct())
        {
            var old = await db.ScriptLogs.Where(x => x.ScriptId == id).OrderByDescending(x => x.CreatedAt).Skip(KeepLogLines).ToListAsync(ct);
            if (old.Count > 0) { db.ScriptLogs.RemoveRange(old); await db.SaveChangesAsync(ct); }
        }
        return Results.Ok(new { runtime = rows.Count, logs = logs.Length });
    }

    private static string Clip(string? text, int max) => string.IsNullOrEmpty(text) ? string.Empty : text.Length > max ? text[..max] : text;

    /// <summary>The state and the use of the island of a script, its console (the last 100 lines) and the permissions it holds: what the editor shows below the code.</summary>
    internal static async Task<IResult> RuntimeAsync(Guid islandId, Guid objectId, Guid scriptId, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        if (!await CanEditAsync(db, islandId, user, ct)) return Results.Forbid();
        var script = await db.ObjectScripts.AsNoTracking().Where(x => x.Id == scriptId && x.ObjectId == objectId).Select(x => new { x.Id, x.Status, x.Version }).SingleOrDefaultAsync(ct);
        if (script is null) return Results.NotFound();
        var runtime = await db.ScriptRuntime.AsNoTracking().Where(x => x.ScriptId == scriptId).SingleOrDefaultAsync(ct);
        var logs = (await db.ScriptLogs.AsNoTracking().Where(x => x.ScriptId == scriptId).OrderByDescending(x => x.CreatedAt).Take(100).Select(x => new { x.Level, x.EventName, x.Text, x.StackTrace, x.Milliseconds, x.CreatedAt }).ToListAsync(ct)).AsEnumerable().Reverse().ToList();
        var grants = await db.ScriptGrants.AsNoTracking().Where(x => x.ScriptId == scriptId).OrderBy(x => x.Permission).Select(x => new { x.SubjectId, x.Permission, x.GrantedAt }).ToListAsync(ct);
        return Results.Ok(new { scriptId, status = script.Status, version = script.Version, runtime, logs, grants });
    }

    /// <summary>Pauses a script (it receives nothing and its timers stand still) or lets it go on. The island applies it the next time it reads its scripts.</summary>
    internal static async Task<IResult> PauseAsync(Guid islandId, Guid objectId, Guid scriptId, bool pause, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        if (!await CanEditAsync(db, islandId, user, ct)) return Results.Forbid();
        var script = await db.ObjectScripts.SingleOrDefaultAsync(x => x.Id == scriptId && x.ObjectId == objectId, ct);
        if (script is null) return Results.NotFound();
        if (script.Status == "stopped") return Results.Json(new { error = "stopped" }, statusCode: StatusCodes.Status409Conflict);
        script.Status = pause ? "paused" : "active"; script.UpdatedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct);
        return Results.Ok(new { status = script.Status });
    }

    /// <summary>The scripts of the island that use it the most (by CPU), with their state: where an administrator looks first when the island is slow.</summary>
    internal static async Task<IResult> UsageAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        if (!await CanEditAsync(db, islandId, user, ct)) return Results.Forbid();
        var rows = await (from runtime in db.ScriptRuntime.AsNoTracking()
                          join script in db.ObjectScripts.AsNoTracking() on runtime.ScriptId equals script.Id
                          join obj in db.BuildObjects.AsNoTracking() on script.ObjectId equals obj.Id
                          where obj.IslandId == islandId && obj.DeletedAt == null
                          orderby runtime.CpuMilliseconds descending
                          select new { script.Id, script.Name, script.ObjectId, ObjectName = obj.Name, script.OwnerUserId, runtime.State, runtime.StateText, runtime.Events, runtime.EventsDropped, runtime.Errors, runtime.Violations, runtime.Suspensions, runtime.CpuMilliseconds, runtime.WorstEventMilliseconds, runtime.Instructions, runtime.AllocatedBytes, runtime.ActiveTimers, runtime.UpdatedAt }).Take(50).ToListAsync(ct);
        var totals = new { scripts = rows.Count, cpuMilliseconds = rows.Sum(r => r.CpuMilliseconds), events = rows.Sum(r => r.Events), errors = rows.Sum(r => r.Errors), suspended = rows.Count(r => r.State == "Suspended"), perOwner = rows.GroupBy(r => r.OwnerUserId).Select(g => new { owner = g.Key, scripts = g.Count(), cpuMilliseconds = g.Sum(r => r.CpuMilliseconds), events = g.Sum(r => r.Events), errors = g.Sum(r => r.Errors) }).OrderByDescending(x => x.cpuMilliseconds).ToList() };
        return Results.Ok(new { totals, scripts = rows });
    }
}

public static partial class ScriptingEndpoints
{
    private static string Describe(LifeVerse.Scripting.ScriptCompilation compiled)
    {
        var first = compiled.Diagnostics.First(d => d.Severity == "error");
        return first.Line > 0 ? $"linha {first.Line}: {first.Message}" : first.Message;
    }
}

public static partial class ScriptingEndpoints
{
    /// <summary>The reference of the Lua API as it really is (the same catalog the editor completes from and the manual is written from), and the permissions and states scripts have.</summary>
    internal static IResult Reference() => Results.Ok(new
    {
        functions = LifeVerse.Scripting.ScriptCatalog.Functions.Select(f => new { f.Category, f.Namespace, f.Name, f.Signature, f.Description, f.Returns, f.Permission, f.Limits, f.Errors, f.Example }),
        events = LifeVerse.Scripting.ScriptCatalog.Events.Select(e => new { e.Name, e.When, e.Fields, e.Example }),
        permissions = LifeVerse.Scripting.ScriptPermission.All.Select(p => new { name = p, description = LifeVerse.Scripting.ScriptPermission.Describe(p), askedOfAvatars = LifeVerse.Scripting.ScriptPermission.OfAvatars.Contains(p) }),
        states = Enum.GetValues<LifeVerse.Scripting.ScriptLifecycle>().Select(s => new { name = s.ToString(), text = LifeVerse.Scripting.ScriptLifecycleRules.Describe(s) })
    });
}

public sealed record OwnerPermissionRequest(bool Granted);

public static partial class ScriptingEndpoints
{
    /// <summary>The owner gives a script a permission over the object (or takes it back): physics, media, outside addresses, NPCs. The island applies it the next time it reads its scripts (within about 15 seconds).</summary>
    /// <summary>The owner takes back a permission an avatar gave the script: it ends at once here and the island server drops it the next time it reads its content.</summary>
    internal static async Task<IResult> RevokeAvatarPermissionAsync(Guid islandId, Guid objectId, Guid scriptId, Guid subjectId, string permission, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        if (!await CanEditAsync(db, islandId, user, ct)) return Results.Forbid();
        permission = permission.ToUpperInvariant();
        if (subjectId == Guid.Empty || !LifeVerse.Scripting.ScriptPermission.OfAvatars.Contains(permission)) return Results.Json(new { error = "bad_permission", allowed = LifeVerse.Scripting.ScriptPermission.OfAvatars }, statusCode: StatusCodes.Status400BadRequest);
        if (!await db.ObjectScripts.AsNoTracking().AnyAsync(x => x.Id == scriptId && x.ObjectId == objectId, ct)) return Results.NotFound();
        var row = await db.ScriptGrants.SingleOrDefaultAsync(x => x.ScriptId == scriptId && x.SubjectId == subjectId && x.Permission == permission, ct);
        if (row is null) return Results.NotFound();
        db.ScriptGrants.Remove(row);
        if (!await db.ScriptGrantRevocations.AnyAsync(x => x.ScriptId == scriptId && x.SubjectId == subjectId && x.Permission == permission, ct)) db.ScriptGrantRevocations.Add(new ScriptGrantRevocation { ScriptId = scriptId, SubjectId = subjectId, Permission = permission, RevokedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { permission, subjectId, revoked = true });
    }

    internal static async Task<IResult> SetOwnerPermissionAsync(Guid islandId, Guid objectId, Guid scriptId, string permission, OwnerPermissionRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        if (!await CanEditAsync(db, islandId, user, ct)) return Results.Forbid();
        permission = permission.ToUpperInvariant();
        if (!LifeVerse.Scripting.ScriptPermission.OfOwner.Contains(permission)) return Results.Json(new { error = "bad_permission", allowed = LifeVerse.Scripting.ScriptPermission.OfOwner }, statusCode: StatusCodes.Status400BadRequest);
        if (!await db.ObjectScripts.AsNoTracking().AnyAsync(x => x.Id == scriptId && x.ObjectId == objectId, ct)) return Results.NotFound();
        var row = await db.ScriptGrants.SingleOrDefaultAsync(x => x.ScriptId == scriptId && x.SubjectId == Guid.Empty && x.Permission == permission, ct);
        if (request.Granted && row is null) db.ScriptGrants.Add(new ScriptGrant { ScriptId = scriptId, SubjectId = Guid.Empty, Permission = permission, GrantedAt = clock.GetUtcNow() });
        else if (!request.Granted && row is not null) db.ScriptGrants.Remove(row);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { permission, granted = request.Granted });
    }
}
