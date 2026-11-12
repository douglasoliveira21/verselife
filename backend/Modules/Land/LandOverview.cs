using System.Text.Json;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

/// <summary>
/// What the island's land management window shows: the numbers of the whole territory, every parcel with what it is worth and who holds it, the offers on the market,
/// the rental contracts, how the objects budget is shared and what happened lately. Only the owner and the administrators of the island may read it; everything is
/// counted here from the real parcels, so the window draws nothing that was not sent.
/// </summary>
public static class LandOverview
{
    public static readonly string[] Categories = ["residencial", "comercial", "mista", "social", "comum"];

    /// <summary>The state of a parcel as the territory map colours it: common (a common area), rented, sale (offered for sale), sold (held by somebody other than the island's owner) or available (the owner's, free).</summary>
    public static string KindOf(IslandParcel parcel, Guid islandOwner, bool rented) =>
        parcel.Category == "comum" ? "common" : rented ? "rented" : parcel.SalePrice is not null ? "sale" : parcel.OwnerUserId != islandOwner ? "sold" : "available";

    public static async Task<bool> IsStaffAsync(LifeVerseDbContext db, Island island, Guid user, DateTimeOffset now, CancellationToken ct) =>
        island.OwnerUserId == user || await db.IslandPermissions.AnyAsync(p => p.IslandId == island.Id && p.UserId == user && p.Permission == "admin" && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now), ct);

    /// <summary>Gives each parcel without one its short code (L-01, L-02…) in the order the parcels were made; a code is never given twice on an island.</summary>
    private static async Task EnsureCodesAsync(LifeVerseDbContext db, List<IslandParcel> parcels, CancellationToken ct)
    {
        var missing = parcels.Where(p => string.IsNullOrEmpty(p.Code)).OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToList(); if (missing.Count == 0) return;
        var taken = parcels.Where(p => !string.IsNullOrEmpty(p.Code)).Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = parcels.Select(p => p.Code).Where(c => c.StartsWith("L-", StringComparison.Ordinal) && int.TryParse(c.AsSpan(2), out _)).Select(c => int.Parse(c.AsSpan(2))).DefaultIfEmpty(0).Max() + 1;
        foreach (var parcel in missing) { string code; do code = $"L-{next++:00}"; while (!taken.Add(code)); parcel.Code = code; }
        try { await db.SaveChangesAsync(ct); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException) { foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached; }
    }

    private static async Task<Dictionary<Guid, string>> NamesAsync(LifeVerseDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => wanted.Contains(u.Id)).Select(u => new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        foreach (var avatar in await db.Avatars.AsNoTracking().Where(a => wanted.Contains(a.UserId)).Select(a => new { a.UserId, a.DisplayName }).ToListAsync(ct)) names[avatar.UserId] = avatar.DisplayName;
        return names;
    }

    public static async Task<object> BuildAsync(LifeVerseDbContext db, Island island, Guid viewer, DateTimeOffset now, CancellationToken ct)
    {
        var tracked = await db.IslandParcels.Where(p => p.IslandId == island.Id && p.DeletedAt == null).ToListAsync(ct);
        await EnsureCodesAsync(db, tracked, ct);
        var view = await LandService.ViewAsync(db, island, viewer, ct);
        var byId = view.Parcels.ToDictionary(p => p.Id);
        var parcelIds = tracked.Select(p => p.Id).ToList();
        var rentals = await db.ParcelRentals.AsNoTracking().Where(r => parcelIds.Contains(r.ParcelId)).OrderByDescending(r => r.CreatedAt).Take(400).ToListAsync(ct);
        var live = rentals.Where(r => r.Status is "active" or "grace").GroupBy(r => r.ParcelId).ToDictionary(g => g.Key, g => g.First());
        var changes = await db.ParcelChanges.AsNoTracking().Where(c => parcelIds.Contains(c.ParcelId)).OrderByDescending(c => c.CreatedAt).Take(150).ToListAsync(ct);
        var money = await db.ParcelTransactions.AsNoTracking().Where(t => parcelIds.Contains(t.ParcelId)).OrderByDescending(t => t.CreatedAt).Take(150).ToListAsync(ct);
        var people = tracked.Select(p => p.OwnerUserId).Concat(rentals.SelectMany(r => new[] { r.LandlordUserId, r.TenantUserId })).Concat(changes.Select(c => c.ActorUserId))
            .Concat(money.SelectMany(t => new[] { t.FromUserId, t.ToUserId }).Where(g => g is not null).Select(g => g!.Value)).Append(island.OwnerUserId);
        var names = await NamesAsync(db, people, ct);
        string Name(Guid id) => names.GetValueOrDefault(id, "—");
        var codeOf = tracked.ToDictionary(p => p.Id, p => p.Code); var nameOf = tracked.ToDictionary(p => p.Id, p => p.Name);

        var parcels = new List<object>(); var counts = new Dictionary<string, int> { ["available"] = 0, ["sale"] = 0, ["rented"] = 0, ["sold"] = 0, ["common"] = 0 };
        foreach (var parcel in tracked.OrderBy(p => p.Code, StringComparer.Ordinal))
        {
            if (!byId.TryGetValue(parcel.Id, out var v)) continue;
            live.TryGetValue(parcel.Id, out var rental); var kind = KindOf(parcel, island.OwnerUserId, rental is not null); counts[kind]++;
            parcels.Add(new
            {
                id = parcel.Id, code = parcel.Code, name = parcel.Name, description = parcel.Description, category = parcel.Category, kind, area = parcel.Area, cells = v.Cells, runs = v.Runs,
                ownerId = parcel.OwnerUserId, owner = Name(parcel.OwnerUserId), ownedByIslandOwner = parcel.OwnerUserId == island.OwnerUserId,
                salePrice = parcel.SalePrice, saleExclusive = parcel.SaleBuyerUserId is not null, rentPrice = parcel.RentPrice, rentPeriodDays = parcel.RentPeriodDays,
                tenantId = rental?.TenantUserId, tenant = rental is null ? null : Name(rental.TenantUserId), rentStatus = rental?.Status, rentExpiresAt = rental?.ExpiresAt, rentalId = rental?.Id,
                maxObjects = v.MaxObjects, objects = v.Objects, cost = v.Cost, maxCost = v.MaxCost, scripts = v.AllowScripts, builders = v.Builders, accessMode = v.AccessMode, buildMode = v.BuildMode,
                spawnX = v.ArrivalX, spawnZ = v.ArrivalZ, createdAt = parcel.CreatedAt, revision = parcel.Revision,
            });
        }

        var area = (double)island.Size * island.Size; var parcelled = tracked.Sum(p => p.Area);
        var monthly = (long)Math.Round(live.Values.Where(r => r.LandlordUserId == island.OwnerUserId).Sum(r => r.PricePerPeriod * 30d / Math.Max(1, r.PeriodDays)));
        var soon = live.Values.Count(r => r.ExpiresAt <= now.AddDays(7));
        var objectsLimit = LandBudget.IslandObjects(island);
        var contracts = rentals.Where(r => r.Status is "active" or "grace" || r.CreatedAt > now.AddDays(-90)).Take(150).Select(r => new
        {
            id = r.Id, parcelId = r.ParcelId, code = codeOf.GetValueOrDefault(r.ParcelId, ""), parcel = nameOf.GetValueOrDefault(r.ParcelId, "—"), landlord = Name(r.LandlordUserId), tenant = Name(r.TenantUserId), price = r.PricePerPeriod, periodDays = r.PeriodDays,
            status = r.Status, startsAt = r.StartsAt, expiresAt = r.ExpiresAt, graceUntil = r.GraceUntil, endedAt = r.EndedAt, autoRenew = r.AutoRenew, deposit = r.DepositCoins,
        });
        var history = changes.Select(c => new { at = c.CreatedAt, kind = "change", what = c.Operation, actor = Name(c.ActorUserId), parcelId = c.ParcelId, code = codeOf.GetValueOrDefault(c.ParcelId, ""), parcel = nameOf.GetValueOrDefault(c.ParcelId, "—"), amount = (long?)null })
            .Concat(money.Select(t => new { at = t.CreatedAt, kind = "money", what = t.Type, actor = t.FromUserId is { } f ? Name(f) : t.ToUserId is { } to ? Name(to) : "—", parcelId = t.ParcelId, code = codeOf.GetValueOrDefault(t.ParcelId, ""), parcel = nameOf.GetValueOrDefault(t.ParcelId, "—"), amount = (long?)t.Amount }))
            .OrderByDescending(h => h.at).Take(200).ToList();

        return new
        {
            island = new { id = island.Id, name = island.Name, size = island.Size, coverImageUrl = island.CoverImageUrl, runtimeState = island.RuntimeState, isPublic = island.IsPublic, owner = Name(island.OwnerUserId), ownerId = island.OwnerUserId },
            totals = new
            {
                area, areaParcelled = parcelled, areaFree = Math.Max(0, area - parcelled), parcels = parcels.Count, available = counts["available"], onSale = counts["sale"], rented = counts["rented"], sold = counts["sold"], common = counts["common"],
                monthlyRevenue = monthly, expiringSoon = soon, objectsLimit, objectsUsed = view.Parcels.Sum(p => p.Objects), objectsShared = tracked.Sum(p => p.MaxObjects),
            },
            categories = Categories, version = view.Version, parcels, contracts, history,
        };
    }

    /// <summary>Changes what a parcel is for. The owner of the parcel and the island's staff may.</summary>
    public static async Task<CommerceResult> SetCategoryAsync(LifeVerseDbContext db, Guid islandId, Guid parcelId, Guid actor, string? category, TimeProvider clock, CancellationToken ct)
    {
        var parcel = await db.IslandParcels.SingleOrDefaultAsync(p => p.Id == parcelId && p.IslandId == islandId && p.DeletedAt == null, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        var island = await db.Islands.AsNoTracking().SingleAsync(i => i.Id == islandId, ct); var now = clock.GetUtcNow();
        if (!await LandAuthority.CanManageAsync(db, parcel, actor, ct) && !await IsStaffAsync(db, island, actor, now, ct)) return CommerceResult.Fail("forbidden");
        var chosen = (category ?? "").Trim().ToLowerInvariant(); if (!Categories.Contains(chosen)) return CommerceResult.Fail("bad_category");
        if (parcel.Category == chosen) return CommerceResult.Done(new { parcelId, category = chosen });
        var before = parcel.Category; parcel.Category = chosen; parcel.Revision++; parcel.UpdatedAt = now;
        db.ParcelChanges.Add(new ParcelChange { Id = Guid.NewGuid(), ParcelId = parcel.Id, ActorUserId = actor, Operation = "category", PayloadJson = JsonSerializer.Serialize(new { before, after = chosen }), Revision = parcel.Revision, CreatedAt = now });
        try { await db.SaveChangesAsync(ct); return CommerceResult.Done(new { parcelId, category = chosen }); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException or InvalidOperationException)
        {
            foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;
            return CommerceResult.Fail("conflict");
        }
    }
}
