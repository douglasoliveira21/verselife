using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using LifeVerse.Api.Modules.Worlds;

namespace LifeVerse.Api.Modules.Platform;

public sealed record RegisterServerRequest(Guid InstanceId, Guid IslandId, string Hostname, int Port);
public sealed record HeartbeatRequest(string Status);
public sealed record ValidateEntryTokenRequest(string Token, Guid IslandId);
public sealed record ValidateEntryTokenResponse(Guid UserId, Guid AvatarId, Guid IslandId, Guid SessionId, Guid TokenId,
    int ProtocolVersion, DateTimeOffset ExpiresAt, string DisplayName, string AppearanceJson, bool CanEditTerrain = false);
public sealed record ServerSnapshotRequest(long ExpectedVersion, string TerrainJson, string EnvironmentJson, string IntegrityHash);
public sealed record ServerBuildOperationRequest(Guid UserId, string Operation, Guid? ObjectId, BuildObjectRequest? Object, int ExpectedRevision);

public static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/platform/servers").WithTags("Platform");
        group.MapPost("/register", RegisterAsync);
        group.MapPut("/{instanceId:guid}/heartbeat", HeartbeatAsync);
        group.MapDelete("/{instanceId:guid}", StopAsync);
        endpoints.MapPost("/api/v1/platform/entry-tokens/validate", ValidateEntryAsync).WithTags("Platform");
        endpoints.MapIslandContentEndpoints();
        endpoints.MapSupervisorEndpoints();
        endpoints.MapGet("/api/v1/platform/servers/{instanceId:guid}/island-snapshot", GetServerSnapshotAsync).WithTags("Platform");
        endpoints.MapPut("/api/v1/platform/servers/{instanceId:guid}/island-snapshot", SaveServerSnapshotAsync).WithTags("Platform");
        endpoints.MapPost("/api/v1/platform/servers/{instanceId:guid}/build-operations", ApplyServerBuildOperationAsync).WithTags("Platform");
        endpoints.MapGet("/api/v1/platform/metrics", MetricsAsync).RequireAuthorization().WithTags("Platform");
        return endpoints;
    }

    private static async Task<IResult> MetricsAsync(LifeVerseDbContext db, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp(); var players = await db.Sessions.AsNoTracking().CountAsync(x => x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow, ct); var islands = await db.Islands.AsNoTracking().CountAsync(x => x.DeletedAt == null, ct); var objects = await db.BuildObjects.AsNoTracking().CountAsync(x => x.DeletedAt == null, ct); var scripts = await db.ObjectScripts.AsNoTracking().CountAsync(x => x.Status == "active", ct); var instances = await db.ServerInstances.AsNoTracking().CountAsync(x => x.StoppedAt == null, ct); var elapsed = Stopwatch.GetElapsedTime(started); return Results.Ok(new { capturedAt = DateTimeOffset.UtcNow, players, islands, objects, activeScripts = scripts, activeInstances = instances, databaseQueryMilliseconds = elapsed.TotalMilliseconds });
    }

    private static async Task<IResult> ApplyServerBuildOperationAsync(Guid instanceId, ServerBuildOperationRequest request, HttpContext context, IConfiguration config, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!HasValidServiceKey(context, config)) return Results.Unauthorized();
        var server = await db.ServerInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == instanceId && x.StoppedAt == null, ct); if (server is null) return Results.NotFound();
        var island = await db.Islands.SingleOrDefaultAsync(x => x.Id == server.IslandId && x.DeletedAt == null, ct); if (island is null) return Results.NotFound();
        var userCanBuild = island.OwnerUserId == request.UserId || await db.IslandPermissions.AnyAsync(x => x.IslandId == island.Id && x.UserId == request.UserId && x.RevokedAt == null && (x.Permission == "build" || x.Permission == "edit" || x.Permission == "admin"), ct); if (!userCanBuild) return Results.Forbid();
        var now = clock.GetUtcNow(); BuildObject? item = null;
        if (request.Operation == "create")
        {
            if (request.Object is null || !ValidBuildRequest(request.Object)) return Results.BadRequest(); if (await MeshAssets.ValidateAsync(db, request.UserId, request.Object.PrimitiveType, request.Object.GeometryJson, null, ct) is { } meshError) return Results.BadRequest(new { code = "invalid_mesh", error = meshError }); if (await MaterialAssets.ValidateAsync(db, request.UserId, request.Object.MaterialJson, null, ct) is { } textureError) return Results.BadRequest(new { code = "invalid_material", error = textureError }); if (await ContentAssets.ValidateAsync(db, request.UserId, request.Object.ContentJson, null, ct) is { } contentError) return Results.BadRequest(new { code = "invalid_content", error = contentError }); var quota = 5000; try { using var doc = JsonDocument.Parse(island.ResourceLimitsJson); if (doc.RootElement.TryGetProperty("objects", out var count)) quota = Math.Clamp(count.GetInt32(), 1, 100000); } catch { }
            if (await db.BuildObjects.CountAsync(x => x.IslandId == island.Id && x.DeletedAt == null, ct) >= quota) return Results.Conflict(new { code = "object_quota" });
            item = NewBuildObject(island.Id, request.UserId, request.Object, now); db.BuildObjects.Add(item);
        }
        else
        {
            if (request.ObjectId is null) return Results.BadRequest(); item = await db.BuildObjects.SingleOrDefaultAsync(x => x.Id == request.ObjectId && x.IslandId == island.Id && x.DeletedAt == null, ct); if (item is null) return Results.NotFound(); if (item.Revision != request.ExpectedRevision) return Results.Conflict(new { code = "revision_conflict", currentRevision = item.Revision });
            if (request.Operation == "delete") { item.DeletedAt = now; item.Revision++; }
            else if (request.Operation == "update" && request.Object is not null && ValidBuildRequest(request.Object)) { if (await MeshAssets.ValidateAsync(db, request.UserId, request.Object.PrimitiveType, request.Object.GeometryJson, MeshAssets.AssetIdOf(item.GeometryJson), ct) is { } meshUpdateError) return Results.BadRequest(new { code = "invalid_mesh", error = meshUpdateError }); if (await MaterialAssets.ValidateAsync(db, request.UserId, request.Object.MaterialJson, item.MaterialJson, ct) is { } textureUpdateError) return Results.BadRequest(new { code = "invalid_material", error = textureUpdateError }); if (await ContentAssets.ValidateAsync(db, request.UserId, request.Object.ContentJson, item.ContentJson, ct) is { } contentUpdateError) return Results.BadRequest(new { code = "invalid_content", error = contentUpdateError }); ApplyBuildObject(item, request.Object); item.Revision++; item.UpdatedAt = now; }
            else return Results.BadRequest();
        }
        db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = island.Id, ObjectId = item.Id, ActorUserId = request.UserId, Operation = request.Operation, PayloadJson = JsonSerializer.Serialize((object?)request.Object ?? new { }), Revision = item.Revision, CreatedAt = now }); await db.SaveChangesAsync(ct);
        return Results.Ok(new { operation = request.Operation, objectState = item.DeletedAt is null ? BuildObjectResponse(item) : null, objectId = item.Id, revision = item.Revision });
    }

    private static BuildObject NewBuildObject(Guid islandId, Guid owner, BuildObjectRequest r, DateTimeOffset now) => new() { Id = Guid.NewGuid(), IslandId = islandId, CreatorUserId = owner, OwnerUserId = owner, GroupId = r.GroupId, ParentObjectId = r.ParentObjectId, Name = string.IsNullOrWhiteSpace(r.Name) ? r.PrimitiveType : r.Name.Trim(), Description = r.Description?.Trim() ?? string.Empty, PrimitiveType = r.PrimitiveType.ToLowerInvariant(), PositionX = r.PositionX, PositionY = r.PositionY, PositionZ = r.PositionZ, RotationX = r.RotationX, RotationY = r.RotationY, RotationZ = r.RotationZ, ScaleX = r.ScaleX, ScaleY = r.ScaleY, ScaleZ = r.ScaleZ, MaterialJson = string.IsNullOrWhiteSpace(r.MaterialJson) ? "{}" : r.MaterialJson, GeometryJson = string.IsNullOrWhiteSpace(r.GeometryJson) ? "{}" : r.GeometryJson, PhysicsJson = string.IsNullOrWhiteSpace(r.PhysicsJson) ? "{}" : r.PhysicsJson, PermissionsJson = string.IsNullOrWhiteSpace(r.PermissionsJson) ? "{}" : r.PermissionsJson, ContentJson = string.IsNullOrWhiteSpace(r.ContentJson) ? "[]" : r.ContentJson, CreatedAt = now, UpdatedAt = now };
    private static void ApplyBuildObject(BuildObject item, BuildObjectRequest r) { item.PrimitiveType = r.PrimitiveType.ToLowerInvariant(); item.GroupId = r.GroupId; item.PositionX = r.PositionX; item.PositionY = r.PositionY; item.PositionZ = r.PositionZ; item.RotationX = r.RotationX; item.RotationY = r.RotationY; item.RotationZ = r.RotationZ; item.ScaleX = r.ScaleX; item.ScaleY = r.ScaleY; item.ScaleZ = r.ScaleZ; if (!string.IsNullOrWhiteSpace(r.MaterialJson)) item.MaterialJson = r.MaterialJson; if (!string.IsNullOrWhiteSpace(r.GeometryJson)) item.GeometryJson = r.GeometryJson; if (!string.IsNullOrWhiteSpace(r.PhysicsJson)) item.PhysicsJson = r.PhysicsJson; }
    private static bool ValidBuildRequest(BuildObjectRequest r) { var values = new[] { r.PositionX, r.PositionY, r.PositionZ, r.RotationX, r.RotationY, r.RotationZ, r.ScaleX, r.ScaleY, r.ScaleZ }; return BuildCatalog.IsPrimitive(r.PrimitiveType) && values.All(float.IsFinite) && new[] { r.ScaleX, r.ScaleY, r.ScaleZ }.All(x => x is >= .01f and <= 64f) && (r.MaterialJson?.Length ?? 0) <= 32768 && (r.PhysicsJson?.Length ?? 0) <= 32768; }
    private static BuildObjectResponse BuildObjectResponse(BuildObject x) => new(x.Id, x.IslandId, x.CreatorUserId, x.OwnerUserId, x.GroupId, x.ParentObjectId, x.Name, x.Description, x.PrimitiveType, x.PositionX, x.PositionY, x.PositionZ, x.RotationX, x.RotationY, x.RotationZ, x.ScaleX, x.ScaleY, x.ScaleZ, x.MaterialJson, x.GeometryJson, x.PhysicsJson, x.PermissionsJson, x.ContentJson, x.Revision);

    private static async Task<IResult> GetServerSnapshotAsync(Guid instanceId, HttpContext context, IConfiguration config, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!HasValidServiceKey(context, config)) return Results.Unauthorized();
        var server = await db.ServerInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == instanceId && x.StoppedAt == null, ct);
        if (server is null) return Results.NotFound();
        var snapshot = await db.IslandSnapshots.AsNoTracking().Where(x => x.IslandId == server.IslandId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        var island = await db.Islands.AsNoTracking().SingleAsync(x => x.Id == server.IslandId, ct);
        return Results.Ok(new { islandId = server.IslandId, version = snapshot?.Version ?? island.SnapshotVersion, terrainJson = snapshot?.TerrainJson ?? "{}", environmentJson = snapshot?.EnvironmentJson ?? island.EnvironmentJson, integrityHash = snapshot?.IntegrityHash ?? string.Empty });
    }

    private static async Task<IResult> SaveServerSnapshotAsync(Guid instanceId, ServerSnapshotRequest request, HttpContext context, IConfiguration config, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!HasValidServiceKey(context, config)) return Results.Unauthorized();
        var server = await db.ServerInstances.SingleOrDefaultAsync(x => x.Id == instanceId && x.StoppedAt == null, ct); if (server is null) return Results.NotFound();
        var island = await db.Islands.SingleAsync(x => x.Id == server.IslandId && x.DeletedAt == null, ct); if (request.ExpectedVersion != island.SnapshotVersion) return Results.Conflict(new { currentVersion = island.SnapshotVersion });
        if (request.TerrainJson.Length > 4_000_000 || request.EnvironmentJson.Length > 256_000) return Results.BadRequest();
        var now = clock.GetUtcNow(); var version = island.SnapshotVersion + 1; db.IslandSnapshots.Add(new IslandSnapshot { Id = Guid.NewGuid(), IslandId = island.Id, Version = version, TerrainJson = request.TerrainJson, EnvironmentJson = request.EnvironmentJson, IntegrityHash = request.IntegrityHash[..Math.Min(request.IntegrityHash.Length, 64)], CreatedAt = now }); db.IslandChanges.Add(new IslandChange { Id = Guid.NewGuid(), IslandId = island.Id, Version = version, ActorUserId = Guid.Empty, Operation = "server_periodic_snapshot", PayloadJson = request.TerrainJson, IntegrityHash = request.IntegrityHash[..Math.Min(request.IntegrityHash.Length, 64)], CreatedAt = now }); island.SnapshotVersion = version; island.EnvironmentJson = request.EnvironmentJson; island.UpdatedAt = now; await db.SaveChangesAsync(ct); return Results.Ok(new { version });
    }

    private static async Task<IResult> ValidateEntryAsync(ValidateEntryTokenRequest request, HttpContext context, IConfiguration config,
        IslandEntryTokenService tokens, LifeVerseDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        if (!HasValidServiceKey(context, config)) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 4096) return Results.BadRequest();
        var identity = tokens.Validate(request.Token);
        if (identity is null || identity.IslandId != request.IslandId) return Results.Unauthorized();
        var now = timeProvider.GetUtcNow();
        var validSession = await db.Sessions.AsNoTracking().AnyAsync(x => x.Id == identity.SessionId && x.UserId == identity.UserId && x.RevokedAt == null && x.ExpiresAt > now, ct);
        if (!validSession) return Results.Unauthorized();
        var avatar = await db.Avatars.AsNoTracking().Include(x => x.Appearance).Include(x => x.EquippedItems)
            .SingleOrDefaultAsync(x => x.Id == identity.AvatarId && x.UserId == identity.UserId, ct);
        if (avatar is null) return Results.Unauthorized();
        var appearance = JsonSerializer.Serialize(new
        {
            id = avatar.Id, displayName = avatar.DisplayName, baseModel = avatar.BaseModel,
            skinColor = avatar.Appearance?.SkinColor ?? "#C98F65", hairColor = avatar.Appearance?.HairColor ?? "#2B1B14",
            hairStyle = avatar.Appearance?.HairStyle ?? "short", eyeColor = avatar.Appearance?.EyeColor ?? "#4A3326",
            beardStyle = avatar.Appearance?.BeardStyle ?? "none", bodySettingsJson = avatar.Appearance?.BodySettingsJson ?? "{}",
            faceSettingsJson = avatar.Appearance?.FaceSettingsJson ?? "{}", outfitsJson = avatar.Appearance?.OutfitsJson ?? "[]",
            attachmentsJson = avatar.Appearance?.AttachmentsJson ?? "[]", cameraSensitivity = avatar.Appearance?.CameraSensitivity ?? .003f,
            invertCameraY = avatar.Appearance?.InvertCameraY ?? false,
            equippedItems = avatar.EquippedItems.Select(x => new { slot = x.Slot, itemKey = x.ItemKey, tintColor = x.TintColor })
        });
        var canEditTerrain = await db.Islands.AsNoTracking().AnyAsync(x => x.Id == identity.IslandId && x.DeletedAt == null &&
            (x.OwnerUserId == identity.UserId || db.IslandPermissions.Any(p => p.IslandId == x.Id && p.UserId == identity.UserId && p.RevokedAt == null && (p.Permission == "edit_terrain" || p.Permission == "admin"))), ct);
        return Results.Ok(new ValidateEntryTokenResponse(identity.UserId, identity.AvatarId, identity.IslandId, identity.SessionId,
            identity.TokenId, identity.ProtocolVersion, identity.ExpiresAt, avatar.DisplayName, appearance, canEditTerrain));
    }

    internal static bool HasValidServiceKey(HttpContext context, IConfiguration config)
    {
        var expected = config["IslandServer:SharedKey"] ?? string.Empty;
        var actual = context.Request.Headers["X-LifeVerse-Service-Key"].ToString();
        return expected.Length >= 32 && actual.Length == expected.Length &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
    }

    private static async Task<IResult> RegisterAsync(RegisterServerRequest request, HttpContext context, IConfiguration config, LifeVerseDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        if (!HasValidServiceKey(context, config)) return Results.Unauthorized();
        if (request.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(request.Hostname)) return Results.BadRequest();
        if (!await db.Islands.AnyAsync(x => x.Id == request.IslandId && x.DeletedAt == null, ct)) return Results.NotFound();
        var now = timeProvider.GetUtcNow();
        var instance = await db.ServerInstances.SingleOrDefaultAsync(x => x.Id == request.InstanceId, ct);
        if (instance is null)
        {
            instance = new ServerInstance { Id = request.InstanceId, IslandId = request.IslandId, Hostname = request.Hostname[..Math.Min(request.Hostname.Length, 255)], Port = request.Port, Status = "starting", StartedAt = now, LastHeartbeatAt = now };
            db.ServerInstances.Add(instance);
        }
        else
        {
            instance.Status = "starting"; instance.LastHeartbeatAt = now; instance.StoppedAt = null;
        }
        await db.SaveChangesAsync(ct);
        await db.Islands.Where(x => x.Id == request.IslandId && x.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RuntimeState, "starting").SetProperty(x => x.UpdatedAt, now), ct);
        return Results.Ok(new { instance.Id, instance.LastHeartbeatAt });
    }

    private static async Task<IResult> HeartbeatAsync(Guid instanceId, HeartbeatRequest request, HttpContext context, IConfiguration config, LifeVerseDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        if (!HasValidServiceKey(context, config)) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Status)) return Results.BadRequest();
        var status = request.Status.Trim()[..Math.Min(request.Status.Trim().Length, 24)];
        var changed = await db.ServerInstances.Where(x => x.Id == instanceId && x.StoppedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status)
                .SetProperty(x => x.LastHeartbeatAt, timeProvider.GetUtcNow()), ct);
        if (changed == 0) return Results.NotFound();
        var islandId = await db.ServerInstances.AsNoTracking().Where(x => x.Id == instanceId).Select(x => (Guid?)x.IslandId).SingleOrDefaultAsync(ct);
        if (islandId is not null)
        {
            if (status == "healthy") await db.Islands.Where(x => x.Id == islandId && x.DeletedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RuntimeState, "running").SetProperty(x => x.UpdatedAt, timeProvider.GetUtcNow()), ct);
            var hasHealthy = await db.ServerInstances.AnyAsync(x => x.IslandId == islandId && x.StoppedAt == null && x.Status == "healthy" && x.LastHeartbeatAt > timeProvider.GetUtcNow().AddSeconds(-45), ct);
            if (!hasHealthy) await db.Islands.Where(x => x.Id == islandId && x.DeletedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RuntimeState, "suspended").SetProperty(x => x.UpdatedAt, timeProvider.GetUtcNow()), ct);
        }
        return Results.NoContent();
    }

    private static async Task<IResult> StopAsync(Guid instanceId, HttpContext context, IConfiguration config, LifeVerseDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        if (!HasValidServiceKey(context, config)) return Results.Unauthorized();
        var now = timeProvider.GetUtcNow();
        await db.ServerInstances.Where(x => x.Id == instanceId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "stopped").SetProperty(x => x.StoppedAt, now).SetProperty(x => x.LastHeartbeatAt, now), ct);
        return Results.NoContent();
    }
}
