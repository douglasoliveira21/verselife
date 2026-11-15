using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Shared.Platform;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Social;

public sealed record TeleportInviteRequest(Guid TargetUserId, string Kind, Guid IslandId);

/// <summary>What a teleport service call answers: ok with the invite (and, once accepted, where to go), or a short code saying why not.</summary>
public sealed record TeleportOutcome(bool Ok, string? Error = null, TeleportInvite? Invite = null, TeleportDestination? Destination = null, string? FromName = null, string? ToName = null)
{
    public static TeleportOutcome Fail(string error) => new(false, error);
}

/// <summary>Who moves and where to: read from the island's report at the moment of acceptance.</summary>
public sealed record TeleportDestination(Guid IslandId, Guid MoverUserId, Guid DestinationUserId, double X, double Y, double Z);

/// <summary>
/// Teleports between two people on the same island. The sender asks (an offer: "come to me", or a request: "take me to you"); only the recipient can accept;
/// the spot is the other person's position in the island's own report when the answer comes, and the island server still decides whether the arrival is allowed
/// (parcel doors and bans apply). Invites last two minutes, one pending invite per pair and kind, and at most ten per minute per sender.
/// </summary>
public static class TeleportInvites
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    public const int MaxPerMinute = 10;
    public static readonly string[] Kinds = ["offer", "request"];

    private static PlayerPosition? Find(ServerReport? report, Guid user) => report?.Players.FirstOrDefault(p => p.UserId == user) is { } p ? new PlayerPosition(p.X, p.Y, p.Z) : null;
    private sealed record PlayerPosition(double X, double Y, double Z);

    private static async Task<ServerReport?> ReportAsync(LifeVerseDbContext db, Guid island, DateTimeOffset now, CancellationToken ct)
    {
        var instance = await ServerChannel.RunningAsync(db, island, now, ct);
        return instance is null ? null : ServerChannel.ReadReport(instance);
    }

    public static async Task<TeleportOutcome> CreateAsync(LifeVerseDbContext db, Guid me, TeleportInviteRequest request, DateTimeOffset now, CancellationToken ct)
    {
        if (!Kinds.Contains(request.Kind) || request.IslandId == Guid.Empty || request.TargetUserId == Guid.Empty) return TeleportOutcome.Fail("bad_request");
        if (request.TargetUserId == me) return TeleportOutcome.Fail("self");
        var names = await db.Users.AsNoTracking().Where(u => (u.Id == me || u.Id == request.TargetUserId) && u.DeletedAt == null).Select(u => new { u.Id, u.Username }).ToListAsync(ct);
        if (names.All(n => n.Id != request.TargetUserId) || names.All(n => n.Id != me)) return TeleportOutcome.Fail("not_found");
        if (await db.UserBlocks.AnyAsync(b => b.UserId == me && b.BlockedUserId == request.TargetUserId || b.UserId == request.TargetUserId && b.BlockedUserId == me, ct)) return TeleportOutcome.Fail("blocked");
        var report = await ReportAsync(db, request.IslandId, now, ct);
        if (report is null) return TeleportOutcome.Fail("island_offline");
        if (Find(report, me) is null || Find(report, request.TargetUserId) is null) return TeleportOutcome.Fail("not_on_island");
        if (await db.TeleportInvites.CountAsync(x => x.FromUserId == me && x.CreatedAt > now.AddMinutes(-1), ct) >= MaxPerMinute) return TeleportOutcome.Fail("rate_limited");
        if (await db.TeleportInvites.AnyAsync(x => x.FromUserId == me && x.ToUserId == request.TargetUserId && x.Kind == request.Kind && x.Status == "pending" && x.ExpiresAt > now, ct)) return TeleportOutcome.Fail("already_pending");
        var invite = new TeleportInvite { Id = Guid.NewGuid(), FromUserId = me, ToUserId = request.TargetUserId, Kind = request.Kind, IslandId = request.IslandId, Status = "pending", CreatedAt = now, ExpiresAt = now + Lifetime };
        db.TeleportInvites.Add(invite); await db.SaveChangesAsync(ct);
        return new TeleportOutcome(true, null, invite, null, names.First(n => n.Id == me).Username, names.First(n => n.Id == request.TargetUserId).Username);
    }

    /// <summary>The recipient accepts: the spot is read now from the island report, and the invite is spent.</summary>
    public static async Task<TeleportOutcome> AcceptAsync(LifeVerseDbContext db, Guid me, Guid inviteId, DateTimeOffset now, CancellationToken ct)
    {
        var invite = await db.TeleportInvites.FirstOrDefaultAsync(x => x.Id == inviteId, ct);
        if (invite is null || invite.ToUserId != me) return TeleportOutcome.Fail("not_found");
        if (invite.Status != "pending") return TeleportOutcome.Fail(invite.Status == "accepted" ? "already_answered" : "closed");
        if (invite.ExpiresAt <= now) { invite.Status = "expired"; await db.SaveChangesAsync(ct); return TeleportOutcome.Fail("expired"); }
        if (await db.UserBlocks.AnyAsync(b => b.UserId == me && b.BlockedUserId == invite.FromUserId || b.UserId == invite.FromUserId && b.BlockedUserId == me, ct)) return TeleportOutcome.Fail("blocked");
        var report = await ReportAsync(db, invite.IslandId, now, ct);
        if (report is null) return TeleportOutcome.Fail("island_offline");
        var mover = invite.Kind == "offer" ? invite.ToUserId : invite.FromUserId; var destination = invite.Kind == "offer" ? invite.FromUserId : invite.ToUserId;
        if (Find(report, mover) is null || Find(report, destination) is not { } spot) return TeleportOutcome.Fail("not_on_island");
        invite.Status = "accepted"; invite.RespondedAt = now; await db.SaveChangesAsync(ct);
        var names = await db.Users.AsNoTracking().Where(u => u.Id == invite.FromUserId || u.Id == invite.ToUserId).Select(u => new { u.Id, u.Username }).ToListAsync(ct);
        return new TeleportOutcome(true, null, invite, new TeleportDestination(invite.IslandId, mover, destination, spot.X, spot.Y, spot.Z), names.FirstOrDefault(n => n.Id == invite.FromUserId)?.Username, names.FirstOrDefault(n => n.Id == invite.ToUserId)?.Username);
    }

    public static async Task<TeleportOutcome> DeclineAsync(LifeVerseDbContext db, Guid me, Guid inviteId, DateTimeOffset now, CancellationToken ct)
    {
        var invite = await db.TeleportInvites.FirstOrDefaultAsync(x => x.Id == inviteId, ct);
        if (invite is null || invite.ToUserId != me) return TeleportOutcome.Fail("not_found");
        if (invite.Status != "pending") return TeleportOutcome.Fail("closed");
        invite.Status = invite.ExpiresAt <= now ? "expired" : "declined"; invite.RespondedAt = now; await db.SaveChangesAsync(ct);
        if (invite.Status == "expired") return TeleportOutcome.Fail("expired");
        var names = await db.Users.AsNoTracking().Where(u => u.Id == invite.FromUserId || u.Id == invite.ToUserId).Select(u => new { u.Id, u.Username }).ToListAsync(ct);
        return new TeleportOutcome(true, null, invite, null, names.FirstOrDefault(n => n.Id == invite.FromUserId)?.Username, names.FirstOrDefault(n => n.Id == invite.ToUserId)?.Username);
    }

    /// <summary>What is waiting for this person (not expired), newest first.</summary>
    public static async Task<List<object>> PendingAsync(LifeVerseDbContext db, Guid me, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.TeleportInvites.AsNoTracking().Where(x => x.ToUserId == me && x.Status == "pending" && x.ExpiresAt > now).OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync(ct);
        var ids = rows.Select(r => r.FromUserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.Username }).ToListAsync(ct);
        return [.. rows.Select(r => (object)new { inviteId = r.Id, kind = r.Kind, islandId = r.IslandId, fromUserId = r.FromUserId, fromName = names.FirstOrDefault(n => n.Id == r.FromUserId)?.Username ?? "alguém", expiresAt = r.ExpiresAt })];
    }
}

public static class TeleportInviteEndpoints
{
    public static IEndpointRouteBuilder MapTeleportInviteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/social/teleports").WithTags("Social").RequireAuthorization();
        group.MapPost("", async (TeleportInviteRequest r, HttpContext c, LifeVerseDbContext db, NotificationHub hub, TimeProvider t, CancellationToken ct) =>
        {
            if (!Who(c, out var me)) return Results.Unauthorized();
            var result = await TeleportInvites.CreateAsync(db, me, r, t.GetUtcNow(), ct);
            if (result.Ok) await hub.NotifyAsync(result.Invite!.ToUserId, "teleport_invite", new { inviteId = result.Invite.Id, kind = result.Invite.Kind, fromName = result.FromName, islandId = result.Invite.IslandId, expiresAt = result.Invite.ExpiresAt }, ct);
            return Answer(result, new { inviteId = result.Invite?.Id, expiresAt = result.Invite?.ExpiresAt });
        });
        group.MapGet("/pending", async (HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Results.Ok(await TeleportInvites.PendingAsync(db, me, t.GetUtcNow(), ct)));
        group.MapPost("/{id:guid}/accept", async (Guid id, HttpContext c, LifeVerseDbContext db, NotificationHub hub, TimeProvider t, CancellationToken ct) =>
        {
            if (!Who(c, out var me)) return Results.Unauthorized();
            var result = await TeleportInvites.AcceptAsync(db, me, id, t.GetUtcNow(), ct);
            if (result.Ok) await hub.NotifyAsync(result.Invite!.FromUserId, "teleport_answer", new { inviteId = id, status = "accepted", byName = result.ToName, kind = result.Invite.Kind, islandId = result.Destination!.IslandId, moverUserId = result.Destination.MoverUserId, x = result.Destination.X, y = result.Destination.Y, z = result.Destination.Z }, ct);
            return Answer(result, result.Destination is { } d ? new { islandId = d.IslandId, moverUserId = d.MoverUserId, x = d.X, y = d.Y, z = d.Z } : null);
        });
        group.MapPost("/{id:guid}/decline", async (Guid id, HttpContext c, LifeVerseDbContext db, NotificationHub hub, TimeProvider t, CancellationToken ct) =>
        {
            if (!Who(c, out var me)) return Results.Unauthorized();
            var result = await TeleportInvites.DeclineAsync(db, me, id, t.GetUtcNow(), ct);
            if (result.Ok) await hub.NotifyAsync(result.Invite!.FromUserId, "teleport_answer", new { inviteId = id, status = "declined", byName = result.ToName, kind = result.Invite.Kind }, ct);
            return Answer(result, new { status = "declined" });
        });
        return endpoints;
    }

    private static bool Who(HttpContext c, out Guid user) => Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out user);

    private static IResult Answer(TeleportOutcome result, object? body) => result.Ok ? Results.Ok(body ?? new { }) : result.Error switch
    {
        "not_found" => Results.NotFound(new { error = result.Error }),
        "bad_request" or "self" => Results.BadRequest(new { error = result.Error }),
        "rate_limited" => Results.Json(new { error = result.Error }, statusCode: 429),
        _ => Results.Conflict(new { error = result.Error })
    };
}
