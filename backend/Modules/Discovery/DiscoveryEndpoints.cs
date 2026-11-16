using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Discovery;

public sealed record LandmarkRequest(string Name, Guid IslandId, float PositionX, float PositionY, float PositionZ, float RotationY, bool IsPublic);
public sealed record EventRequest(string Name, string Description, Guid IslandId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Capacity, bool IsPublic, string? Category = null, string? ImageUrl = null, long PriceCoins = 0, string? ContentRating = null, float? ArrivalX = null, float? ArrivalZ = null);
public sealed record CalendarRequest(bool On);
public sealed record StoreRequest(string? Name, string? Description, string? Category, string? LogoUrl, string? BannerUrl, bool? IsOpen);
public sealed record EventPolicyRequest(bool AllowGuests);
public sealed record SnapshotRequest(Guid? IslandId, string StorageKey, string MetadataJson, bool IsPublic);
public sealed record LandmarkUpdateRequest(string Name, bool? IsPublic);

public static class DiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/discovery").WithTags("Discovery").RequireAuthorization();
        group.MapGet("/search", GlobalSearchAsync); group.MapGet("/islands", SearchIslandsAsync); group.MapPost("/islands/{islandId:guid}/favorite", FavoriteAsync); group.MapDelete("/islands/{islandId:guid}/favorite", UnfavoriteAsync); group.MapPost("/islands/{islandId:guid}/teleport", TeleportAsync);
        group.MapGet("/landmarks", ListLandmarksAsync); group.MapPost("/landmarks", CreateLandmarkAsync); group.MapPatch("/landmarks/{id:guid}", UpdateLandmarkAsync); group.MapDelete("/landmarks/{id:guid}", DeleteLandmarkAsync);
        group.MapGet("/islands/{islandId:guid}/info", PlaceInfoAsync); group.MapGet("/items/{productId:guid}", ItemDetailAsync); group.MapGet("/stores/mine", MyStoresAsync); group.MapGet("/stores/{storeId:guid}", StoreDetailAsync); group.MapPost("/stores", OpenStoreAsync).RequireRateLimiting("announce"); group.MapPut("/stores/{storeId:guid}", ChangeStoreAsync).RequireRateLimiting("announce"); group.MapDelete("/stores/{storeId:guid}", DeleteStoreAsync);
        group.MapGet("/events", ListEventsAsync); group.MapGet("/events/mine", MyEventsAsync); group.MapGet("/events/{eventId:guid}", EventDetailAsync); group.MapPost("/events", CreateEventAsync).RequireRateLimiting("announce"); group.MapPost("/events/{eventId:guid}/join", JoinEventAsync); group.MapPost("/events/{eventId:guid}/leave", LeaveEventAsync);
        group.MapPost("/events/{eventId:guid}/cancel", CancelEventAsync).RequireRateLimiting("announce"); group.MapPut("/events/{eventId:guid}/calendar", CalendarEventAsync); group.MapPut("/islands/{islandId:guid}/event-policy", EventPolicyAsync);
        group.MapGet("/photos", ListPhotosAsync); group.MapPost("/photos", CreatePhotoAsync); group.MapDelete("/photos/{id:guid}", DeletePhotoAsync);
        return endpoints;
    }
    private static async Task<IResult> SearchIslandsAsync(string? q, string? category, string? sort, int page, int pageSize, HttpContext context, LifeVerseDbContext db, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); page = Math.Max(1, page); pageSize = Math.Clamp(pageSize == 0 ? 24 : pageSize, 1, 100); var query = db.Islands.AsNoTracking().Where(x => x.DeletedAt == null && ((x.IsPublic && x.ShowInSearch) || x.OwnerUserId == me)); if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => x.Name.ToLower().Contains(q.ToLower()) || x.Description.ToLower().Contains(q.ToLower())); if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.Category == category); query = sort == "recent" ? query.OrderByDescending(x => x.CreatedAt) : sort == "popular" ? query.OrderByDescending(x => x.MaxPlayers).ThenByDescending(x => x.UpdatedAt) : query.OrderBy(x => x.Name); var total = await query.CountAsync(ct); var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).Select(x => new { x.Id, x.Name, x.Description, x.CoverImageUrl, x.Category, x.IsPublic, x.MaxPlayers, x.RuntimeState, x.CreatedAt }).ToListAsync(ct); return Results.Ok(new { page, pageSize, total, items = rows }); }
    private static async Task<IResult> GlobalSearchAsync(string? q, string? type, int page, int pageSize, HttpContext context, LifeVerseDbContext db, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); if (string.IsNullOrWhiteSpace(q) || q.Trim().Length > 120) return Results.BadRequest(new { error = "Query is required." }); page = Math.Max(1, page); pageSize = Math.Clamp(pageSize == 0 ? 24 : pageSize, 1, 100); var term = q.Trim().ToLower(); var results = new List<object>(); if (type is null or "islands") results.AddRange(await db.Islands.AsNoTracking().Where(x => x.DeletedAt == null && (x.IsPublic || x.OwnerUserId == me) && (x.Name.ToLower().Contains(term) || x.Description.ToLower().Contains(term))).OrderBy(x => x.Name).Take(pageSize).Select(x => (object)new { type = "island", id = x.Id, name = x.Name, description = x.Description, publicItem = x.IsPublic }).ToListAsync(ct)); if (type is null or "events") results.AddRange(await db.DiscoveryEvents.AsNoTracking().Where(x => x.IsPublic && x.EndsAt >= DateTimeOffset.UtcNow && (x.Name.ToLower().Contains(term) || x.Description.ToLower().Contains(term))).OrderBy(x => x.StartsAt).Take(pageSize).Select(x => (object)new { type = "event", id = x.Id, name = x.Name, description = x.Description, startsAt = x.StartsAt }).ToListAsync(ct)); if (type is null or "products") results.AddRange(await db.MarketplaceProducts.AsNoTracking().Where(x => x.Status == "active" && (x.Name.ToLower().Contains(term) || x.Description.ToLower().Contains(term))).OrderBy(x => x.Name).Take(pageSize).Select(x => (object)new { type = "product", id = x.Id, name = x.Name, description = x.Description, priceCoins = x.PriceCoins }).ToListAsync(ct)); return Results.Ok(new { page, pageSize, total = results.Count, items = results.Skip((page - 1) * pageSize).Take(pageSize) }); }
    private static async Task<IResult> FavoriteAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); if (!await db.Islands.AnyAsync(x => x.Id == islandId && x.DeletedAt == null && (x.IsPublic || x.OwnerUserId == me), ct)) return Results.NotFound(); if (!await db.IslandFavorites.AnyAsync(x => x.UserId == me && x.IslandId == islandId, ct)) { db.IslandFavorites.Add(new IslandFavorite { Id = Guid.NewGuid(), UserId = me, IslandId = islandId, CreatedAt = clock.GetUtcNow() }); await db.SaveChangesAsync(ct); } return Results.NoContent(); }
    private static async Task<IResult> UnfavoriteAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); var item = await db.IslandFavorites.SingleOrDefaultAsync(x => x.UserId == me && x.IslandId == islandId, ct); if (item is not null) { db.IslandFavorites.Remove(item); await db.SaveChangesAsync(ct); } return Results.NoContent(); }
    private static async Task<IResult> TeleportAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, WorldManager worlds, TimeProvider clock, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null || !await IslandAccess.AllowsAsync(db, island, me, clock.GetUtcNow(), ct)) return Results.NotFound(); var now = clock.GetUtcNow(); var existing = await db.TeleportReservations.AsNoTracking().Where(x => x.UserId == me && x.IslandId == islandId && (x.Status == "pending" || x.Status == "reserved") && x.ExpiresAt > now).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct); if (existing is not null) return Results.Ok(new { reservationId = existing.Id, state = existing.Status, duplicate = true }); var active = await db.TeleportReservations.CountAsync(x => x.IslandId == islandId && x.Status == "reserved" && x.ExpiresAt > now, ct); if (active >= island.MaxPlayers) return Results.Conflict(new { error = "island_full" }); var decision = await worlds.EnsureAsync(islandId, ct); var reservation = new TeleportReservation { Id = Guid.NewGuid(), UserId = me, IslandId = islandId, Status = decision?.State == "running" ? "reserved" : "pending", ExpiresAt = now.AddSeconds(30), CreatedAt = now }; db.TeleportReservations.Add(reservation); await db.SaveChangesAsync(ct); return Results.Ok(new { reservationId = reservation.Id, state = reservation.Status, instance = decision }); }
    private static async Task<IResult> ListLandmarksAsync(HttpContext context, LifeVerseDbContext db, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); return Results.Ok(await db.Landmarks.AsNoTracking().Where(x => x.OwnerUserId == me && x.DeletedAt == null).ToListAsync(ct)); }
    private static async Task<IResult> CreateLandmarkAsync(LandmarkRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); if (!await CanEnter(db, request.IslandId, me, ct)) return Results.Forbid(); if (request.Name.Trim().Length is < 1 or > 120) return Results.BadRequest(); var item = new Landmark { Id = Guid.NewGuid(), OwnerUserId = me, IslandId = request.IslandId, Name = request.Name.Trim(), PositionX = request.PositionX, PositionY = request.PositionY, PositionZ = request.PositionZ, RotationY = request.RotationY, IsPublic = request.IsPublic, CreatedAt = clock.GetUtcNow(), UpdatedAt = clock.GetUtcNow() }; db.Landmarks.Add(item); await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/discovery/landmarks/{item.Id}", item); }
    private static async Task<IResult> UpdateLandmarkAsync(Guid id, LandmarkUpdateRequest request, HttpContext context, LifeVerseDbContext db, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); var item = await db.Landmarks.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == me && x.DeletedAt == null, ct); if (item is null) return Results.NotFound(); if (request.Name.Trim().Length is < 1 or > 120) return Results.BadRequest(); item.Name = request.Name.Trim(); if (request.IsPublic is not null) item.IsPublic = request.IsPublic.Value; item.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); return Results.Ok(item); }
    private static async Task<IResult> DeleteLandmarkAsync(Guid id, HttpContext context, LifeVerseDbContext db, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); var item = await db.Landmarks.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == me && x.DeletedAt == null, ct); if (item is null) return Results.NotFound(); item.DeletedAt = DateTimeOffset.UtcNow; item.UpdatedAt = item.DeletedAt.Value; await db.SaveChangesAsync(ct); return Results.NoContent(); }
    private static async Task<IResult> ListPhotosAsync(HttpContext context, LifeVerseDbContext db, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); return Results.Ok(await db.PhotoSnapshots.AsNoTracking().Where(x => x.OwnerUserId == me || x.IsPublic).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct)); }
    private static async Task<IResult> CreatePhotoAsync(SnapshotRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); if (request.StorageKey.Length is < 1 or > 512 || request.MetadataJson.Length > 8192) return Results.BadRequest(); try { using var json = JsonDocument.Parse(request.MetadataJson); } catch { return Results.BadRequest(); } var item = new PhotoSnapshot { Id = Guid.NewGuid(), OwnerUserId = me, IslandId = request.IslandId, StorageKey = request.StorageKey, MetadataJson = request.MetadataJson, IsPublic = request.IsPublic, CreatedAt = clock.GetUtcNow() }; db.PhotoSnapshots.Add(item); await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/discovery/photos/{item.Id}", item); }
    private static async Task<IResult> DeletePhotoAsync(Guid id, HttpContext context, LifeVerseDbContext db, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); var item = await db.PhotoSnapshots.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == me, ct); if (item is null) return Results.NotFound(); db.PhotoSnapshots.Remove(item); await db.SaveChangesAsync(ct); return Results.NoContent(); }
    private static IResult Outcome(EventOutcome outcome, Func<IResult>? ok = null) => outcome.Ok ? ok?.Invoke() ?? Results.NoContent() : outcome.Error switch
    {
        "not_found" or "island_not_found" => Results.NotFound(new { error = outcome.Error }),
        "forbidden" or "not_allowed" => Results.Json(new { error = outcome.Error }, statusCode: 403),
        "event_full" or "cancelled" or "ended" or "already_ended" or "too_many_events" or "island_busy" => Results.Conflict(new { error = outcome.Error }),
        _ => Results.BadRequest(new { error = outcome.Error })
    };

    /// <summary>The events still to come (or going on) that this person may see, soonest first. Called-off and finished ones are not listed.</summary>
    private static async Task<IResult> ListEventsAsync(HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!User(context, out var me)) return Results.Unauthorized(); var now = clock.GetUtcNow();
        var rows = await db.DiscoveryEvents.AsNoTracking().Where(x => x.IsPublic && x.GroupId == null && x.CancelledAt == null && x.EndsAt >= now && db.Islands.Any(i => i.Id == x.IslandId && i.IsPublic && i.DeletedAt == null)).OrderBy(x => x.StartsAt).Take(100).ToListAsync(ct);
        var blocked = (await db.UserBlocks.AsNoTracking().Where(b => b.UserId == me || b.BlockedUserId == me).Select(b => b.UserId == me ? b.BlockedUserId : b.UserId).ToListAsync(ct)).ToHashSet();
        return Results.Ok(rows.Where(e => !blocked.Contains(e.OrganizerUserId)));
    }

    private static async Task<IResult> PlaceInfoAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => !User(context, out var me) ? Results.Unauthorized() : await PlaceDirectory.InfoAsync(db, me, islandId, clock.GetUtcNow(), ct) is { } info ? Results.Ok(info) : Results.NotFound();
    private static async Task<IResult> MyEventsAsync(HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => User(context, out var me) ? Results.Ok(await EventDirectory.MineAsync(db, me, clock.GetUtcNow(), ct)) : Results.Unauthorized();
    private static async Task<IResult> EventDetailAsync(Guid eventId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => !User(context, out var me) ? Results.Unauthorized() : await EventDirectory.DetailAsync(db, me, eventId, clock.GetUtcNow(), ct) is { } detail ? Results.Ok(detail) : Results.NotFound();


    private static async Task<IResult> JoinEventAsync(Guid eventId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => User(context, out var me) ? Outcome(await EventDirectory.JoinAsync(db, me, eventId, clock.GetUtcNow(), ct)) : Results.Unauthorized();
    private static async Task<IResult> LeaveEventAsync(Guid eventId, HttpContext context, LifeVerseDbContext db, CancellationToken ct) => User(context, out var me) ? Outcome(await EventDirectory.LeaveAsync(db, me, eventId, ct)) : Results.Unauthorized();
    private static async Task<IResult> CalendarEventAsync(Guid eventId, CalendarRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => User(context, out var me) ? Outcome(await EventDirectory.CalendarAsync(db, me, eventId, request.On, clock.GetUtcNow(), ct)) : Results.Unauthorized();

    private static IResult Stored(StoreOutcome outcome, Func<IResult>? ok = null) => outcome.Ok ? ok?.Invoke() ?? Results.NoContent() : outcome.Error switch
    {
        "not_found" => Results.NotFound(new { error = outcome.Error }),
        "too_many_stores" or "name_taken" => Results.Conflict(new { error = outcome.Error }),
        _ => Results.BadRequest(new { error = outcome.Error })
    };

    private static async Task<IResult> ItemDetailAsync(Guid productId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => !User(context, out var me) ? Results.Unauthorized() : await MarketDirectory.ItemAsync(db, me, productId, clock.GetUtcNow(), ct) is { } item ? Results.Ok(item) : Results.NotFound();
    private static async Task<IResult> StoreDetailAsync(Guid storeId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => !User(context, out var me) ? Results.Unauthorized() : await MarketDirectory.StoreAsync(db, me, storeId, clock.GetUtcNow(), ct) is { } shop ? Results.Ok(shop) : Results.NotFound();
    private static async Task<IResult> MyStoresAsync(HttpContext context, LifeVerseDbContext db, CancellationToken ct) => User(context, out var me) ? Results.Ok(await MarketDirectory.MyStoresAsync(db, me, ct)) : Results.Unauthorized();

    /// <summary>Sensitive changes (announcing or calling off an event, opening, changing or closing a shop, letting guests announce) are written to the audit log: who, what, which one, and how it ended.</summary>
    private static void Audit(ILoggerFactory loggers, string action, Guid user, Guid? target, string result) =>
        loggers.CreateLogger("LifeVerse.Audit").LogInformation("audit action={Action} user={User} target={Target} result={Result}", action, user, target, result);

    private static async Task<IResult> CreateEventAsync(EventRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!User(context, out var me)) return Results.Unauthorized();
        var outcome = await EventDirectory.CreateAsync(db, me, new EventCreate(request.Name, request.Description, request.IslandId, request.StartsAt, request.EndsAt, request.Capacity, request.IsPublic, request.Category, request.ImageUrl, request.PriceCoins, request.ContentRating, request.ArrivalX, request.ArrivalZ), clock.GetUtcNow(), ct);
        Audit(loggers, "event_created", me, outcome.Id, outcome.Ok ? "ok" : outcome.Error ?? "failed");
        return Outcome(outcome, () => Results.Created($"/api/v1/discovery/events/{outcome.Id}", new { id = outcome.Id }));
    }

    private static async Task<IResult> CancelEventAsync(Guid eventId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!User(context, out var me)) return Results.Unauthorized();
        var outcome = await EventDirectory.CancelAsync(db, me, eventId, clock.GetUtcNow(), ct); Audit(loggers, "event_cancelled", me, eventId, outcome.Ok ? "ok" : outcome.Error ?? "failed"); return Outcome(outcome);
    }

    private static async Task<IResult> EventPolicyAsync(Guid islandId, EventPolicyRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!User(context, out var me)) return Results.Unauthorized();
        var outcome = await EventDirectory.SetGuestEventsAsync(db, me, islandId, request.AllowGuests, clock.GetUtcNow(), ct); Audit(loggers, request.AllowGuests ? "island_guest_events_allowed" : "island_guest_events_blocked", me, islandId, outcome.Ok ? "ok" : outcome.Error ?? "failed"); return Outcome(outcome);
    }

    private static async Task<IResult> OpenStoreAsync(StoreRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!User(context, out var me)) return Results.Unauthorized();
        var outcome = await MarketDirectory.OpenAsync(db, me, new StoreChange(request.Name, request.Description, request.Category, request.LogoUrl, request.BannerUrl, request.IsOpen), clock.GetUtcNow(), ct);
        Audit(loggers, "store_opened", me, outcome.Id, outcome.Ok ? "ok" : outcome.Error ?? "failed"); return Stored(outcome, () => Results.Created($"/api/v1/discovery/stores/{outcome.Id}", new { id = outcome.Id }));
    }

    private static async Task<IResult> ChangeStoreAsync(Guid storeId, StoreRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!User(context, out var me)) return Results.Unauthorized();
        var outcome = await MarketDirectory.ChangeAsync(db, me, storeId, new StoreChange(request.Name, request.Description, request.Category, request.LogoUrl, request.BannerUrl, request.IsOpen), clock.GetUtcNow(), ct);
        Audit(loggers, "store_changed", me, storeId, outcome.Ok ? "ok" : outcome.Error ?? "failed"); return Stored(outcome);
    }

    private static async Task<IResult> DeleteStoreAsync(Guid storeId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!User(context, out var me)) return Results.Unauthorized();
        var outcome = await MarketDirectory.CloseForGoodAsync(db, me, storeId, clock.GetUtcNow(), ct); Audit(loggers, "store_deleted", me, storeId, outcome.Ok ? "ok" : outcome.Error ?? "failed"); return Stored(outcome);
    }

    private static bool User(HttpContext context, out Guid id) => Guid.TryParse(context.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out id);
    private static Task<bool> CanEnter(LifeVerseDbContext db, Guid islandId, Guid userId, CancellationToken ct) => db.Islands.AnyAsync(x => x.Id == islandId && x.DeletedAt == null && (x.IsPublic || x.OwnerUserId == userId || db.IslandPermissions.Any(p => p.IslandId == islandId && p.UserId == userId && p.RevokedAt == null && p.Permission == "enter")), ct);
}
