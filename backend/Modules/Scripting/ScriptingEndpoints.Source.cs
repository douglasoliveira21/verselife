using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Scripting;

/// <summary>What the script editor inside the Construir window needs: read one script with its source, see how its last runs went, and delete it.</summary>
public static partial class ScriptingEndpoints
{
    internal static async Task<IResult> GetOneAsync(Guid islandId, Guid objectId, Guid scriptId, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        if (!await CanEditAsync(db, islandId, user, ct)) return Results.Forbid();
        var script = await db.ObjectScripts.AsNoTracking().Where(x => x.Id == scriptId && x.ObjectId == objectId).Select(x => new { x.Id, x.Name, x.Source, x.Version, x.Status, x.InstructionBudget, x.MemoryLimitKb, x.UpdatedAt }).SingleOrDefaultAsync(ct);
        return script is null ? Results.NotFound() : Results.Ok(script);
    }

    /// <summary>The last runs of a script (event, result, instructions and the error when there was one), newest first: what the editor shows as its console.</summary>
    internal static async Task<IResult> ExecutionsAsync(Guid islandId, Guid objectId, Guid scriptId, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        if (!await CanEditAsync(db, islandId, user, ct)) return Results.Forbid();
        if (!await db.ObjectScripts.AsNoTracking().AnyAsync(x => x.Id == scriptId && x.ObjectId == objectId, ct)) return Results.NotFound();
        var runs = await db.ScriptExecutions.AsNoTracking().Where(x => x.ScriptId == scriptId).OrderByDescending(x => x.CreatedAt).Take(20).Select(x => new { x.EventName, x.Status, x.InstructionCount, x.Error, x.CreatedAt }).ToListAsync(ct);
        return Results.Ok(runs);
    }

    /// <summary>Removes a script from its object (the island stops running it when it next reads its scripts). Only on an object that may be modified, like adding one.</summary>
    internal static async Task<IResult> DeleteOneAsync(Guid islandId, Guid objectId, Guid scriptId, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        if (!await CanEditAsync(db, islandId, user, ct)) return Results.Forbid();
        var script = await db.ObjectScripts.SingleOrDefaultAsync(x => x.Id == scriptId && x.ObjectId == objectId, ct);
        if (script is null) return Results.NotFound();
        if (!await db.BuildObjects.AsNoTracking().AnyAsync(x => x.Id == objectId && x.DeletedAt == null && (x.OwnerRights & 2) != 0, ct)) return Results.Json(new { error = "no_modify" }, statusCode: StatusCodes.Status403Forbidden);
        db.ScriptExecutions.RemoveRange(await db.ScriptExecutions.Where(x => x.ScriptId == scriptId).ToListAsync(ct));
        db.ObjectScripts.Remove(script); await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
