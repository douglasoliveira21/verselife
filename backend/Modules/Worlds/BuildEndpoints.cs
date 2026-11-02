using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public static class BuildCatalog
{
    public static readonly string[] PrimitiveTypes = ["cube", "sphere", "cylinder", "cone", "plane", "prism", "tube", "ring", "pyramid"];
    public static bool IsPrimitive(string value) => PrimitiveTypes.Contains(value, StringComparer.OrdinalIgnoreCase);
}

public sealed record BuildObjectRequest(string PrimitiveType, float PositionX, float PositionY, float PositionZ, float RotationX, float RotationY, float RotationZ, float ScaleX, float ScaleY, float ScaleZ, string? MaterialJson, string? PhysicsJson, Guid? GroupId, int ExpectedRevision = 0);
public sealed record BuildObjectResponse(Guid Id, Guid IslandId, Guid OwnerUserId, Guid? GroupId, string PrimitiveType, float PositionX, float PositionY, float PositionZ, float RotationX, float RotationY, float RotationZ, float ScaleX, float ScaleY, float ScaleZ, string MaterialJson, string PhysicsJson, int Revision);
public sealed record BuildOperationResponse(Guid OperationId, Guid ObjectId, string Operation, int Revision);
public sealed record BuildGroupRequest(Guid[] ObjectIds, Guid? GroupId);

public static class BuildEndpoints
{
    public static IEndpointRouteBuilder MapBuildEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/worlds/islands/{islandId:guid}/objects").WithTags("Build").RequireAuthorization();
        group.MapGet("", ListAsync); group.MapPost("", CreateAsync); group.MapPut("/{objectId:guid}", UpdateAsync); group.MapDelete("/{objectId:guid}", DeleteAsync); group.MapPost("/{objectId:guid}/duplicate", DuplicateAsync); group.MapPost("/group", GroupAsync);
        return endpoints;
    }

    private static async Task<IResult> GroupAsync(Guid islandId, BuildGroupRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var actor)) return Results.Unauthorized(); if (request.ObjectIds is null || request.ObjectIds.Length is < 1 or > 256) return Results.BadRequest(); var island = await db.Islands.SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return Results.NotFound(); if (!await CanAsync(db, island, actor, "edit", ct)) return Results.Forbid(); var objects = await db.BuildObjects.Where(x => x.IslandId == islandId && x.DeletedAt == null && request.ObjectIds.Contains(x.Id)).ToListAsync(ct); if (objects.Count != request.ObjectIds.Distinct().Count()) return Results.NotFound(); var groupId = request.GroupId ?? Guid.NewGuid(); var now = clock.GetUtcNow(); foreach (var item in objects) { item.GroupId = request.GroupId is null ? groupId : null; item.Revision++; item.UpdatedAt = now; db.BuildOperations.Add(Operation(item, actor, request.GroupId is null ? "group" : "ungroup", new { groupId }, now)); } await db.SaveChangesAsync(ct); return Results.Ok(new { groupId = request.GroupId is null ? groupId : (Guid?)null, objectIds = objects.Select(x => x.Id), revisions = objects.ToDictionary(x => x.Id, x => x.Revision) });
    }

    private static async Task<IResult> ListAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var userId)) return Results.Unauthorized(); var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return Results.NotFound(); if (!await CanAsync(db, island, userId, "enter", ct)) return Results.Forbid();
        return Results.Ok(await db.BuildObjects.AsNoTracking().Where(x => x.IslandId == islandId && x.DeletedAt == null).Select(x => ToResponse(x)).ToListAsync(ct));
    }

    private static async Task<IResult> CreateAsync(Guid islandId, BuildObjectRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var owner)) return Results.Unauthorized(); var island = await db.Islands.SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return Results.NotFound(); if (!await CanAsync(db, island, owner, "build", ct)) return Results.Forbid();
        var quota = ReadQuota(island.ResourceLimitsJson, "objects", 5000); if (await db.BuildObjects.CountAsync(x => x.IslandId == islandId && x.DeletedAt == null, ct) >= quota) return Results.Problem(statusCode: 409, title: "Island object quota reached."); var error = Validate(request); if (error is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["object"] = [error] });
        var now = clock.GetUtcNow(); var item = NewObject(islandId, owner, request, now); db.BuildObjects.Add(item); db.BuildOperations.Add(Operation(item, owner, "create", request, now)); await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/worlds/islands/{islandId}/objects/{item.Id}", ToResponse(item));
    }

    private static async Task<IResult> UpdateAsync(Guid islandId, Guid objectId, BuildObjectRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var actor)) return Results.Unauthorized(); var island = await db.Islands.SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return Results.NotFound(); var item = await db.BuildObjects.SingleOrDefaultAsync(x => x.Id == objectId && x.IslandId == islandId && x.DeletedAt == null, ct); if (item is null) return Results.NotFound(); if (!await CanAsync(db, island, actor, "edit", ct) && item.OwnerUserId != actor) return Results.Forbid(); if (request.ExpectedRevision != item.Revision) return Results.Conflict(new { currentRevision = item.Revision }); var error = Validate(request); if (error is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["object"] = [error] });
        Apply(item, request); item.Revision++; item.UpdatedAt = clock.GetUtcNow(); db.BuildOperations.Add(Operation(item, actor, "update", request, item.UpdatedAt)); await db.SaveChangesAsync(ct); return Results.Ok(ToResponse(item));
    }

    private static async Task<IResult> DeleteAsync(Guid islandId, Guid objectId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var actor)) return Results.Unauthorized(); var island = await db.Islands.SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return Results.NotFound(); var item = await db.BuildObjects.SingleOrDefaultAsync(x => x.Id == objectId && x.IslandId == islandId && x.DeletedAt == null, ct); if (item is null) return Results.NotFound(); if (!await CanAsync(db, island, actor, "delete", ct) && item.OwnerUserId != actor) return Results.Forbid(); if (item.Revision != 0 && item.Revision != 1 && item.Revision != int.MaxValue) { }
        item.DeletedAt = clock.GetUtcNow(); item.Revision++; db.BuildOperations.Add(Operation(item, actor, "delete", new { expectedRevision = item.Revision - 1 }, item.UpdatedAt)); await db.SaveChangesAsync(ct); return Results.NoContent();
    }

    private static async Task<IResult> DuplicateAsync(Guid islandId, Guid objectId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var owner)) return Results.Unauthorized(); var island = await db.Islands.SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return Results.NotFound(); if (!await CanAsync(db, island, owner, "build", ct)) return Results.Forbid(); var source = await db.BuildObjects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == objectId && x.IslandId == islandId && x.DeletedAt == null, ct); if (source is null) return Results.NotFound(); var quota = ReadQuota(island.ResourceLimitsJson, "objects", 5000); if (await db.BuildObjects.CountAsync(x => x.IslandId == islandId && x.DeletedAt == null, ct) >= quota) return Results.Problem(statusCode: 409, title: "Island object quota reached."); var now = clock.GetUtcNow(); var item = new BuildObject { Id = Guid.NewGuid(), IslandId = islandId, OwnerUserId = owner, GroupId = source.GroupId, PrimitiveType = source.PrimitiveType, PositionX = source.PositionX + .5f, PositionY = source.PositionY, PositionZ = source.PositionZ + .5f, RotationX = source.RotationX, RotationY = source.RotationY, RotationZ = source.RotationZ, ScaleX = source.ScaleX, ScaleY = source.ScaleY, ScaleZ = source.ScaleZ, MaterialJson = source.MaterialJson, PhysicsJson = source.PhysicsJson, CreatedAt = now, UpdatedAt = now }; db.BuildObjects.Add(item); db.BuildOperations.Add(Operation(item, owner, "duplicate", new { sourceId = objectId }, now)); await db.SaveChangesAsync(ct); return Results.Ok(ToResponse(item));
    }

    private static BuildObject NewObject(Guid islandId, Guid owner, BuildObjectRequest request, DateTimeOffset now) => new() { Id = Guid.NewGuid(), IslandId = islandId, OwnerUserId = owner, GroupId = request.GroupId, PrimitiveType = request.PrimitiveType.ToLowerInvariant(), PositionX = request.PositionX, PositionY = request.PositionY, PositionZ = request.PositionZ, RotationX = request.RotationX, RotationY = request.RotationY, RotationZ = request.RotationZ, ScaleX = request.ScaleX, ScaleY = request.ScaleY, ScaleZ = request.ScaleZ, MaterialJson = ValidJson(request.MaterialJson, "{}"), PhysicsJson = ValidJson(request.PhysicsJson, "{}"), CreatedAt = now, UpdatedAt = now };
    private static void Apply(BuildObject item, BuildObjectRequest request) { item.PrimitiveType = request.PrimitiveType.ToLowerInvariant(); item.GroupId = request.GroupId; item.PositionX = request.PositionX; item.PositionY = request.PositionY; item.PositionZ = request.PositionZ; item.RotationX = request.RotationX; item.RotationY = request.RotationY; item.RotationZ = request.RotationZ; item.ScaleX = request.ScaleX; item.ScaleY = request.ScaleY; item.ScaleZ = request.ScaleZ; item.MaterialJson = ValidJson(request.MaterialJson, item.MaterialJson); item.PhysicsJson = ValidJson(request.PhysicsJson, item.PhysicsJson); }
    private static BuildOperation Operation(BuildObject item, Guid actor, string operation, object payload, DateTimeOffset now) => new() { Id = Guid.NewGuid(), IslandId = item.IslandId, ObjectId = item.Id, ActorUserId = actor, Operation = operation, PayloadJson = JsonSerializer.Serialize(payload), Revision = item.Revision, CreatedAt = now };
    private static BuildObjectResponse ToResponse(BuildObject x) => new(x.Id, x.IslandId, x.OwnerUserId, x.GroupId, x.PrimitiveType, x.PositionX, x.PositionY, x.PositionZ, x.RotationX, x.RotationY, x.RotationZ, x.ScaleX, x.ScaleY, x.ScaleZ, x.MaterialJson, x.PhysicsJson, x.Revision);
    private static string? Validate(BuildObjectRequest r) { if (!BuildCatalog.IsPrimitive(r.PrimitiveType)) return "Unsupported primitive type."; var values = new[] { r.PositionX, r.PositionY, r.PositionZ, r.RotationX, r.RotationY, r.RotationZ, r.ScaleX, r.ScaleY, r.ScaleZ }; if (values.Any(x => !float.IsFinite(x))) return "Transform values must be finite."; if (new[] { r.ScaleX, r.ScaleY, r.ScaleZ }.Any(x => x is < .01f or > 64f)) return "Scale must be between 0.01 and 64."; if ((r.MaterialJson?.Length ?? 0) > 32768 || (r.PhysicsJson?.Length ?? 0) > 32768) return "Material and physics settings are too large."; if (!string.IsNullOrWhiteSpace(r.MaterialJson) && !JsonValid(r.MaterialJson)) return "Material settings must be valid JSON."; if (!string.IsNullOrWhiteSpace(r.PhysicsJson) && !JsonValid(r.PhysicsJson)) return "Physics settings must be valid JSON."; return null; }
    private static int ReadQuota(string json, string key, int fallback) { try { using var doc = JsonDocument.Parse(json); return doc.RootElement.TryGetProperty(key, out var value) && value.TryGetInt32(out var limit) ? Math.Clamp(limit, 1, 100000) : fallback; } catch { return fallback; } }
    private static string ValidJson(string? value, string fallback) => !string.IsNullOrWhiteSpace(value) && JsonValid(value) ? value : fallback;
    private static bool JsonValid(string value) { try { using var _ = JsonDocument.Parse(value); return true; } catch { return false; } }
    private static bool TryUser(HttpContext context, out Guid userId) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId);
    private static async Task<bool> CanAsync(LifeVerseDbContext db, Island island, Guid userId, string permission, CancellationToken ct) => island.OwnerUserId == userId || await db.IslandPermissions.AnyAsync(x => x.IslandId == island.Id && x.UserId == userId && x.RevokedAt == null && (x.Permission == permission || x.Permission == "admin"), ct);
}
