using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Api.Modules.Platform;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

/// <summary>
/// Every build operation a player makes on an island (create, update, delete, return) ends up here, sent by the island server
/// with the player's id. This is where the rules are enforced: who may build where (island and parcel permissions), who may change
/// an object, the island and parcel limits on objects and on cost, the revision check against other people's edits, and the audit
/// trail. A refusal always carries a short <c>code</c> the client turns into a message.
/// </summary>
public static class BuildOperations
{
    private static IResult Refuse(int status, string code, string? message = null) => Results.Json(new { code, error = message ?? code }, statusCode: status);

    public static async Task<IResult> ApplyAsync(Guid instanceId, ServerBuildOperationRequest request, LifeVerseDbContext db, AssetStorageService storage, TimeProvider clock, CancellationToken ct)
    {
        var server = await db.ServerInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == instanceId && x.StoppedAt == null, ct); if (server is null) return Results.NotFound();
        var island = await db.Islands.SingleOrDefaultAsync(x => x.Id == server.IslandId && x.DeletedAt == null, ct); if (island is null) return Results.NotFound();
        var actor = request.UserId;
        var permissions = await db.IslandPermissions.AsNoTracking().Where(x => x.IslandId == island.Id && x.UserId == actor && x.RevokedAt == null).Select(x => x.Permission).ToListAsync(ct);
        var isAdmin = island.OwnerUserId == actor || permissions.Contains("admin");
        var hasBuild = isAdmin || permissions.Contains("build") || permissions.Contains("edit");
        var parcels = (await db.IslandParcels.AsNoTracking().Where(x => x.IslandId == island.Id && x.DeletedAt == null).ToListAsync(ct)).Select(ParcelFacts.From).OfType<ParcelFacts>().ToList();
        var now = clock.GetUtcNow();

        switch (request.Operation)
        {
            case "create": return await CreateAsync(request, island, actor, isAdmin, hasBuild, parcels, db, now, ct);
            case "update": return await UpdateAsync(request, island, actor, isAdmin, hasBuild, parcels, db, now, ct);
            case "delete": return await DeleteAsync(request, island, actor, isAdmin, parcels, db, now, ct);
            case "return": return await ReturnAsync(request, island, actor, isAdmin, parcels, db, storage, now, ct);
            default: return Refuse(400, "invalid_operation");
        }
    }

    // ---- create ----------------------------------------------------------------------------------------------

    private static async Task<IResult> CreateAsync(ServerBuildOperationRequest request, Island island, Guid actor, bool isAdmin, bool hasBuild, List<ParcelFacts> parcels, LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        if (request.Object is not { } o) return Refuse(400, "invalid_operation");
        if (await ValidateAsync(db, actor, o, null, ct) is { } invalid) return Refuse(400, "invalid_object", invalid);
        var parcel = BuildRules.ParcelAt(parcels, o.PositionX, o.PositionZ);
        if (BuildRules.CanBuildAt(parcel, actor, isAdmin, hasBuild) is { } denied) return Refuse(403, denied);
        var cost = BuildCost.Of(o.PrimitiveType, o.GeometryJson, o.MaterialJson, o.PhysicsJson, o.ContentJson);
        if (await LimitAsync(db, island, parcel, cost, null, ct) is { } limit) return Refuse(409, limit);
        var item = BuildEndpoints.NewObject(island.Id, actor, o, now);
        db.BuildObjects.Add(item);
        return await FinishAsync(db, island, actor, "create", item, o, now, ct);
    }

    // ---- update ----------------------------------------------------------------------------------------------

    private static async Task<IResult> UpdateAsync(ServerBuildOperationRequest request, Island island, Guid actor, bool isAdmin, bool hasBuild, List<ParcelFacts> parcels, LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        if (request.ObjectId is null || request.Object is not { } o) return Refuse(400, "invalid_operation");
        var item = await db.BuildObjects.SingleOrDefaultAsync(x => x.Id == request.ObjectId && x.IslandId == island.Id && x.DeletedAt == null, ct);
        if (item is null) return Refuse(404, "not_found");
        if (item.Revision != request.ExpectedRevision) return Results.Json(new { code = "revision_conflict", currentRevision = item.Revision }, statusCode: 409);
        var here = BuildRules.ParcelAt(parcels, item.PositionX, item.PositionZ);
        if (BuildRules.CanEditObject(actor, item.OwnerUserId, isAdmin, here) is { } notOwner) return Refuse(403, notOwner);
        if (await ValidateAsync(db, actor, o, item, ct) is { } invalid) return Refuse(400, "invalid_object", invalid);
        var there = BuildRules.ParcelAt(parcels, o.PositionX, o.PositionZ);
        if (there?.Id != here?.Id && BuildRules.CanBuildAt(there, actor, isAdmin, hasBuild) is { } denied) return Refuse(403, denied);

        var previousPermissions = item.PermissionsJson; var oldCost = item.Cost;
        BuildEndpoints.Apply(item, o);
        if (item.OwnerUserId != actor && !isAdmin) item.PermissionsJson = previousPermissions; // only the owner (or an island admin) changes what others may do
        var newCost = BuildCost.Of(item.PrimitiveType, item.GeometryJson, item.MaterialJson, item.PhysicsJson, item.ContentJson);
        var movedParcel = there?.Id != here?.Id;
        if ((newCost > oldCost || movedParcel) && await LimitAsync(db, island, there, newCost, item, ct, countsAsNewObjectInParcel: movedParcel) is { } limit) { db.Entry(item).State = EntityState.Unchanged; await db.Entry(item).ReloadAsync(ct); return Refuse(409, limit); }
        item.Cost = newCost; item.Revision++; item.UpdatedAt = now;
        return await FinishAsync(db, island, actor, "update", item, o, now, ct);
    }

    // ---- delete ----------------------------------------------------------------------------------------------

    private static async Task<IResult> DeleteAsync(ServerBuildOperationRequest request, Island island, Guid actor, bool isAdmin, List<ParcelFacts> parcels, LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        if (request.ObjectId is null) return Refuse(400, "invalid_operation");
        var item = await db.BuildObjects.SingleOrDefaultAsync(x => x.Id == request.ObjectId && x.IslandId == island.Id && x.DeletedAt == null, ct);
        if (item is null) return Refuse(404, "not_found");
        if (item.Revision != request.ExpectedRevision) return Results.Json(new { code = "revision_conflict", currentRevision = item.Revision }, statusCode: 409);
        if (BuildRules.CanEditObject(actor, item.OwnerUserId, isAdmin, BuildRules.ParcelAt(parcels, item.PositionX, item.PositionZ)) is { } denied) return Refuse(403, denied);
        item.DeletedAt = now; item.Revision++;
        return await FinishAsync(db, island, actor, "delete", item, null, now, ct);
    }

    // ---- return ----------------------------------------------------------------------------------------------

    /// <summary>
    /// "Devolver": the objects leave the world and go back to their owners' inventories as saved constructions, so nothing is lost.
    /// All or nothing: if the actor may not return one of them, none is returned. The owner of a parcel and island admins may return
    /// other people's objects; anyone may take back their own.
    /// </summary>
    private static async Task<IResult> ReturnAsync(ServerBuildOperationRequest request, Island island, Guid actor, bool isAdmin, List<ParcelFacts> parcels, LifeVerseDbContext db, AssetStorageService storage, DateTimeOffset now, CancellationToken ct)
    {
        var ids = (request.ObjectIds ?? (request.ObjectId is { } one ? [one] : [])).Distinct().Take(BuildPackageLimits.MaxReturnAtOnce).ToArray();
        if (ids.Length == 0) return Refuse(400, "invalid_operation");
        var items = await db.BuildObjects.Where(x => ids.Contains(x.Id) && x.IslandId == island.Id && x.DeletedAt == null).ToListAsync(ct);
        if (items.Count == 0) return Refuse(404, "not_found");
        foreach (var item in items)
            if (BuildRules.CanReturn(actor, item.OwnerUserId, isAdmin, BuildRules.ParcelAt(parcels, item.PositionX, item.PositionZ)) is { } denied) return Refuse(403, denied, $"{item.Name}: {denied}");

        var itemIds = new List<(Guid Owner, Guid Item)>();
        foreach (var owned in items.GroupBy(x => x.OwnerUserId))
        {
            var sources = owned.Select(x => new PackageSourceDto(x.PrimitiveType, x.Name, x.Description, x.PositionX, x.PositionY, x.PositionZ, x.RotationX, x.RotationY, x.RotationZ, x.ScaleX, x.ScaleY, x.ScaleZ, x.MaterialJson, x.GeometryJson, x.PhysicsJson, x.ContentJson, x.GroupId)).ToList();
            var first = owned.First().Name;
            var package = PackageDocument.Create(owned.Count() == 1 ? $"Devolvido: {first}" : $"Devolvido: {first} (+{owned.Count() - 1})", sources);
            var saved = await BuildPackageStore.SaveAsync(db, storage, owned.Key, package, now, ct);
            itemIds.Add((owned.Key, saved.Id));
        }
        foreach (var item in items)
        {
            item.DeletedAt = now; item.Revision++;
            db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = island.Id, ObjectId = item.Id, ActorUserId = actor, Operation = "return", PayloadJson = JsonSerializer.Serialize(new { returnedTo = item.OwnerUserId, byOwner = item.OwnerUserId == actor }), Revision = item.Revision, CreatedAt = now });
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { operation = "return", objectIds = items.Select(x => x.Id).ToArray(), returned = items.Count, packages = itemIds.Count });
    }

    // ---- shared ----------------------------------------------------------------------------------------------

    private static async Task<string?> ValidateAsync(LifeVerseDbContext db, Guid actor, BuildObjectRequest o, BuildObject? existing, CancellationToken ct) =>
        BuildEndpoints.ValidateObject(o)
        ?? await MeshAssets.ValidateAsync(db, actor, o.PrimitiveType, o.GeometryJson, existing is null ? null : MeshAssets.AssetIdOf(existing.GeometryJson), ct)
        ?? await MaterialAssets.ValidateAsync(db, actor, o.MaterialJson, existing?.MaterialJson, ct)
        ?? await ContentAssets.ValidateAsync(db, actor, o.ContentJson, existing?.ContentJson, ct);

    /// <summary>Null when one more object (or a heavier one) still fits the island and the parcel, otherwise the refusal code.</summary>
    private static async Task<string?> LimitAsync(LifeVerseDbContext db, Island island, ParcelFacts? parcel, int newCost, BuildObject? existing, CancellationToken ct, bool countsAsNewObjectInParcel = true)
    {
        var excluded = existing?.Id ?? Guid.Empty;
        var isNew = existing is null;
        var islandObjects = await db.BuildObjects.CountAsync(x => x.IslandId == island.Id && x.DeletedAt == null, ct);
        if (isNew && islandObjects >= BuildCost.Limit(island.ResourceLimitsJson, "objects", 5000)) return "object_quota";
        var islandCost = await db.BuildObjects.Where(x => x.IslandId == island.Id && x.DeletedAt == null && x.Id != excluded).SumAsync(x => (int?)x.Cost, ct) ?? 0;
        if (islandCost + newCost > BuildCost.Limit(island.ResourceLimitsJson, "cost", BuildCost.DefaultIslandBudget)) return "cost_limit";
        if (parcel is null) return null;
        var inParcel = db.BuildObjects.Where(x => x.IslandId == island.Id && x.DeletedAt == null && x.Id != excluded && x.PositionX >= parcel.MinX && x.PositionX < parcel.MaxX && x.PositionZ >= parcel.MinZ && x.PositionZ < parcel.MaxZ);
        if (countsAsNewObjectInParcel && await inParcel.CountAsync(ct) >= parcel.MaxObjects) return "parcel_object_limit";
        if (await inParcel.SumAsync(x => (int?)x.Cost, ct) + newCost > parcel.MaxCost) return "parcel_cost_limit";
        return null;
    }

    private static async Task<IResult> FinishAsync(LifeVerseDbContext db, Island island, Guid actor, string operation, BuildObject item, BuildObjectRequest? payload, DateTimeOffset now, CancellationToken ct)
    {
        db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = island.Id, ObjectId = item.Id, ActorUserId = actor, Operation = operation, PayloadJson = JsonSerializer.Serialize((object?)payload ?? new { }), Revision = item.Revision, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { operation, objectState = item.DeletedAt is null ? BuildEndpoints.ToResponse(item) : null, objectId = item.Id, revision = item.Revision });
    }

    // ---- reading limits and the audit trail ------------------------------------------------------------------

    public static IEndpointRouteBuilder MapBuildLimitEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/worlds/islands/{islandId:guid}/build-limits", LimitsAsync).WithTags("Build").RequireAuthorization();
        endpoints.MapGet("/api/v1/worlds/islands/{islandId:guid}/objects/{objectId:guid}/audit", AuditAsync).WithTags("Build").RequireAuthorization();
        return endpoints;
    }

    private static bool TryUser(HttpContext context, out Guid id) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);

    /// <summary>How full the island and the parcel under a spot are, and whether the asking player may build there.</summary>
    private static async Task<IResult> LimitsAsync(Guid islandId, double? x, double? z, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x2 => x2.Id == islandId && x2.DeletedAt == null, ct); if (island is null) return Results.NotFound();
        var permissions = await db.IslandPermissions.AsNoTracking().Where(p => p.IslandId == islandId && p.UserId == user && p.RevokedAt == null).Select(p => p.Permission).ToListAsync(ct);
        if (!island.IsPublic && island.OwnerUserId != user && !permissions.Any(p => p is "enter" or "admin" or "build" or "edit")) return Results.Forbid();
        var isAdmin = island.OwnerUserId == user || permissions.Contains("admin");
        var hasBuild = isAdmin || permissions.Contains("build") || permissions.Contains("edit");
        var live = db.BuildObjects.AsNoTracking().Where(o => o.IslandId == islandId && o.DeletedAt == null);
        var objects = await live.CountAsync(ct); var cost = await live.SumAsync(o => (int?)o.Cost, ct) ?? 0;
        var parcels = (await db.IslandParcels.AsNoTracking().Where(p => p.IslandId == islandId && p.DeletedAt == null).ToListAsync(ct)).Select(ParcelFacts.From).OfType<ParcelFacts>().ToList();
        var parcel = x is { } px && z is { } pz ? BuildRules.ParcelAt(parcels, px, pz) : null;
        object? parcelInfo = null;
        if (parcel is not null)
        {
            var inside = live.Where(o => o.PositionX >= parcel.MinX && o.PositionX < parcel.MaxX && o.PositionZ >= parcel.MinZ && o.PositionZ < parcel.MaxZ);
            parcelInfo = new { id = parcel.Id, name = parcel.Name, mine = parcel.OwnerUserId == user, publicBuild = parcel.PublicBuild, objects = await inside.CountAsync(ct), maxObjects = parcel.MaxObjects, cost = await inside.SumAsync(o => (int?)o.Cost, ct) ?? 0, maxCost = parcel.MaxCost };
        }
        return Results.Ok(new
        {
            objects, maxObjects = BuildCost.Limit(island.ResourceLimitsJson, "objects", 5000), cost, maxCost = BuildCost.Limit(island.ResourceLimitsJson, "cost", BuildCost.DefaultIslandBudget),
            canBuildHere = BuildRules.CanBuildAt(parcel, user, isAdmin, hasBuild) is null, isAdmin, parcel = parcelInfo
        });
    }

    /// <summary>The history of one object (who did what, in which revision), for its owner, the owner of its parcel and island admins.</summary>
    private static async Task<IResult> AuditAsync(Guid islandId, Guid objectId, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(i => i.Id == islandId && i.DeletedAt == null, ct); if (island is null) return Results.NotFound();
        var item = await db.BuildObjects.AsNoTracking().SingleOrDefaultAsync(o => o.Id == objectId && o.IslandId == islandId, ct); if (item is null) return Results.NotFound();
        var isAdmin = island.OwnerUserId == user || await db.IslandPermissions.AsNoTracking().AnyAsync(p => p.IslandId == islandId && p.UserId == user && p.RevokedAt == null && p.Permission == "admin", ct);
        var parcels = (await db.IslandParcels.AsNoTracking().Where(p => p.IslandId == islandId && p.DeletedAt == null).ToListAsync(ct)).Select(ParcelFacts.From).OfType<ParcelFacts>().ToList();
        if (BuildRules.CanEditObject(user, item.OwnerUserId, isAdmin, BuildRules.ParcelAt(parcels, item.PositionX, item.PositionZ)) is not null) return Results.Forbid();
        var log = await db.BuildOperations.AsNoTracking().Where(o => o.IslandId == islandId && o.ObjectId == objectId).OrderByDescending(o => o.CreatedAt).Take(100)
            .Select(o => new { operation = o.Operation, actorUserId = o.ActorUserId, revision = o.Revision, at = o.CreatedAt }).ToListAsync(ct);
        return Results.Ok(log);
    }
}

public static class BuildPackageLimits { public const int MaxReturnAtOnce = 200; }
