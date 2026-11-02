using System.Security.Cryptography;
using System.Text;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;
using LifeVerse.Api.Modules.Worlds;
using System.Text.Json;

namespace LifeVerse.Api.Modules.Platform;

public sealed record RegisterServerRequest(Guid InstanceId, Guid IslandId, string Hostname, int Port);
public sealed record HeartbeatRequest(string Status);
public sealed record ValidateEntryTokenRequest(string Token, Guid IslandId);
public sealed record ValidateEntryTokenResponse(Guid UserId, Guid AvatarId, Guid IslandId, Guid SessionId, Guid TokenId,
    int ProtocolVersion, DateTimeOffset ExpiresAt, string DisplayName, string AppearanceJson);
public sealed record ServerSnapshotRequest(long ExpectedVersion, string TerrainJson, string EnvironmentJson, string IntegrityHash);

public static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/platform/servers").WithTags("Platform");
        group.MapPost("/register", RegisterAsync);
        group.MapPut("/{instanceId:guid}/heartbeat", HeartbeatAsync);
        group.MapDelete("/{instanceId:guid}", StopAsync);
        endpoints.MapPost("/api/v1/platform/entry-tokens/validate", ValidateEntryAsync).WithTags("Platform");
        endpoints.MapGet("/api/v1/platform/servers/{instanceId:guid}/island-snapshot", GetServerSnapshotAsync).WithTags("Platform");
        endpoints.MapPut("/api/v1/platform/servers/{instanceId:guid}/island-snapshot", SaveServerSnapshotAsync).WithTags("Platform");
        return endpoints;
    }

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
            hairStyle = avatar.Appearance?.HairStyle ?? "short", cameraSensitivity = avatar.Appearance?.CameraSensitivity ?? .003f,
            invertCameraY = avatar.Appearance?.InvertCameraY ?? false,
            equippedItems = avatar.EquippedItems.Select(x => new { slot = x.Slot, itemKey = x.ItemKey, tintColor = x.TintColor })
        });
        return Results.Ok(new ValidateEntryTokenResponse(identity.UserId, identity.AvatarId, identity.IslandId, identity.SessionId,
            identity.TokenId, identity.ProtocolVersion, identity.ExpiresAt, avatar.DisplayName, appearance));
    }

    private static bool HasValidServiceKey(HttpContext context, IConfiguration config)
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
            instance = new ServerInstance { Id = request.InstanceId, IslandId = request.IslandId, Hostname = request.Hostname[..Math.Min(request.Hostname.Length, 255)], Port = request.Port, Status = "healthy", StartedAt = now, LastHeartbeatAt = now };
            db.ServerInstances.Add(instance);
        }
        else
        {
            instance.Status = "healthy"; instance.LastHeartbeatAt = now; instance.StoppedAt = null;
        }
        await db.SaveChangesAsync(ct);
        await db.Islands.Where(x => x.Id == request.IslandId && x.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RuntimeState, "running").SetProperty(x => x.UpdatedAt, now), ct);
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
