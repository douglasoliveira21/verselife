using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Platform;

public sealed record ScriptStateEntry(Guid ScriptId, Dictionary<string, string> Values);
public sealed record ScriptStateBatch(ScriptStateEntry[] States);

/// <summary>
/// Internal endpoints (service key) the island server uses to run object scripts: it reads the objects and active
/// scripts of its island, and writes back the key/value state the scripts keep.
/// </summary>
public static class IslandContentEndpoints
{
    private const int MaxKeysPerScript = 32;
    private const int MaxKeyLength = 64;
    private const int MaxValueLength = 2100;

    public static IEndpointRouteBuilder MapIslandContentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/platform/servers/{instanceId:guid}/island-content", ContentAsync).WithTags("Platform");
        endpoints.MapPut("/api/v1/platform/servers/{instanceId:guid}/script-state", SaveStateAsync).WithTags("Platform");
        endpoints.MapPut("/api/v1/platform/servers/{instanceId:guid}/script-runtime", Scripting.ScriptingEndpoints.SaveRuntimeAsync).WithTags("Platform");
        endpoints.MapPost("/api/v1/platform/servers/{instanceId:guid}/script-http", Scripting.ScriptingEndpoints.ScriptHttpAsync).WithTags("Platform");
        endpoints.MapPost("/api/v1/platform/servers/{instanceId:guid}/script-payment", Scripting.ScriptingEndpoints.ScriptPaymentAsync).WithTags("Platform");
        endpoints.MapPost("/api/v1/platform/servers/{instanceId:guid}/script-give-item", Scripting.ScriptingEndpoints.ScriptGiveItemAsync).WithTags("Platform");
        endpoints.MapGet("/api/v1/platform/servers/{instanceId:guid}/vehicles", VehiclesAsync).WithTags("Platform");
        return endpoints;
    }

    private static async Task<IResult> ContentAsync(Guid instanceId, HttpContext context, IConfiguration config, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!PlatformEndpoints.HasValidServiceKey(context, config)) return Results.Unauthorized();
        var server = await db.ServerInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == instanceId && x.StoppedAt == null, ct);
        if (server is null) return Results.NotFound();
        var objects = await db.BuildObjects.AsNoTracking().Where(x => x.IslandId == server.IslandId && x.DeletedAt == null)
            .Select(x => new
            {
                x.Id, x.OwnerUserId, x.CreatorUserId, x.GroupId, x.ParentObjectId, x.Name, x.PrimitiveType,
                x.PositionX, x.PositionY, x.PositionZ, x.RotationX, x.RotationY, x.RotationZ, x.ScaleX, x.ScaleY, x.ScaleZ,
                x.MaterialJson, x.GeometryJson, x.PhysicsJson, x.ContentJson, x.Revision
            }).ToListAsync(ct);
        var scripts = await db.ObjectScripts.AsNoTracking()
            .Where(script => (script.Status == "active" || script.Status == "paused") && db.BuildObjects.Any(o => o.Id == script.ObjectId && o.IslandId == server.IslandId && o.DeletedAt == null))
            .Select(script => new { script.Id, script.ObjectId, script.OwnerUserId, script.Name, script.Source, script.Version, script.InstructionBudget, paused = script.Status == "paused" })
            .ToListAsync(ct);
        var scriptIds = scripts.Select(s => s.Id).ToList();
        var state = (await db.ScriptStates.AsNoTracking().Where(x => scriptIds.Contains(x.ScriptId)).Select(x => new { x.ScriptId, x.Key, x.ValueJson }).ToListAsync(ct))
            .GroupBy(x => x.ScriptId).ToDictionary(group => group.Key, group => group.ToDictionary(x => x.Key, x => x.ValueJson));
        var grants = await db.ScriptGrants.AsNoTracking().Where(x => scriptIds.Contains(x.ScriptId)).Select(x => new { x.ScriptId, x.SubjectId, x.Permission }).ToListAsync(ct);
        var revoked = await db.ScriptGrantRevocations.AsNoTracking().Where(x => scriptIds.Contains(x.ScriptId)).Select(x => new { x.ScriptId, x.SubjectId, x.Permission }).ToListAsync(ct);
        var islandName = await db.Islands.AsNoTracking().Where(x => x.Id == server.IslandId).Select(x => x.Name).SingleOrDefaultAsync(ct) ?? "Ilha";
        return Results.Ok(new { islandId = server.IslandId, islandName, objects, scripts, state, grants, revoked });
    }

    /// The vehicles that belong on this island; the island server simulates them and adds new ones as they appear.
    private static async Task<IResult> VehiclesAsync(Guid instanceId, HttpContext context, IConfiguration config, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!PlatformEndpoints.HasValidServiceKey(context, config)) return Results.Unauthorized();
        var server = await db.ServerInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == instanceId && x.StoppedAt == null, ct);
        if (server is null) return Results.NotFound();
        var vehicles = await db.Vehicles.AsNoTracking().Where(x => x.IslandId == server.IslandId && x.DeletedAt == null)
            .Select(x => new { x.Id, x.Name, x.VehicleType, x.PositionX, x.PositionZ, x.RotationY, x.MaxSpeed, x.OwnerUserId }).Take(64).ToListAsync(ct);
        return Results.Ok(new { vehicles });
    }

    private static async Task<IResult> SaveStateAsync(Guid instanceId, ScriptStateBatch batch, HttpContext context, IConfiguration config, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!PlatformEndpoints.HasValidServiceKey(context, config)) return Results.Unauthorized();
        var server = await db.ServerInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == instanceId && x.StoppedAt == null, ct);
        if (server is null) return Results.NotFound();
        if (batch.States is null || batch.States.Length > 200) return Results.BadRequest();
        var now = clock.GetUtcNow();
        var ids = batch.States.Select(x => x.ScriptId).Distinct().ToList();
        var allowed = await db.ObjectScripts.AsNoTracking().Where(script => ids.Contains(script.Id) && db.BuildObjects.Any(o => o.Id == script.ObjectId && o.IslandId == server.IslandId)).Select(script => script.Id).ToListAsync(ct);
        var existing = await db.ScriptStates.Where(x => allowed.Contains(x.ScriptId)).ToListAsync(ct);
        var saved = 0;
        foreach (var entry in batch.States.Where(x => allowed.Contains(x.ScriptId)))
        {
            if (entry.Values is null || entry.Values.Count > MaxKeysPerScript || entry.Values.Any(pair => pair.Key.Length is < 1 or > MaxKeyLength || pair.Value.Length > MaxValueLength)) return Results.BadRequest();
            var current = existing.Where(x => x.ScriptId == entry.ScriptId).ToList();
            foreach (var row in current.Where(row => !entry.Values.ContainsKey(row.Key))) db.ScriptStates.Remove(row);
            foreach (var (key, value) in entry.Values)
            {
                var row = current.FirstOrDefault(x => x.Key == key);
                if (row is null) db.ScriptStates.Add(new ScriptState { Id = Guid.NewGuid(), ScriptId = entry.ScriptId, Key = key, ValueJson = value, UpdatedAt = now });
                else if (row.ValueJson != value) { row.ValueJson = value; row.UpdatedAt = now; }
            }
            saved++;
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { saved });
    }
}
