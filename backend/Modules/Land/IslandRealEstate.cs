using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

public sealed record TransferRequest(string Name);

/// <summary>The real estate of a whole island: what the "Propriedade" tab shows and the calls that sell, rent, hand over and take back an island.</summary>
public static class IslandRealEstate
{
    public static IEndpointRouteBuilder MapIslandRealEstateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/worlds/islands/{islandId:guid}/real-estate").WithTags("Land").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (Guid.TryParse(http.GetRouteValue("islandId")?.ToString(), out var island) && Guid.TryParse(http.GetRouteValue("leaseId")?.ToString(), out var lease)
                && !await http.RequestServices.GetRequiredService<LifeVerseDbContext>().IslandLeases.AsNoTracking().AnyAsync(l => l.Id == lease && l.IslandId == island, http.RequestAborted)) return Results.NotFound();
            return await next(context);
        });
        group.MapGet("", ViewAsync);
        group.MapPost("/sale", async (Guid islandId, SaleRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => IslandCommerce.OfferForSaleAsync(db, islandId, user, new SaleOffer(r.Price, r.BuyerUserId, r.Description, r.IncludeObjects), t, ct)));
        group.MapDelete("/sale", async (Guid islandId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => IslandCommerce.CancelSaleAsync(db, islandId, user, t, ct)));
        group.MapPost("/buy", async (Guid islandId, BuyRequest r, HttpContext c, LifeVerseDbContext db, AssetStorageService storage, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => IslandCommerce.BuyAsync(db, storage, islandId, user, r.IdempotencyKey, r.ExpectedPrice, t, ct)));
        group.MapPost("/transfer", async (Guid islandId, TransferRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => IslandCommerce.TransferAsync(db, islandId, user, r.Name, t, ct)));
        group.MapPost("/rental-offer", async (Guid islandId, RentOfferRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => IslandCommerce.OfferForRentAsync(db, islandId, user, new RentOffer(r.PricePerPeriod, r.PeriodDays, r.MinPeriods, r.MaxPeriods, r.DepositCoins, r.GraceDays, r.AutoRenewAllowed), t, ct)));
        group.MapDelete("/rental-offer", async (Guid islandId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => IslandCommerce.CancelRentOfferAsync(db, islandId, user, t, ct)));
        group.MapPost("/rent", async (Guid islandId, RentRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => IslandCommerce.RentAsync(db, islandId, user, r.Periods, r.IdempotencyKey, r.ExpectedPrice, t, ct, r.AutoRenew)));
        group.MapPost("/leases/{leaseId:guid}/renew", async (Guid islandId, Guid leaseId, RenewRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => IslandCommerce.RenewAsync(db, leaseId, user, r.Periods, r.IdempotencyKey, t, ct)));
        group.MapPost("/leases/{leaseId:guid}/end", async (Guid islandId, Guid leaseId, HttpContext c, LifeVerseDbContext db, AssetStorageService storage, TimeProvider t, CancellationToken ct) =>
            await Run(c, islandId, db, ct, user => IslandCommerce.EndAsync(db, storage, leaseId, user, t, ct)));
        return endpoints;
    }

    private static bool TryUser(HttpContext c, out Guid user) => Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out user);

    private static async Task<IResult> Run(HttpContext context, Guid islandId, LifeVerseDbContext db, CancellationToken ct, Func<Guid, Task<CommerceResult>> operation)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        if (!await db.Islands.AsNoTracking().AnyAsync(x => x.Id == islandId && x.DeletedAt == null, ct)) return Results.NotFound();
        return LandEndpoints.Answer(await operation(user));
    }

    private static async Task<IResult> ViewAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return Results.NotFound();
        var now = clock.GetUtcNow(); var staff = await LandOverview.IsStaffAsync(db, island, user, now, ct);
        if (!island.IsPublic && !staff) return Results.Forbid();
        return Results.Ok(await ViewOfAsync(db, island, user, staff, now, ct));
    }

    public static async Task<object> ViewOfAsync(LifeVerseDbContext db, Island island, Guid viewer, bool staff, DateTimeOffset now, CancellationToken ct)
    {
        var lease = await db.IslandLeases.AsNoTracking().Where(l => l.IslandId == island.Id && (l.Status == "active" || l.Status == "grace")).OrderByDescending(l => l.CreatedAt).FirstOrDefaultAsync(ct);
        var people = new List<Guid> { island.OwnerUserId }; if (lease is not null) people.Add(lease.TenantUserId);
        var history = staff ? await db.IslandTransactions.AsNoTracking().Where(t => t.IslandId == island.Id).OrderByDescending(t => t.CreatedAt).Take(30).ToListAsync(ct) : [];
        people.AddRange(history.SelectMany(t => new[] { t.FromUserId, t.ToUserId }).Where(g => g is not null).Select(g => g!.Value));
        var names = await LandOverview.NamesAsync(db, people, ct); string Name(Guid id) => names.GetValueOrDefault(id, "—");
        var forMe = island.SaleBuyerUserId is null || island.SaleBuyerUserId == viewer || staff;
        var parcels = await db.IslandParcels.AsNoTracking().CountAsync(p => p.IslandId == island.Id && p.DeletedAt == null, ct);
        // the platform plan is the owner's bill: nobody else sees it
        var host = island.OwnerUserId == viewer ? await (from r in db.IslandRentals.AsNoTracking() join plan in db.IslandRentalPlans.AsNoTracking() on r.PlanId equals plan.Id where r.IslandId == island.Id && r.Status == "active" orderby r.ExpiresAt descending select new { r.ExpiresAt, r.RecoveryUntil, Plan = plan.Name, plan.MaxPlayers, plan.MaxObjects }).FirstOrDefaultAsync(ct) : null;
        return new
        {
            hosting = host is null ? null : new { plan = host.Plan, status = host.ExpiresAt > now ? "active" : "recovery", expiresAt = host.ExpiresAt, recoveryUntil = host.RecoveryUntil, maxPlayers = host.MaxPlayers, maxObjects = host.MaxObjects },
            islandId = island.Id, name = island.Name, size = island.Size, region = IslandCommerce.RegionOf(island), ownerId = island.OwnerUserId, owner = Name(island.OwnerUserId), createdAt = island.CreatedAt, parcels,
            isOwner = island.OwnerUserId == viewer, isTenant = lease?.TenantUserId == viewer, isWelcome = island.IsWelcome,
            sale = island.SalePrice is { } price && forMe ? new { price, exclusive = island.SaleBuyerUserId is not null, includeObjects = island.SaleIncludesObjects, description = island.SaleDescription } : null,
            rentOffer = island.RentPrice is { } rent ? new { price = rent, periodDays = island.RentPeriodDays, minPeriods = island.RentMinPeriods, maxPeriods = island.RentMaxPeriods, deposit = island.RentDeposit, graceDays = island.RentGraceDays, autoRenewAllowed = island.RentAutoRenewAllowed } : null,
            lease = lease is not null && (staff || lease.TenantUserId == viewer) ? new { id = lease.Id, tenant = Name(lease.TenantUserId), landlord = Name(lease.LandlordUserId), price = lease.PricePerPeriod, periodDays = lease.PeriodDays, status = lease.Status, startsAt = lease.StartsAt, expiresAt = lease.ExpiresAt, deposit = lease.DepositCoins, autoRenew = lease.AutoRenew } : null,
            history = history.Select(t => new { at = t.CreatedAt, type = t.Type, amount = t.Amount, from = t.FromUserId is { } f ? Name(f) : null, to = t.ToUserId is { } to ? Name(to) : null }),
        };
    }
}
