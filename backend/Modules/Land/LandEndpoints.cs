using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

/// <summary>Cells as pairs [cx, cz]; or a rectangle in metres when the person selected a plain area.</summary>
public sealed record LandClaimRequest(string Name, string? Description, int[][]? Cells, double? MinX, double? MinZ, double? MaxX, double? MaxZ, int? MaxObjects = null);

public sealed record SaleRequest(long Price, Guid? BuyerUserId, string? Description, bool IncludeObjects = true);
public sealed record BuyRequest(long ExpectedPrice, string IdempotencyKey);
public sealed record RentOfferRequest(long PricePerPeriod, int PeriodDays = 30, int MinPeriods = 1, int MaxPeriods = 12, long DepositCoins = 0, int GraceDays = 3, bool AutoRenewAllowed = true);
public sealed record RentRequest(int Periods, bool AutoRenew, long ExpectedPrice, string IdempotencyKey);
public sealed record RenewRequest(int Periods, string IdempotencyKey);
public sealed record SplitRequest(int[][] Cells, string Name);
public sealed record MergeRequest(Guid[] ParcelIds, Guid? PrimaryId);
public sealed record AccessModeRequest(string Mode);
public sealed record CategoryRequest(string Category);
public sealed record AccessAddRequest(Guid UserId, string Kind, int? Minutes, string? Reason);
public sealed record EjectRequest(Guid UserId);
public sealed record OptionsRequest(string BuildMode, bool AllowScripts, int? AutoReturnMinutes);
public sealed record ArrivalRequest(double? X, double? Z, double Yaw, string Mode);
public sealed record EnvironmentRequest(string? Environment);
public sealed record MediaRequest(string? MusicUrl, float Volume, bool Autoplay, string? Name);
public sealed record ReturnObjectsRequest(string Scope, Guid[]? ObjectIds, string? Reason);

public static class LandEndpoints
{
    public static IEndpointRouteBuilder MapLandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/worlds/islands/{islandId:guid}/land").WithTags("Land").RequireAuthorization();
        // a parcel or a rental in the address must belong to the island in the address: no reaching across islands
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!Guid.TryParse(http.GetRouteValue("islandId")?.ToString(), out var island)) return await next(context);
            var db = http.RequestServices.GetRequiredService<LifeVerseDbContext>();
            if (Guid.TryParse(http.GetRouteValue("parcelId")?.ToString(), out var parcel) && !await db.IslandParcels.AsNoTracking().AnyAsync(p => p.Id == parcel && p.IslandId == island, http.RequestAborted)) return Results.NotFound();
            if (Guid.TryParse(http.GetRouteValue("rentalId")?.ToString(), out var rental) && !await (from r in db.ParcelRentals.AsNoTracking() join p in db.IslandParcels.AsNoTracking() on r.ParcelId equals p.Id where r.Id == rental && p.IslandId == island select r.Id).AnyAsync(http.RequestAborted)) return Results.NotFound();
            return await next(context);
        });
        group.MapGet("", ViewAsync);
        group.MapGet("/version", VersionAsync);
        group.MapGet("/overview", OverviewAsync);
        group.MapPut("/parcels/{parcelId:guid}/category", async (Guid islandId, Guid parcelId, CategoryRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandOverview.SetCategoryAsync(db, islandId, parcelId, user, r.Category, t, ct)));
        group.MapPost("/claim", ClaimAsync);
        group.MapPost("/parcels/{parcelId:guid}/sale", async (Guid islandId, Guid parcelId, SaleRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandCommerce.OfferForSaleAsync(db, parcelId, user, new SaleOffer(r.Price, r.BuyerUserId, r.Description, r.IncludeObjects), t, ct)));
        group.MapDelete("/parcels/{parcelId:guid}/sale", async (Guid islandId, Guid parcelId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandCommerce.CancelSaleAsync(db, parcelId, user, t, ct)));
        group.MapPost("/parcels/{parcelId:guid}/buy", async (Guid islandId, Guid parcelId, BuyRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandCommerce.BuyAsync(db, parcelId, user, r.IdempotencyKey, r.ExpectedPrice, t, ct)));
        group.MapPost("/parcels/{parcelId:guid}/rental-offer", async (Guid islandId, Guid parcelId, RentOfferRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandCommerce.OfferForRentAsync(db, parcelId, user, new RentOffer(r.PricePerPeriod, r.PeriodDays, r.MinPeriods, r.MaxPeriods, r.DepositCoins, r.GraceDays, r.AutoRenewAllowed), t, ct)));
        group.MapDelete("/parcels/{parcelId:guid}/rental-offer", async (Guid islandId, Guid parcelId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandCommerce.CancelRentOfferAsync(db, parcelId, user, t, ct)));
        group.MapPost("/parcels/{parcelId:guid}/rent", async (Guid islandId, Guid parcelId, RentRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandCommerce.RentAsync(db, parcelId, user, r.Periods, r.AutoRenew, r.IdempotencyKey, r.ExpectedPrice, t, ct)));
        group.MapPost("/rentals/{rentalId:guid}/renew", async (Guid islandId, Guid rentalId, RenewRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandCommerce.RenewAsync(db, rentalId, user, r.Periods, r.IdempotencyKey, t, ct)));
        group.MapPost("/rentals/{rentalId:guid}/end", async (Guid islandId, Guid rentalId, HttpContext c, LifeVerseDbContext db, Modules.Assets.AssetStorageService storage, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandCommerce.EndAsync(db, storage, rentalId, user, t, ct)));
        group.MapGet("/parcels/{parcelId:guid}/history", HistoryAsync);

        // administration: cutting, joining, who may enter, what stands on the land
        group.MapPost("/parcels/{parcelId:guid}/split", async (Guid islandId, Guid parcelId, SplitRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            r.Cells is null || r.Cells.Length > LandGrid.MaxCellsPerParcel || r.Cells.Any(x => x is not { Length: 2 }) ? Results.BadRequest(new { error = "empty" })
                : await Run(c, islandId, db, ct, user => LandAdmin.SplitAsync(db, parcelId, user, [.. r.Cells.Select(x => LandGrid.Key(x[0], x[1]))], r.Name, t, ct)));
        group.MapPost("/merge", async (Guid islandId, MergeRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandAdmin.MergeAsync(db, r.ParcelIds ?? [], user, r.PrimaryId, t, ct)));
        group.MapPut("/parcels/{parcelId:guid}/access-mode", async (Guid islandId, Guid parcelId, AccessModeRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandAdmin.SetAccessModeAsync(db, parcelId, user, r.Mode, t, ct)));
        group.MapPost("/parcels/{parcelId:guid}/access", async (Guid islandId, Guid parcelId, AccessAddRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandAdmin.AddAccessAsync(db, parcelId, user, r.UserId, r.Kind, r.Minutes, r.Reason, t, ct)));
        group.MapDelete("/parcels/{parcelId:guid}/access/{entryId:guid}", async (Guid islandId, Guid parcelId, Guid entryId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandAdmin.RemoveAccessAsync(db, parcelId, user, entryId, t, ct)));
        group.MapPut("/parcels/{parcelId:guid}/options", async (Guid islandId, Guid parcelId, OptionsRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandAdmin.SetOptionsAsync(db, parcelId, user, r.BuildMode, r.AllowScripts, r.AutoReturnMinutes, t, ct)));
        group.MapPost("/parcels/{parcelId:guid}/eject", async (Guid islandId, Guid parcelId, EjectRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandAdmin.EjectAsync(db, parcelId, user, r.UserId, t, ct)));
        group.MapGet("/parcels/{parcelId:guid}/access", async (Guid islandId, Guid parcelId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            !TryUser(c, out var user) ? Results.Unauthorized() : await LandAdmin.AccessAsync(db, parcelId, user, t, ct) is { } view ? Results.Ok(view) : Results.Forbid());
        group.MapGet("/parcels/{parcelId:guid}/objects", async (Guid islandId, Guid parcelId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            !TryUser(c, out var user) ? Results.Unauthorized() : await LandAdmin.ObjectsAsync(db, parcelId, user, t, ct) is { } list ? Results.Ok(list) : Results.Forbid());
        group.MapPost("/parcels/{parcelId:guid}/objects/return", async (Guid islandId, Guid parcelId, ReturnObjectsRequest r, HttpContext c, LifeVerseDbContext db, Modules.Assets.AssetStorageService storage, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandAdmin.ReturnObjectsAsync(db, storage, parcelId, user, r.Scope, r.ObjectIds, r.Reason, t, ct)));
        group.MapGet("/parcels/{parcelId:guid}/audit", async (Guid islandId, Guid parcelId, HttpContext c, LifeVerseDbContext db, CancellationToken ct) =>
            !TryUser(c, out var user) ? Results.Unauthorized() : await LandAdmin.AuditAsync(db, parcelId, user, ct) is { } list ? Results.Ok(list) : Results.Forbid());

        // opening a parcel to a whole group
        group.MapGet("/parcels/{parcelId:guid}/group-access", async (Guid islandId, Guid parcelId, HttpContext c, LifeVerseDbContext db, CancellationToken ct) =>
            !TryUser(c, out var user) ? Results.Unauthorized() : Groups.GroupEndpoints.Answer(await Groups.GroupService.GroupAccessAsync(db, user, parcelId, ct)));
        group.MapPost("/parcels/{parcelId:guid}/group-access", async (Guid islandId, Guid parcelId, Groups.GroupAccessRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            !TryUser(c, out var user) ? Results.Unauthorized() : Groups.GroupEndpoints.Answer(await Groups.GroupService.GrantGroupAccessAsync(db, user, parcelId, r.GroupId, r.Kind, t.GetUtcNow(), ct)));
        group.MapDelete("/parcels/{parcelId:guid}/group-access/{entryId:guid}", async (Guid islandId, Guid parcelId, Guid entryId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            !TryUser(c, out var user) ? Results.Unauthorized() : Groups.GroupEndpoints.Answer(await Groups.GroupService.RevokeGroupAccessAsync(db, user, parcelId, entryId, t.GetUtcNow(), ct)));

        // how a parcel presents itself: where people arrive, its sky and its music
        group.MapPut("/parcels/{parcelId:guid}/arrival", async (Guid islandId, Guid parcelId, ArrivalRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandPresentation.SetArrivalAsync(db, parcelId, user, r.X, r.Z, r.Yaw, r.Mode, t, ct)));
        group.MapPut("/parcels/{parcelId:guid}/environment", async (Guid islandId, Guid parcelId, EnvironmentRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandPresentation.SetEnvironmentAsync(db, parcelId, user, r.Environment, t, ct)));
        group.MapPut("/parcels/{parcelId:guid}/media", async (Guid islandId, Guid parcelId, MediaRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => LandPresentation.SetMediaAsync(db, parcelId, user, r.MusicUrl, r.Volume, r.Autoplay, r.Name, t, ct)));

        // land for sale or rent on the public islands
        endpoints.MapGet("/api/v1/discovery/properties", async (string? q, string? offer, long? minPrice, long? maxPrice, double? minArea, double? maxArea, string? sort, int? skip, int? take, HttpContext c, LifeVerseDbContext db, CancellationToken ct) =>
            !TryUser(c, out var user) ? Results.Unauthorized() : Results.Ok(await LandSearch.SearchAsync(db, user, new PropertyQuery(q, offer, minPrice, maxPrice, minArea, maxArea, sort, skip ?? 0, take ?? 20), ct))).WithTags("Discovery").RequireAuthorization();

        // the real estate market window: filters, sorting and pages
        endpoints.MapGet("/api/v1/discovery/market", async (string? q, string? offer, string? category, long? minPrice, long? maxPrice, double? minArea, double? maxArea, bool? onlyAvailable, bool? withVisit, string? sort, int? page, int? pageSize, HttpContext c, LifeVerseDbContext db, CancellationToken ct) =>
            !TryUser(c, out var user) ? Results.Unauthorized() : Results.Ok(await LandMarket.SearchAsync(db, user, new MarketQuery(q, offer, category, minPrice, maxPrice, minArea, maxArea, onlyAvailable ?? true, withVisit ?? false, sort, page ?? 1, pageSize ?? 9), ct))).WithTags("Discovery").RequireAuthorization();

        // what the island servers ask for (internal, like the other platform routes)
        endpoints.MapGet("/api/v1/platform/servers/{instanceId:guid}/land-sync", async (Guid instanceId, long? since, HttpContext context, IConfiguration config, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            !Modules.Platform.PlatformEndpoints.HasValidServiceKey(context, config) ? Results.Unauthorized() : await LandSync.BuildAsync(db, instanceId, since ?? 0, t, ct) is { } answer ? Results.Ok(new { access = answer.Access, events = answer.Events, cursor = answer.Cursor, terrain = answer.Terrain, teleport = answer.Teleport }) : Results.NotFound()).WithTags("Platform");
        return endpoints;
    }

    /// <summary>Checks the person and the island, runs the operation and turns its answer into an HTTP one.</summary>
    private static async Task<IResult> Run(HttpContext context, Guid islandId, LifeVerseDbContext db, CancellationToken ct, Func<Guid, Task<CommerceResult>> operation)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        if (!await db.Islands.AsNoTracking().AnyAsync(x => x.Id == islandId && x.DeletedAt == null, ct)) return Results.NotFound();
        return Answer(await operation(user));
    }

    public static IResult Answer(CommerceResult result) => result.Ok ? Results.Ok(result.Data ?? new { }) : result.Error switch
    {
        "not_found" => Results.NotFound(new { error = result.Error }),
        "forbidden" => Results.Json(new { error = result.Error }, statusCode: 403),
        "bad_key" or "bad_price" or "bad_terms" or "bad_periods" or "bad_buyer" or "bad_name" or "empty" or "bad_selection" or "bad_mode" or "bad_kind" or "bad_target" or "bad_duration" or "bad_reason" or "bad_scope" or "bad_primary" or "bad_limit" or "bad_environment" or "bad_volume" or "bad_point" or "too_long" or "not_https" or "bad_host" or "bad_type" or "bad_category" =>Results.BadRequest(new { error = result.Error }),
        _ => Results.Conflict(new { error = result.Error })
    };

    /// <summary>The money history of a parcel, for its owner and its tenant.</summary>
    private static async Task<IResult> HistoryAsync(Guid islandId, Guid parcelId, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        var parcel = await db.IslandParcels.AsNoTracking().SingleOrDefaultAsync(p => p.Id == parcelId && p.IslandId == islandId && p.DeletedAt == null, ct);
        if (parcel is null) return Results.NotFound();
        var tenant = await db.ParcelRentals.AsNoTracking().AnyAsync(r => r.ParcelId == parcelId && r.TenantUserId == user && (r.Status == "active" || r.Status == "grace"), ct);
        if (!tenant && !await LandAuthority.CanManageAsync(db, parcel, user, ct)) return Results.Forbid();
        return Results.Ok(await db.ParcelTransactions.AsNoTracking().Where(t => t.ParcelId == parcelId).OrderByDescending(t => t.CreatedAt).Take(50).Select(t => new { t.Type, t.Amount, t.FromUserId, t.ToUserId, at = t.CreatedAt }).ToListAsync(ct));
    }

    private static bool TryUser(HttpContext context, out Guid user) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out user);

    /// <summary>Whoever may enter the island sees its land: the parcels, their owners and how much is built.</summary>
    private static async Task<IResult> ViewAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct);
        if (island is null) return Results.NotFound();
        var permissions = await db.IslandPermissions.AsNoTracking().Where(p => p.IslandId == islandId && p.UserId == user && p.RevokedAt == null).Select(p => p.Permission).ToListAsync(ct);
        if (!island.IsPublic && island.OwnerUserId != user && !permissions.Any(p => p is "enter" or "admin" or "build" or "edit")) return Results.Forbid();
        return Results.Ok(await LandService.ViewAsync(db, island, user, ct));
    }

    /// <summary>The numbers, parcels, offers, contracts and history behind the land management window. Owner and administrators only.</summary>
    private static async Task<IResult> OverviewAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct);
        if (island is null) return Results.NotFound();
        var now = clock.GetUtcNow();
        if (!await LandOverview.IsStaffAsync(db, island, user, now, ct)) return Results.Forbid();
        return Results.Ok(await LandOverview.BuildAsync(db, island, user, now, ct));
    }

    /// <summary>A cheap number that changes whenever a parcel is added, removed or changed: clients ask for it every few seconds and reload the land only when it moves.</summary>
    private static async Task<IResult> VersionAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct);
        if (island is null) return Results.NotFound();
        var permissions = await db.IslandPermissions.AsNoTracking().Where(p => p.IslandId == islandId && p.UserId == user && p.RevokedAt == null).Select(p => p.Permission).ToListAsync(ct);
        if (!island.IsPublic && island.OwnerUserId != user && !permissions.Any(p => p is "enter" or "admin" or "build" or "edit")) return Results.Forbid();
        return Results.Ok(new { version = await LandService.VersionAsync(db, islandId, ct) });
    }

    /// <summary>Marks off a new parcel. For now only the island owner and administrators may do it; buying and selling come later.</summary>
    private static async Task<IResult> ClaimAsync(Guid islandId, LandClaimRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        var island = await db.Islands.SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct);
        if (island is null) return Results.NotFound();
        var isAdmin = island.OwnerUserId == user || await db.IslandPermissions.AnyAsync(p => p.IslandId == islandId && p.UserId == user && p.RevokedAt == null && p.Permission == "admin", ct);
        if (!isAdmin) return Results.Forbid();
        HashSet<long> cells;
        if (request.Cells is { Length: > 0 })
        {
            if (request.Cells.Length > LandGrid.MaxCellsPerParcel || request.Cells.Any(c => c is not { Length: 2 })) return Results.BadRequest(new { error = "too_big" });
            cells = [.. request.Cells.Select(c => LandGrid.Key(c[0], c[1]))];
        }
        else if (request.MinX is { } a && request.MinZ is { } b && request.MaxX is { } c2 && request.MaxZ is { } d) cells = LandGrid.CellsInRect(a, b, c2, d);
        else return Results.BadRequest(new { error = "empty" });
        var result = await LandService.ClaimAsync(db, island, user, request.Name, request.Description, cells, request.MaxObjects ?? LandBudget.DefaultObjects(island, LandGrid.AreaOf(cells.Count)), null, null, null, null, null, clock, ct);
        if (result.Parcel is null) return result.Error is "overlap" or "name_taken" ? Results.Conflict(new { error = result.Error, parcelId = result.ConflictParcelId }) : Results.BadRequest(new { error = result.Error });
        return Results.Created($"/api/v1/worlds/islands/{islandId}/parcels/{result.Parcel.Id}", new { id = result.Parcel.Id, area = result.Parcel.Area, cells = cells.Count });
    }
}
