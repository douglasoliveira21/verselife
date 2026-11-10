using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record TeleportView(Guid Id, Guid DestinationIslandId, Guid? SourceIslandId, string Status, string? FailureCode, DateTimeOffset ExpiresAt, DateTimeOffset? ArrivedAt);
public sealed record TeleportConnection(Guid TransferId, string Token, string Host, int Port, DateTimeOffset ExpiresAt, int ProtocolVersion);

/// <summary>The trip between islands, as the client uses it: prepare, connect, look, cancel. The island servers confirm the arrival on the platform endpoints.</summary>
public static class TeleportEndpoints
{
    public static IEndpointRouteBuilder MapTeleportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/worlds/teleports").WithTags("Teleport").RequireAuthorization();
        group.MapPost("/prepare", PrepareAsync); group.MapPost("/{transferId:guid}/connect", ConnectAsync); group.MapPost("/{transferId:guid}/cancel", CancelAsync); group.MapGet("/{transferId:guid}", GetAsync);
        return endpoints;
    }

    internal static TeleportView ViewOf(TeleportTransfer t) => new(t.Id, t.DestinationIslandId, t.SourceIslandId, t.Status, t.FailureCode, t.ExpiresAt, t.ArrivedAt);

    private static IResult Answer(TeleportOutcome outcome) =>
        outcome.Status is 200 or 201 ? Results.Json(ViewOf(outcome.Transfer!), statusCode: outcome.Status)
        : Results.Json(new { code = outcome.Code, transfer = outcome.Transfer is null ? null : ViewOf(outcome.Transfer) }, statusCode: outcome.Status);

    private static bool TryIdentity(HttpContext context, out Guid userId, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        return Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId) && Guid.TryParse(context.User.FindFirst("sid")?.Value, out sessionId);
    }

    private static async Task<IResult> PrepareAsync(PrepareTeleportRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryIdentity(context, out var userId, out var sessionId)) return Results.Unauthorized();
        var now = clock.GetUtcNow();
        if (!await db.Sessions.AnyAsync(x => x.Id == sessionId && x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > now, ct)) return Results.Unauthorized();
        return Answer(await TeleportService.PrepareAsync(db, userId, sessionId, request, now, ct));
    }

    private static async Task<IResult> GetAsync(Guid transferId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryIdentity(context, out var userId, out _)) return Results.Unauthorized();
        return await TeleportService.OwnAsync(db, userId, transferId, clock.GetUtcNow(), ct) is { } transfer ? Results.Ok(ViewOf(transfer)) : Results.NotFound(new { code = "transfer_not_found" });
    }

    private static async Task<IResult> CancelAsync(Guid transferId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryIdentity(context, out var userId, out _)) return Results.Unauthorized();
        return Answer(await TeleportService.CancelAsync(db, userId, transferId, clock.GetUtcNow(), ct));
    }

    /// <summary>Issues the single-use ticket for the trip and says where to connect. While the destination server is starting it answers 503 and the reservation waits.</summary>
    private static async Task<IResult> ConnectAsync(Guid transferId, HttpContext context, LifeVerseDbContext db, WorldManager world, IslandEntryTokenService tokens, TimeProvider clock, CancellationToken ct)
    {
        if (!TryIdentity(context, out var userId, out var sessionId)) return Results.Unauthorized();
        var now = clock.GetUtcNow();
        if (!await db.Sessions.AnyAsync(x => x.Id == sessionId && x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > now, ct)) return Results.Unauthorized();
        var transfer = await TeleportService.OwnAsync(db, userId, transferId, now, ct);
        if (transfer is null) return Results.NotFound(new { code = "transfer_not_found" });
        if (transfer.Status == "arrived") return Results.Json(new { code = "already_arrived", transfer = ViewOf(transfer) }, statusCode: 409);
        if (!TeleportService.ActiveStatuses.Contains(transfer.Status)) return Results.Json(new { code = transfer.Status, transfer = ViewOf(transfer) }, statusCode: 410);
        if (transfer.TokenUsedAt is not null) return Results.Json(new { code = "token_used", transfer = ViewOf(transfer) }, statusCode: 409); // the ticket already opened the destination: the trip cannot be connected twice

        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == transfer.DestinationIslandId && x.DeletedAt == null, ct);
        if (island is null) { transfer.Status = "failed"; transfer.FailureCode = "island_not_found"; await db.SaveChangesAsync(ct); return Results.Json(new { code = "island_not_found" }, statusCode: 404); }
        var user = await db.Users.AsNoTracking().Include(x => x.Avatar).SingleAsync(x => x.Id == userId, ct);
        if (user.Avatar is null) return Results.Json(new { code = "avatar_required" }, statusCode: 409);
        var server = await db.ServerInstances.AsNoTracking().Where(x => x.IslandId == island.Id && x.StoppedAt == null && x.Status == "healthy" && x.LastHeartbeatAt > now.AddSeconds(-45)).OrderByDescending(x => x.LastHeartbeatAt).FirstOrDefaultAsync(ct);
        if (server is null)
        {
            var decision = await world.EnsureAsync(island.Id, ct);
            context.Response.Headers.RetryAfter = "5";
            return Results.Json(new { code = "server_starting", state = decision?.State ?? "unknown", transfer = ViewOf(transfer) }, statusCode: 503);
        }
        var tokenId = Guid.NewGuid();
        var issued = tokens.Create(user, user.Avatar, island, sessionId, transfer.Id, tokenId);
        transfer.Status = "connecting"; transfer.TokenId = tokenId; transfer.TokenExpiresAt = issued.Identity.ExpiresAt; // a new ticket replaces an unused older one
        await db.SaveChangesAsync(ct);
        return Results.Ok(new TeleportConnection(transfer.Id, issued.Token, server.Hostname, server.Port, issued.Identity.ExpiresAt, issued.Identity.ProtocolVersion));
    }
}
