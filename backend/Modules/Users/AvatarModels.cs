using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Shared.Avatar;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Users;

public sealed record AvatarModelSummary(Guid Id, string Name, int SizeBytes, int Triangles, int Joints, float Height, float ScaleToStandard, bool Active, DateTimeOffset CreatedAt);

/// <summary>The outcome of a model operation. Data carries the summary, or the report of a model that was refused.</summary>
public sealed record ModelResult(bool Ok, string? Error = null, object? Data = null);

/// <summary>
/// Custom avatar bodies: a person uploads a GLB, the server checks it (readable, humanoid skeleton, skinning, proportions, budgets), stores it
/// and keeps the report and the bone map. Only a model that passed can be chosen, only its owner can choose it, and everybody on an island
/// downloads the same checked file by its hash.
/// </summary>
public static class AvatarModels
{
    public const int MaxModels = 5;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAvatarModelEndpoints(this RouteGroupBuilder group)
    {
        var models = group.MapGroup("/avatar/models");
        models.MapGet("", async (HttpContext c, LifeVerseDbContext db, CancellationToken ct) => User(c) is { } user ? Results.Ok(await ListAsync(db, user, ct)) : Results.Unauthorized());
        models.MapPost("", UploadHttpAsync).DisableAntiforgery();
        models.MapPost("/{id:guid}/use", async (Guid id, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => await Run(c, user => UseAsync(db, user, id, t.GetUtcNow(), ct)));
        models.MapDelete("/use", async (HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => await Run(c, user => UseAsync(db, user, null, t.GetUtcNow(), ct)));
        models.MapDelete("/{id:guid}", async (Guid id, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => await Run(c, user => DeleteAsync(db, user, id, t.GetUtcNow(), ct)));
        models.MapGet("/{id:guid}/report", async (Guid id, HttpContext c, LifeVerseDbContext db, CancellationToken ct) => User(c) is not { } user ? Results.Unauthorized() : await ReportAsync(db, user, id, ct) is { } json ? Results.Content(json, "application/json") : Results.NotFound());
        return group;
    }

    public static void MapPublic(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/avatars/models/{id:guid}/file", async (Guid id, HttpContext c, LifeVerseDbContext db, AssetStorageService storage, CancellationToken ct) => await DownloadAsync(c, id, db, storage, ct)).RequireAuthorization().WithTags("Users");
    }

    private static Guid? User(HttpContext context) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;

    private static async Task<IResult> Run(HttpContext context, Func<Guid, Task<ModelResult>> operation)
    {
        if (User(context) is not { } user) return Results.Unauthorized();
        var result = await operation(user);
        return result.Ok ? Results.Ok(result.Data ?? new { }) : result.Error switch
        {
            "not_found" => Results.NotFound(new { error = result.Error }),
            "invalid_model" => Results.Json(new { error = result.Error, report = result.Data }, statusCode: 422),
            "bad_file" => Results.BadRequest(new { error = result.Error }),
            _ => Results.Conflict(new { error = result.Error })
        };
    }

    private static async Task<IResult> UploadHttpAsync(HttpRequest request, HttpContext context, LifeVerseDbContext db, AssetStorageService storage, TimeProvider clock, CancellationToken ct)
    {
        if (User(context) is not { } user) return Results.Unauthorized();
        if (!request.HasFormContentType) return Results.BadRequest(new { error = "bad_file" });
        var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0 || file.Length > AvatarModelValidator.MaxBytes) return Results.BadRequest(new { error = file is null || file.Length == 0 ? "bad_file" : "too_big" });
        using var memory = new MemoryStream(); await using (var input = file.OpenReadStream()) await input.CopyToAsync(memory, ct);
        var result = await UploadAsync(db, storage, user, file.FileName, memory.ToArray(), form["name"].ToString(), clock.GetUtcNow(), ct);
        return result.Ok ? Results.Ok(result.Data) : result.Error switch
        {
            "invalid_model" => Results.Json(new { error = result.Error, report = result.Data }, statusCode: 422),
            "bad_file" => Results.BadRequest(new { error = result.Error }),
            "not_found" => Results.NotFound(new { error = result.Error }),
            _ => Results.Conflict(new { error = result.Error })
        };
    }

    private static string CleanName(string? name, string fileName)
    {
        var text = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(fileName) : name;
        text = new string((text ?? string.Empty).Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_').ToArray()).Trim();
        return text.Length == 0 ? "Modelo" : text.Length > 60 ? text[..60] : text;
    }

    private static AvatarModelSummary Summary(AvatarModel model, Guid? active) => new(model.Id, model.Name, model.SizeBytes, model.Triangles, model.Joints, model.Height, model.ScaleToStandard, model.Id == active, model.CreatedAt);

    public static async Task<IReadOnlyList<AvatarModelSummary>> ListAsync(LifeVerseDbContext db, Guid user, CancellationToken ct)
    {
        var avatar = await db.Avatars.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return [];
        return [.. (await db.AvatarModels.AsNoTracking().Where(m => m.AvatarId == avatar.Id && m.DeletedAt == null).OrderByDescending(m => m.CreatedAt).ToListAsync(ct)).Select(m => Summary(m, avatar.CustomModelId))];
    }

    /// <summary>Checks and stores a model. A refused model is not stored; its report comes back so the person can fix the file.</summary>
    public static async Task<ModelResult> UploadAsync(LifeVerseDbContext db, AssetStorageService storage, Guid user, string fileName, byte[] bytes, string? name, DateTimeOffset now, CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (extension != ".glb") return new ModelResult(false, "bad_file"); // one file with everything inside
        var avatar = await db.Avatars.SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return new ModelResult(false, "not_found");
        var report = AvatarModelValidator.Validate(bytes, binary: true);
        if (!report.Ok) return new ModelResult(false, "invalid_model", JsonSerializer.Deserialize<JsonElement>(report.ToJson()));
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var existing = await db.AvatarModels.SingleOrDefaultAsync(m => m.AvatarId == avatar.Id && m.ContentHash == hash, ct);
        if (existing is { DeletedAt: null }) return new ModelResult(true, null, new { model = Summary(existing, avatar.CustomModelId), report = JsonSerializer.Deserialize<JsonElement>(report.ToJson()) });
        if (await db.AvatarModels.CountAsync(m => m.AvatarId == avatar.Id && m.DeletedAt == null, ct) >= MaxModels) return new ModelResult(false, "too_many_models");
        var key = await storage.PutAsync(user, hash, bytes, ct);
        var model = existing ?? new AvatarModel { Id = Guid.NewGuid(), AvatarId = avatar.Id, Name = CleanName(name, fileName!), ContentHash = hash, StorageKey = key, CreatedAt = now, ReportJson = "{}", BoneMapJson = "{}" };
        model.DeletedAt = null; model.Name = CleanName(name, fileName!); model.StorageKey = key; model.SizeBytes = bytes.Length; model.Triangles = report.Triangles; model.Joints = report.Joints; model.Height = report.Height; model.ScaleToStandard = report.ScaleToStandard;
        model.ReportJson = report.ToJson(); model.BoneMapJson = JsonSerializer.Serialize(report.BoneMap, Options);
        if (existing is null) db.AvatarModels.Add(model);
        try { await db.SaveChangesAsync(ct); }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException) { foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached; return new ModelResult(false, "conflict"); }
        return new ModelResult(true, null, new { model = Summary(model, avatar.CustomModelId), report = JsonSerializer.Deserialize<JsonElement>(report.ToJson()) });
    }

    /// <summary>Chooses the model the avatar wears as its body (null goes back to the procedural body). Only the owner, only a model that passed.</summary>
    public static async Task<ModelResult> UseAsync(LifeVerseDbContext db, Guid user, Guid? id, DateTimeOffset now, CancellationToken ct)
    {
        var avatar = await db.Avatars.SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return new ModelResult(false, "not_found");
        if (id is { } wanted)
        {
            var model = await db.AvatarModels.AsNoTracking().SingleOrDefaultAsync(m => m.Id == wanted && m.AvatarId == avatar.Id && m.DeletedAt == null, ct);
            if (model is null) return new ModelResult(false, "not_found");
        }
        avatar.CustomModelId = id; avatar.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new ModelResult(true, null, new { customModelId = id });
    }

    public static async Task<ModelResult> DeleteAsync(LifeVerseDbContext db, Guid user, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var avatar = await db.Avatars.SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return new ModelResult(false, "not_found");
        var model = await db.AvatarModels.SingleOrDefaultAsync(m => m.Id == id && m.AvatarId == avatar.Id && m.DeletedAt == null, ct); if (model is null) return new ModelResult(false, "not_found");
        model.DeletedAt = now;
        if (avatar.CustomModelId == id) { avatar.CustomModelId = null; avatar.UpdatedAt = now; }
        await db.SaveChangesAsync(ct);
        return new ModelResult(true);
    }

    public static async Task<string?> ReportAsync(LifeVerseDbContext db, Guid user, Guid id, CancellationToken ct)
    {
        var avatar = await db.Avatars.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == user, ct); if (avatar is null) return null;
        return await db.AvatarModels.AsNoTracking().Where(m => m.Id == id && m.AvatarId == avatar.Id && m.DeletedAt == null).Select(m => m.ReportJson).SingleOrDefaultAsync(ct);
    }

    /// <summary>Adds the custom model of an avatar (its id, hash, scale and bone map) to what is sent to clients and island servers.</summary>
    public static async Task<AvatarModel?> ActiveAsync(LifeVerseDbContext db, Avatar avatar, CancellationToken ct) =>
        avatar.CustomModelId is { } id ? await db.AvatarModels.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id && m.DeletedAt == null, ct) : null;

    private static async Task<IResult> DownloadAsync(HttpContext context, Guid id, LifeVerseDbContext db, AssetStorageService storage, CancellationToken ct)
    {
        if (User(context) is null) return Results.Unauthorized();
        var model = await db.AvatarModels.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id && m.DeletedAt == null, ct); if (model is null) return Results.NotFound();
        var stream = await storage.OpenReadAsync(model.StorageKey, ct); if (stream is null) return Results.NotFound();
        context.Response.Headers["X-Content-Hash"] = model.ContentHash; context.Response.Headers["Cache-Control"] = "private, max-age=86400";
        return Results.File(stream, "model/gltf-binary");
    }
}
