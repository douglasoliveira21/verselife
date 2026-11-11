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

    public sealed record ValidateScriptRequest(string Source);

    /// <summary>Checks a script without saving or running it: the safety rules and the Lua syntax, with the line of the error. What the editor's Validar button shows.</summary>
    internal static async Task<IResult> ValidateAsync(Guid islandId, Guid objectId, ValidateScriptRequest request, HttpContext context, LifeVerseDbContext db, LuaRuntimeOptions options, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        if (!await CanEditAsync(db, islandId, user, ct)) return Results.Forbid();
        var safety = LuaSafetyValidator.Validate(request.Source ?? string.Empty, options);
        if (!safety.Valid) return Results.Ok(new { valid = false, errors = new[] { new { line = 0, message = safety.Error } } });
        var syntax = LuaSyntax.Check(request.Source!);
        return Results.Ok(new { valid = syntax is null, errors = syntax is null ? [] : new[] { new { line = syntax.Line, message = (string?)syntax.Message } } });
    }

    /// <summary>Restarts one script: the island server starts it again from its saved source, its stored state is cleared and a line goes in its log. The source is never touched.</summary>
    internal static async Task<IResult> ResetOneAsync(Guid islandId, Guid objectId, Guid scriptId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        if (!await CanEditAsync(db, islandId, user, ct)) return Results.Forbid();
        var script = await db.ObjectScripts.SingleOrDefaultAsync(x => x.Id == scriptId && x.ObjectId == objectId, ct);
        if (script is null) return Results.NotFound();
        await ResetAsync(db, [script], clock, ct);
        return Results.Ok(new { reset = 1 });
    }

    /// <summary>Restarts every script of the object (the Resetar scripts button).</summary>
    internal static async Task<IResult> ResetAllAsync(Guid islandId, Guid objectId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        if (!await CanEditAsync(db, islandId, user, ct)) return Results.Forbid();
        if (!await db.BuildObjects.AsNoTracking().AnyAsync(x => x.Id == objectId && x.IslandId == islandId && x.DeletedAt == null, ct)) return Results.NotFound();
        var scripts = await db.ObjectScripts.Where(x => x.ObjectId == objectId).ToListAsync(ct);
        await ResetAsync(db, scripts, clock, ct);
        return Results.Ok(new { reset = scripts.Count });
    }

    private static async Task ResetAsync(LifeVerseDbContext db, List<ObjectScript> scripts, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var ids = scripts.Select(x => x.Id).ToList();
        db.ScriptStates.RemoveRange(await db.ScriptStates.Where(x => ids.Contains(x.ScriptId)).ToListAsync(ct));
        foreach (var script in scripts)
        {
            script.Version++; script.Status = "active"; script.UpdatedAt = now; // a new version makes the island server end the running instance and start a clean one
            db.ScriptExecutions.Add(new ScriptExecution { Id = Guid.NewGuid(), ScriptId = script.Id, EventName = "reset", Status = "reset", InstructionCount = 0, Error = string.Empty, CreatedAt = now });
        }
        await db.SaveChangesAsync(ct);
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

public static partial class ScriptingEndpoints
{
    /// <summary>Models that use what the object carries (a sound) and floating text.</summary>
    private static readonly object[] SoundTemplates =
    [
        new { name = "campainha", eventName = "on_touch", source = "-- Toca o som \"Campainha\" que está no Conteúdo deste objeto.\nfunction on_touch(event)\n  object.play_sound(\"Campainha\", 1.0)\nend\n" },
        new { name = "texto_flutuante", eventName = "on_start", source = "function on_start(event)\n  object.set_text(\"Toque em mim\", \"#12F2E0\")\nend\n\nfunction on_touch(event)\n  object.set_text(\"Olá!\", \"#F4A340\")\nend\n" }
    ];
}
