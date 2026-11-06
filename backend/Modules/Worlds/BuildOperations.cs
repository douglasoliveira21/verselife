using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Shared.Items;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

/// <summary>
/// Every build operation a player makes on an island (create, update, delete, return) ends up here, sent by the island server
/// with the player's id. This is where the rules are enforced: who may build where (island and parcel permissions), who may change
/// an object, the island and parcel limits on objects and on cost, the revision check against other people's edits, and the audit
/// trail. A refusal always carries a short <c>code</c> the client turns into a message.
/// </summary>
public static partial class BuildOperations
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
        var parcels = await LandService.LoadFactsAsync(db, island.Id, ct);
        var now = clock.GetUtcNow();

        switch (request.Operation)
        {
            case "create": return request.Object is { GroupId: not null } or { ParentObjectId: not null } ? await Exclusive(db, island.Id, ct, () => CreateAsync(request, island, actor, isAdmin, hasBuild, parcels, db, now, ct)) : await CreateAsync(request, island, actor, isAdmin, hasBuild, parcels, db, now, ct);
            case "update": return await UpdateAsync(request, island, actor, isAdmin, hasBuild, parcels, db, now, ct);
            case "delete": return await Exclusive(db, island.Id, ct, () => DeleteAsync(request, island, actor, isAdmin, parcels, db, now, ct));
            case "return": return await Exclusive(db, island.Id, ct, () => ReturnAsync(request, island, actor, isAdmin, parcels, db, storage, now, ct));
            case "structure": return await Exclusive(db, island.Id, ct, () => StructureAsync(request, island, actor, isAdmin, parcels, db, now, ct));
            case "protect": return await ProtectAsync(request, island, actor, isAdmin, parcels, db, now, ct);
            case "rez": return await Exclusive(db, island.Id, ct, () => RezAsync(request, island, actor, isAdmin, hasBuild, parcels, db, storage, now, ct));
            default: return Refuse(400, "invalid_operation");
        }
    }

    /// <summary>
    /// What changes how objects hang together (linking, deleting, returning, placing a construction) takes turns on an island. Each of these decides from the tree as it is: a link to
    /// a parent that is deleted at the same moment, or a re-hanging computed before somebody else's change, would leave a linkset with a missing parent. On PostgreSQL the island's
    /// advisory lock is held until the change is saved; everything else (moving, painting…) runs freely.
    /// </summary>
    private static async Task<IResult> Exclusive(LifeVerseDbContext db, Guid islandId, CancellationToken ct, Func<Task<IResult>> body)
    {
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) != true || db.Database.CurrentTransaction is not null) return await body();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({"build-tree:" + islandId}))", ct);
        var result = await body(); await transaction.CommitAsync(ct); return result;
    }

    // ---- create ----------------------------------------------------------------------------------------------

    private static async Task<IResult> CreateAsync(ServerBuildOperationRequest request, Island island, Guid actor, bool isAdmin, bool hasBuild, List<ParcelFacts> parcels, LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        if (request.Object is not { } o) return Refuse(400, "invalid_operation");
        if (await ValidateAsync(db, actor, o, null, ct) is { } invalid) return Refuse(400, "invalid_object", invalid);
        var parcel = BuildRules.ParcelAt(parcels, o.PositionX, o.PositionZ);
        if (BuildRules.CanBuildAt(parcel, actor, isAdmin, hasBuild) is { } denied) return Refuse(403, denied);
        if (BuildCost.HasScripts(o.ContentJson) && BuildRules.CanPutScripts(parcel, actor, isAdmin) is { } noScripts) return Refuse(403, noScripts);
        var cost = BuildCost.Of(o.PrimitiveType, o.GeometryJson, o.MaterialJson, o.PhysicsJson, o.ContentJson);
        if (await LimitAsync(db, island, parcel, cost, null, ct) is { } limit) return Refuse(409, limit);
        if ((o.ParentObjectId is not null || o.GroupId is not null) && await ValidateNewLinkAsync(db, island.Id, o, ct) is { } linkProblem) return Refuse(400, linkProblem); // a new object may only join a linkset the way the rules allow
        var parts = ObjectRights.PartsOf(o.PrimitiveType, o.GeometryJson, o.MaterialJson, o.ContentJson);
        if (await ObjectRights.ValidateSinglesAsync(db, actor, parts, null, null, ct) is { } inUse) return Refuse(409, inUse); // a thing that cannot be copied is in one object only
        var item = BuildEndpoints.NewObject(island.Id, actor, o, now);
        var inherited = await ObjectRights.InheritAsync(db, actor, parts, ct); item.OwnerRights = inherited.Owner; item.NextOwnerRights = ItemRights.Clamp(inherited.NextOwner, inherited.Owner);
        db.BuildObjects.Add(item);
        return await FinishAsync(db, island, actor, "create", item, o, now, ct);
    }

    // ---- update ----------------------------------------------------------------------------------------------

    private static async Task<IResult> UpdateAsync(ServerBuildOperationRequest request, Island island, Guid actor, bool isAdmin, bool hasBuild, List<ParcelFacts> parcels, LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        if (request.ObjectId is null || request.Object is not { } o) return Refuse(400, "invalid_operation");
        var item = await db.BuildObjects.SingleOrDefaultAsync(x => x.Id == request.ObjectId && x.IslandId == island.Id && x.DeletedAt == null, ct);
        if (item is null) return Refuse(404, "not_found");
        if (item.EditLocked) return Refuse(403, "object_protected");
        if (item.Revision != request.ExpectedRevision) return Results.Json(new { code = "revision_conflict", currentRevision = item.Revision }, statusCode: 409);
        var here = BuildRules.ParcelAt(parcels, item.PositionX, item.PositionZ);
        if (BuildRules.CanEditObject(actor, item.OwnerUserId, isAdmin, here) is { } notOwner) return Refuse(403, notOwner);
        if (await ValidateAsync(db, actor, o, item, ct) is { } invalid) return Refuse(400, "invalid_object", invalid);
        var there = BuildRules.ParcelAt(parcels, o.PositionX, o.PositionZ);
        if (there?.Id != here?.Id && BuildRules.CanBuildAt(there, actor, isAdmin, hasBuild) is { } denied) return Refuse(403, denied);
        if (BuildCost.HasScripts(o.ContentJson) && !BuildCost.HasScripts(item.ContentJson) && BuildRules.CanPutScripts(there, actor, isAdmin) is { } noScripts) return Refuse(403, noScripts); // adding the first script

        var changes = ObjectRights.ChangesWhatItIs(item, o);
        if (changes && !ItemRights.Has((await ObjectRights.EffectiveAsync(db, item, ct)).Owner, Right.Modify)) return Refuse(403, "no_modify"); // moving and turning never need Modify; changing what the object is does, for everyone
        var before = ObjectRights.PartsOf(item.PrimitiveType, item.GeometryJson, item.MaterialJson, item.ContentJson); var after = ObjectRights.PartsOf(o.PrimitiveType, o.GeometryJson ?? item.GeometryJson, o.MaterialJson ?? item.MaterialJson, o.ContentJson ?? item.ContentJson);
        if (changes && await ObjectRights.ValidateSinglesAsync(db, actor, after, before, item.Id, ct) is { } inUse) return Refuse(409, inUse);
        var previousPermissions = item.PermissionsJson; var oldCost = item.Cost;
        BuildEndpoints.Apply(item, o);
        if (item.OwnerUserId != actor && !isAdmin) item.PermissionsJson = previousPermissions; // only the owner (or an island admin) changes what others may do
        if (changes) { var inherited = await ObjectRights.InheritAsync(db, actor, after, ct); item.OwnerRights &= inherited.Owner; item.NextOwnerRights = ItemRights.Clamp(item.NextOwnerRights & inherited.NextOwner, item.OwnerRights); } // new parts can only take rights away

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
        if (item.EditLocked) return Refuse(403, "object_protected");
        item.DeletedAt = now; item.Revision++;
        var healed = await HealAsync(db, island.Id, [item], actor, now, ct); // what hung under it is re-hung, and a linkset left with one member is dissolved
        return await FinishAsync(db, island, actor, "delete", item, null, now, ct, healed);
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
        {
            if (BuildRules.CanReturn(actor, item.OwnerUserId, isAdmin, BuildRules.ParcelAt(parcels, item.PositionX, item.PositionZ)) is { } denied) return Refuse(403, denied, $"{item.Name}: {denied}");
            if (item.EditLocked && actor == item.OwnerUserId) return Refuse(403, "object_protected", $"{item.Name} está protegido contra edição."); // whoever manages the land may still clear it
        }

        var itemIds = new List<(Guid Owner, Guid Item)>();
        foreach (var owned in items.GroupBy(x => x.OwnerUserId))
        {
            var sources = owned.Select(x => new PackageSourceDto(x.PrimitiveType, x.Name, x.Description, x.PositionX, x.PositionY, x.PositionZ, x.RotationX, x.RotationY, x.RotationZ, x.ScaleX, x.ScaleY, x.ScaleZ, x.MaterialJson, x.GeometryJson, x.PhysicsJson, x.ContentJson, x.GroupId, x.Id, x.ParentObjectId, x.PivotX, x.PivotY, x.PivotZ)).ToList();
            var first = owned.First().Name;
            var package = PackageDocument.Create(owned.Count() == 1 ? $"Devolvido: {first}" : $"Devolvido: {first} (+{owned.Count() - 1})", sources);
            var saved = await BuildPackageStore.SaveAsync(db, storage, owned.Key, package, now, ct, ObjectRights.ForPackage(owned), "returned");
            itemIds.Add((owned.Key, saved.Id));
        }
        foreach (var item in items)
        {
            item.DeletedAt = now; item.Revision++;
            db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = island.Id, ObjectId = item.Id, ActorUserId = actor, Operation = "return", PayloadJson = JsonSerializer.Serialize(new { returnedTo = item.OwnerUserId, byOwner = item.OwnerUserId == actor }), Revision = item.Revision, CreatedAt = now });
        }
        var healed = await HealAsync(db, island.Id, items, actor, now, ct);
        if (await TrySaveAsync(db, items[0].Id, ct) is { } conflict) return conflict;
        return Results.Ok(new { operation = "return", objectIds = items.Select(x => x.Id).ToArray(), returned = items.Count, packages = itemIds.Count, healed = healed.Select(BuildEndpoints.ToResponse).ToArray() });
    }

    /// <summary>Sends objects back to their owners' inventories on behalf of the land system (a rental ended): nothing is deleted for good.</summary>
    public static Task<IResult> ReturnForLandAsync(Island island, Guid actor, Guid[] objectIds, LifeVerseDbContext db, AssetStorageService storage, DateTimeOffset now, CancellationToken ct) =>
        ReturnAsync(new ServerBuildOperationRequest(actor, "return", null, null, 0, objectIds), island, actor, true, [], db, storage, now, ct);

    // ---- rez (place a saved construction from the inventory) ---------------------------------------------------------------------

    /// <summary>
    /// "Colocar do inventário": the server places a saved construction itself, all objects or none. Each object goes through the same checks as a created one
    /// (where the person may build, scripts, limits, what they hold). The objects get the rights of the item. An item that cannot be copied is used up by being
    /// placed (it comes back as a new item, with the same rights, when the objects are returned), so placing it twice is impossible.
    /// </summary>
    private static async Task<IResult> RezAsync(ServerBuildOperationRequest request, Island island, Guid actor, bool isAdmin, bool hasBuild, List<ParcelFacts> parcels, LifeVerseDbContext db, AssetStorageService storage, DateTimeOffset now, CancellationToken ct)
    {
        if (request.PackageItemId is not { } itemId) return Refuse(400, "invalid_operation");
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == itemId && i.OwnerUserId == actor && i.DeletedAt == null, ct); if (item is null) return Refuse(404, "not_found");
        if (item.Status != ItemRightsService.Active) return Refuse(409, "item_busy");
        var (package, loadError) = await BuildPackageEndpoints.LoadAsync(db, storage, item, ct); if (package is null) return Refuse(400, "invalid_package", loadError);
        if (package.Objects.Length is 0 or > BuildPackageLimits.MaxReturnAtOnce) return Refuse(400, "invalid_package");
        if (new[] { request.OriginX, request.OriginY, request.OriginZ }.Any(v => !float.IsFinite(v) || Math.Abs(v) > 100_000)) return Refuse(400, "invalid_operation");

        var copyable = ItemRights.IsCopyable(item.OwnerRights); var rights = new ItemPermissions(item.OwnerRights, 0, item.NextOwnerRights).Normalized();
        var requests = package.Objects.Select(p => new BuildObjectRequest(p.PrimitiveType, p.X + request.OriginX, p.Y + request.OriginY + package.BaseOffset, p.Z + request.OriginZ, p.RotationX, p.RotationY, p.RotationZ, p.ScaleX, p.ScaleY, p.ScaleZ, p.MaterialJson, p.PhysicsJson, null, 0, p.GeometryJson, p.Name, p.Description, null, null, p.ContentJson, p.PivotX, p.PivotY, p.PivotZ)).ToList();
        var singles = new HashSet<Guid>(); var cumulativeCost = 0; var perParcel = new Dictionary<Guid, int>(); var usage = new Dictionary<Guid, (int Objects, int Cost)>(); var created = new List<BuildObject>(); var groups = new Dictionary<int, Guid>();
        for (var i = 0; i < requests.Count; i++)
        {
            var o = requests[i];
            if (await ValidateAsync(db, actor, o, null, ct) is { } invalid) return Refuse(400, "invalid_object", $"{o.Name}: {invalid}");
            var parcel = BuildRules.ParcelAt(parcels, o.PositionX, o.PositionZ);
            if (BuildRules.CanBuildAt(parcel, actor, isAdmin, hasBuild) is { } denied) return Refuse(403, denied);
            if (BuildCost.HasScripts(o.ContentJson) && BuildRules.CanPutScripts(parcel, actor, isAdmin) is { } noScripts) return Refuse(403, noScripts);
            var parts = ObjectRights.PartsOf(o.PrimitiveType, o.GeometryJson, o.MaterialJson, o.ContentJson);
            foreach (var single in parts.Singles) if (!singles.Add(single) && !await ObjectRights.CanBeInManyAsync(db, actor, single, ct)) return Refuse(409, "no_copy_in_use"); // the same thing twice in one construction
            if (await ObjectRights.ValidateSinglesAsync(db, actor, parts, null, null, ct) is { } inUse) return Refuse(409, inUse);
            var cost = BuildCost.Of(o.PrimitiveType, o.GeometryJson, o.MaterialJson, o.PhysicsJson, o.ContentJson); cumulativeCost += cost;
            if (await LimitAsync(db, island, parcel, cumulativeCost, null, ct) is { } limit) return Refuse(409, limit);
            if (parcel is not null)
            {
                if (!usage.TryGetValue(parcel.Id, out var used)) usage[parcel.Id] = used = await LandService.UsageAsync(db, island.Id, parcel, Guid.Empty, ct);
                perParcel[parcel.Id] = perParcel.GetValueOrDefault(parcel.Id) + 1; if (used.Objects + perParcel[parcel.Id] > parcel.MaxObjects) return Refuse(409, "parcel_object_limit");
            }
            var made = BuildEndpoints.NewObject(island.Id, actor, o, now); var inherited = await ObjectRights.InheritAsync(db, actor, parts, ct);
            made.OwnerRights = ItemRights.Clamp(rights.Owner, inherited.Owner); made.NextOwnerRights = ItemRights.Clamp(rights.NextOwner & inherited.NextOwner, made.OwnerRights);
            if (package.Objects[i].Group is { } groupIndex) { if (!groups.TryGetValue(groupIndex, out var groupId)) groups[groupIndex] = groupId = Guid.NewGuid(); made.GroupId = groupId; } // the construction's linksets come back as linksets
            created.Add(made);
        }
        for (var i = 0; i < created.Count; i++) if (package.Objects[i].Parent is { } parentIndex && parentIndex >= 0 && parentIndex < created.Count) created[i].ParentObjectId = created[parentIndex].Id; // and keep their tree
        if (await db.BuildObjects.CountAsync(x => x.IslandId == island.Id && x.DeletedAt == null, ct) + created.Count > BuildCost.Limit(island.ResourceLimitsJson, "objects", 5000)) return Refuse(409, "object_quota");

        foreach (var made in created)
        {
            db.BuildObjects.Add(made);
            db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = island.Id, ObjectId = made.Id, ActorUserId = actor, Operation = "create", PayloadJson = JsonSerializer.Serialize(new { rezFromItem = item.Id }), Revision = made.Revision, CreatedAt = now });
        }
        if (!copyable) { item.Status = "consumed"; item.DeletedAt = now; } // the only one there is: it is now the objects
        item.Revision++; item.UpdatedAt = now; if (copyable) item.LastUsedAt = now; // a copy that stays in the inventory counts as used
        ItemRightsService.Audit(db, actor, "rez", "ok", item.Id, item.AssetId, actor, null, $"{created.Count} objects on island {island.Id}{(copyable ? string.Empty : ", item used up")}", now);
        try { await db.SaveChangesAsync(ct); }
        catch (Exception exception) when (RaceFailures.IsRace(exception)) { return Refuse(409, "item_busy"); } // the same item was placed, offered or sold at the same moment
        return Results.Ok(new { operation = "rez", itemId = item.Id, consumed = !copyable, objects = created.Select(BuildEndpoints.ToResponse).ToArray() });
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
        var (inParcelObjects, inParcelCost) = await LandService.UsageAsync(db, island.Id, parcel, excluded, ct);
        if (countsAsNewObjectInParcel && inParcelObjects >= parcel.MaxObjects) return "parcel_object_limit";
        if (inParcelCost + newCost > parcel.MaxCost) return "parcel_cost_limit";
        return null;
    }

    /// <summary>
    /// Saves, and turns "someone else changed this object between my reading it and my saving it" into an ordinary revision conflict.
    /// The object's revision is a concurrency token, so two operations on the same object that overlap in time cannot both win.
    /// </summary>
    private static async Task<IResult?> TrySaveAsync(LifeVerseDbContext db, Guid objectId, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return null; }
        catch (DbUpdateConcurrencyException)
        {
            var current = await db.BuildObjects.AsNoTracking().Where(x => x.Id == objectId).Select(x => (int?)x.Revision).FirstOrDefaultAsync(ct);
            return Results.Json(new { code = "revision_conflict", currentRevision = current ?? 0 }, statusCode: 409);
        }
    }

    private static async Task<IResult> FinishAsync(LifeVerseDbContext db, Island island, Guid actor, string operation, BuildObject item, BuildObjectRequest? payload, DateTimeOffset now, CancellationToken ct, List<BuildObject>? healed = null)
    {
        db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = island.Id, ObjectId = item.Id, ActorUserId = actor, Operation = operation, PayloadJson = JsonSerializer.Serialize((object?)payload ?? new { }), Revision = item.Revision, CreatedAt = now });
        if (await TrySaveAsync(db, item.Id, ct) is { } conflict) return conflict;
        return Results.Ok(new { operation, objectState = item.DeletedAt is null ? BuildEndpoints.ToResponse(item) : null, objectId = item.Id, revision = item.Revision, healed = (healed ?? []).Select(BuildEndpoints.ToResponse).ToArray() });
    }

    // ---- reading limits and the audit trail ------------------------------------------------------------------

    public static IEndpointRouteBuilder MapBuildLimitEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/worlds/islands/{islandId:guid}/build-limits", LimitsAsync).WithTags("Build").RequireAuthorization();
        endpoints.MapGet("/api/v1/worlds/islands/{islandId:guid}/objects/{objectId:guid}/audit", AuditAsync).WithTags("Build").RequireAuthorization();
        endpoints.MapGet("/api/v1/worlds/islands/{islandId:guid}/linksets/{groupId:guid}/audit", LinksetAuditAsync).WithTags("Build").RequireAuthorization();
        return endpoints;
    }

    /// <summary>
    /// The history of a whole linkset: every operation on any of its parts (including parts that have since been deleted or returned), newest first. Only somebody who
    /// may edit <b>every</b> live part sees it, so it never shows more than the parts' own histories would.
    /// </summary>
    public static async Task<IResult> LinksetAuditAsync(Guid islandId, Guid groupId, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(i => i.Id == islandId && i.DeletedAt == null, ct); if (island is null) return Results.NotFound();
        var members = await db.BuildObjects.AsNoTracking().Where(o => o.IslandId == islandId && o.GroupId == groupId).ToListAsync(ct); if (members.Count == 0) return Results.NotFound();
        var isAdmin = island.OwnerUserId == user || await db.IslandPermissions.AsNoTracking().AnyAsync(p => p.IslandId == islandId && p.UserId == user && p.RevokedAt == null && p.Permission == "admin", ct);
        var parcels = await LandService.LoadFactsAsync(db, islandId, ct);
        foreach (var member in members.Where(m => m.DeletedAt == null))
            if (BuildRules.CanEditObject(user, member.OwnerUserId, isAdmin, BuildRules.ParcelAt(parcels, member.PositionX, member.PositionZ)) is not null) return Results.Forbid();
        if (members.All(m => m.DeletedAt != null) && !isAdmin && members.Any(m => m.OwnerUserId != user)) return Results.Forbid(); // nothing live left to judge by: only the owner of every part, or an admin
        var ids = members.Select(m => m.Id).ToList(); var names = members.ToDictionary(m => m.Id, m => m.Name);
        var log = await db.BuildOperations.AsNoTracking().Where(o => o.IslandId == islandId && o.ObjectId != null && ids.Contains(o.ObjectId.Value)).OrderByDescending(o => o.CreatedAt).Take(200)
            .Select(o => new { objectId = o.ObjectId, operation = o.Operation, actorUserId = o.ActorUserId, revision = o.Revision, at = o.CreatedAt }).ToListAsync(ct);
        return Results.Ok(new
        {
            groupId, members = members.Select(m => new { id = m.Id, name = m.Name, parentId = m.ParentObjectId, deleted = m.DeletedAt != null }).ToArray(),
            log = log.Select(l => new { l.objectId, objectName = names.GetValueOrDefault(l.objectId ?? Guid.Empty, "?"), l.operation, l.actorUserId, l.revision, l.at }).ToArray()
        });
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
        var parcels = await LandService.LoadFactsAsync(db, islandId, ct);
        var parcel = x is { } px && z is { } pz ? BuildRules.ParcelAt(parcels, px, pz) : null;
        object? parcelInfo = null;
        if (parcel is not null)
        {
            var (insideObjects, insideCost) = await LandService.UsageAsync(db, islandId, parcel, Guid.Empty, ct);
            parcelInfo = new { id = parcel.Id, name = parcel.Name, mine = parcel.IsOwner(user), publicBuild = parcel.PublicBuild, buildMode = parcel.BuildMode, objects = insideObjects, maxObjects = parcel.MaxObjects, cost = insideCost, maxCost = parcel.MaxCost, allowScripts = parcel.AllowScripts, autoReturnMinutes = parcel.AutoReturnMinutes };
        }
        return Results.Ok(new
        {
            objects, maxObjects = BuildCost.Limit(island.ResourceLimitsJson, "objects", 5000), cost, maxCost = BuildCost.Limit(island.ResourceLimitsJson, "cost", BuildCost.DefaultIslandBudget),
            canBuildHere = BuildRules.CanBuildAt(parcel, user, isAdmin, hasBuild) is null, isAdmin, parcel = parcelInfo, canPutScripts = BuildRules.CanPutScripts(parcel, user, isAdmin) is null
        });
    }

    /// <summary>The history of one object (who did what, in which revision), for its owner, the owner of its parcel and island admins.</summary>
    private static async Task<IResult> AuditAsync(Guid islandId, Guid objectId, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(i => i.Id == islandId && i.DeletedAt == null, ct); if (island is null) return Results.NotFound();
        var item = await db.BuildObjects.AsNoTracking().SingleOrDefaultAsync(o => o.Id == objectId && o.IslandId == islandId, ct); if (item is null) return Results.NotFound();
        var isAdmin = island.OwnerUserId == user || await db.IslandPermissions.AsNoTracking().AnyAsync(p => p.IslandId == islandId && p.UserId == user && p.RevokedAt == null && p.Permission == "admin", ct);
        var parcels = await LandService.LoadFactsAsync(db, islandId, ct);
        if (BuildRules.CanEditObject(user, item.OwnerUserId, isAdmin, BuildRules.ParcelAt(parcels, item.PositionX, item.PositionZ)) is not null) return Results.Forbid();
        var log = await db.BuildOperations.AsNoTracking().Where(o => o.IslandId == islandId && o.ObjectId == objectId).OrderByDescending(o => o.CreatedAt).Take(100)
            .Select(o => new { operation = o.Operation, actorUserId = o.ActorUserId, revision = o.Revision, at = o.CreatedAt }).ToListAsync(ct);
        return Results.Ok(log);
    }
}

public static class BuildPackageLimits { public const int MaxReturnAtOnce = 200; }
