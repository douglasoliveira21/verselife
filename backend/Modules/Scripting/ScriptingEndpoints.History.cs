using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Scripting;

public sealed record ScriptRevisionItem(int Version, string Name, int Lines, int Bytes, DateTimeOffset SavedAt);

/// <summary>The history of a script's code: every save keeps the code it replaced (the latest twenty), and the owner can read an old version or go back to it.</summary>
public static partial class ScriptingEndpoints
{
    public const int KeepRevisions = 20;

    /// <summary>Keeps what the script was before it changes (nothing when the code and name stay the same) and drops the oldest beyond the limit. Called before the new code is stored.</summary>
    internal static async Task KeepRevisionAsync(LifeVerseDbContext db, ObjectScript script, string newSource, Guid user, DateTimeOffset now, CancellationToken ct)
    {
        if (script.Source == newSource) return;
        db.ScriptRevisions.Add(new ScriptRevision { Id = Guid.NewGuid(), ScriptId = script.Id, Version = script.Version, Name = script.Name, Source = script.Source, SavedByUserId = user, CreatedAt = now });
        var old = await db.ScriptRevisions.Where(x => x.ScriptId == script.Id).OrderByDescending(x => x.Version).Skip(KeepRevisions - 1).ToListAsync(ct);
        db.ScriptRevisions.RemoveRange(old);
    }

    private static async Task<ObjectScript?> OwnScriptAsync(Guid islandId, Guid objectId, Guid scriptId, Guid user, LifeVerseDbContext db, CancellationToken ct)
        => await CanEditAsync(db, islandId, user, ct) ? await db.ObjectScripts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == scriptId && x.ObjectId == objectId, ct) : null;

    internal static async Task<IResult> VersionsAsync(Guid islandId, Guid objectId, Guid scriptId, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        if (!await CanEditAsync(db, islandId, user, ct)) return Results.Forbid();
        if (await OwnScriptAsync(islandId, objectId, scriptId, user, db, ct) is not { } script) return Results.NotFound();
        var rows = await db.ScriptRevisions.AsNoTracking().Where(x => x.ScriptId == scriptId).OrderByDescending(x => x.Version).Select(x => new { x.Version, x.Name, x.Source, x.CreatedAt }).ToListAsync(ct);
        return Results.Ok(new { current = script.Version, versions = rows.Select(x => new ScriptRevisionItem(x.Version, x.Name, x.Source.Split('\n').Length, System.Text.Encoding.UTF8.GetByteCount(x.Source), x.CreatedAt)) });
    }

    internal static async Task<IResult> VersionAsync(Guid islandId, Guid objectId, Guid scriptId, int version, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        if (!await CanEditAsync(db, islandId, user, ct)) return Results.Forbid();
        if (await OwnScriptAsync(islandId, objectId, scriptId, user, db, ct) is null) return Results.NotFound();
        var row = await db.ScriptRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.ScriptId == scriptId && x.Version == version, ct);
        return row is null ? Results.NotFound() : Results.Ok(new { row.Version, row.Name, row.Source, savedAt = row.CreatedAt });
    }

    /// <summary>Goes back to an old version: its code becomes the current one (a new version), and the code it replaces is kept in turn, so going back can itself be undone.</summary>
    internal static async Task<IResult> RestoreAsync(Guid islandId, Guid objectId, Guid scriptId, int version, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        if (!await CanEditAsync(db, islandId, user, ct)) return Results.Forbid();
        var script = await db.ObjectScripts.SingleOrDefaultAsync(x => x.Id == scriptId && x.ObjectId == objectId, ct);
        if (script is null) return Results.NotFound();
        if (!await db.BuildObjects.AsNoTracking().AnyAsync(x => x.Id == objectId && x.DeletedAt == null && (x.OwnerRights & 2) != 0, ct)) return Results.Json(new { error = "no_modify" }, statusCode: StatusCodes.Status403Forbidden);
        var row = await db.ScriptRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.ScriptId == scriptId && x.Version == version, ct);
        if (row is null) return Results.NotFound();
        var compiled = LifeVerse.Scripting.ScriptCompiler.Compile(row.Source);
        if (!compiled.Valid) return Results.ValidationProblem(new Dictionary<string, string[]> { ["source"] = [Describe(compiled)] });      // the rules may have changed since it was saved
        var now = clock.GetUtcNow(); await KeepRevisionAsync(db, script, row.Source, user, now, ct);
        script.Source = row.Source; script.Name = row.Name; script.Version++; script.Status = "active"; script.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { script.Id, script.Version, restoredFrom = version });
    }
}
