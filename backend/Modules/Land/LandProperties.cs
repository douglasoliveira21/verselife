using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

/// <summary>
/// "Minhas Propriedades": everything a person holds in the real estate of the game, counted from the real islands, parcels, contracts and money records: the islands they own, the land they own, what they rent out and
/// what they rent, what they have on offer, what they paid and received, and the summary of the account. Nothing here is invented: a thing the game does not keep (such as a hosting plan) is not in the answer.
/// </summary>
public static class LandProperties
{
    private const int MaxIslandsForUsage = 40;

    public static string KindOf(IslandParcel parcel, bool rentedOut) => parcel.Category == "comum" ? "common" : rentedOut ? "rented" : parcel.SalePrice is not null ? "sale" : parcel.RentPrice is not null ? "rent" : "available";

    public static async Task<object> BuildAsync(LifeVerseDbContext db, Guid me, DateTimeOffset now, CancellationToken ct)
    {
        var islands = await db.Islands.AsNoTracking().Where(i => i.OwnerUserId == me && i.DeletedAt == null).OrderBy(i => i.Name).ToListAsync(ct);
        var mine = await db.IslandParcels.AsNoTracking().Where(p => p.OwnerUserId == me && p.DeletedAt == null).ToListAsync(ct);
        var rentalsAsTenant = await db.ParcelRentals.AsNoTracking().Where(r => r.TenantUserId == me).OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync(ct);
        var myParcelIds = mine.Select(p => p.Id).ToList();
        var rentalsAsLandlord = await db.ParcelRentals.AsNoTracking().Where(r => r.LandlordUserId == me || myParcelIds.Contains(r.ParcelId)).OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync(ct);
        var rentalParcelIds = rentalsAsTenant.Select(r => r.ParcelId).Concat(rentalsAsLandlord.Select(r => r.ParcelId)).Distinct().ToList();
        var extra = await db.IslandParcels.AsNoTracking().Where(p => rentalParcelIds.Contains(p.Id) && !myParcelIds.Contains(p.Id)).ToListAsync(ct);
        var parcelById = mine.Concat(extra).ToDictionary(p => p.Id);
        var islandIds = islands.Select(i => i.Id).Concat(parcelById.Values.Select(p => p.IslandId)).Distinct().ToList();
        var islandById = (await db.Islands.AsNoTracking().Where(i => islandIds.Contains(i.Id)).ToListAsync(ct)).ToDictionary(i => i.Id);
        var parcelIds = parcelById.Keys.ToList();
        var payments = await db.ParcelTransactions.AsNoTracking().Where(t => (t.FromUserId == me || t.ToUserId == me) && (t.Type == "sale" || t.Type == "rent_start" || t.Type == "rent_payment" || t.Type == "rent_end")).OrderByDescending(t => t.CreatedAt).Take(200).ToListAsync(ct);
        var paymentParcels = payments.Select(t => t.ParcelId).Where(id => !parcelById.ContainsKey(id)).Distinct().ToList();
        foreach (var p in await db.IslandParcels.AsNoTracking().Where(p => paymentParcels.Contains(p.Id)).ToListAsync(ct)) parcelById[p.Id] = p;
        var moreIslands = parcelById.Values.Select(p => p.IslandId).Where(id => !islandById.ContainsKey(id)).Distinct().ToList();
        foreach (var i in await db.Islands.AsNoTracking().Where(i => moreIslands.Contains(i.Id)).ToListAsync(ct)) islandById[i.Id] = i;

        var myIslandIds = islands.Select(i => i.Id).ToList();
        var leasesAsTenant = await db.IslandLeases.AsNoTracking().Where(l => l.TenantUserId == me).OrderByDescending(l => l.CreatedAt).Take(100).ToListAsync(ct);
        var leasesAsLandlord = await db.IslandLeases.AsNoTracking().Where(l => l.LandlordUserId == me || myIslandIds.Contains(l.IslandId)).OrderByDescending(l => l.CreatedAt).Take(100).ToListAsync(ct);
        var islandPayments = await db.IslandTransactions.AsNoTracking().Where(t => (t.FromUserId == me || t.ToUserId == me) && (t.Type == "sale" || t.Type == "rent_start" || t.Type == "rent_payment" || t.Type == "rent_end")).OrderByDescending(t => t.CreatedAt).Take(200).ToListAsync(ct);
        var moreIslandIds = leasesAsTenant.Select(l => l.IslandId).Concat(leasesAsLandlord.Select(l => l.IslandId)).Concat(islandPayments.Select(t => t.IslandId)).Where(id => !islandById.ContainsKey(id)).Distinct().ToList();
        foreach (var extraIsland in await db.Islands.AsNoTracking().Where(i => moreIslandIds.Contains(i.Id)).ToListAsync(ct)) islandById[extraIsland.Id] = extraIsland;
        var people = rentalsAsTenant.SelectMany(r => new[] { r.LandlordUserId, r.TenantUserId }).Concat(rentalsAsLandlord.SelectMany(r => new[] { r.LandlordUserId, r.TenantUserId }))
            .Concat(payments.SelectMany(t => new[] { t.FromUserId, t.ToUserId }).Where(g => g is not null).Select(g => g!.Value)).Concat(islandPayments.SelectMany(t => new[] { t.FromUserId, t.ToUserId }).Where(g => g is not null).Select(g => g!.Value))
            .Concat(leasesAsTenant.SelectMany(l => new[] { l.LandlordUserId, l.TenantUserId })).Concat(leasesAsLandlord.SelectMany(l => new[] { l.LandlordUserId, l.TenantUserId })).Append(me);
        var names = await LandOverview.NamesAsync(db, people, ct);
        string Name(Guid id) => names.GetValueOrDefault(id, "—");
        string IslandName(Guid id) => islandById.TryGetValue(id, out var i) ? i.Name : "—";
        string Code(Guid id) => parcelById.TryGetValue(id, out var p) ? p.Code : ""; string ParcelName(Guid id) => parcelById.TryGetValue(id, out var p) ? p.Name : "—";
        Guid IslandOf(Guid parcelId) => parcelById.TryGetValue(parcelId, out var p) ? p.IslandId : Guid.Empty;

        // islands: who is on them, how full the objects budget is
        var instances = await db.ServerInstances.AsNoTracking().Where(i => islands.Select(x => x.Id).Contains(i.IslandId) && i.StoppedAt == null && i.LastHeartbeatAt > now.AddSeconds(-60)).ToListAsync(ct);
        var counts = await db.BuildObjects.AsNoTracking().Where(o => o.DeletedAt == null && islands.Select(x => x.Id).Contains(o.IslandId)).GroupBy(o => o.IslandId).Select(g => new { Island = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Island, g => g.Count, ct);
        var parcelsPerIsland = await db.IslandParcels.AsNoTracking().Where(p => p.DeletedAt == null && islands.Select(x => x.Id).Contains(p.IslandId)).GroupBy(p => p.IslandId).Select(g => new { Island = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Island, g => g.Count, ct);
        var liveLeaseByIsland = leasesAsLandlord.Where(l => l.Status is "active" or "grace").GroupBy(l => l.IslandId).ToDictionary(g => g.Key, g => g.First());
        var islandRows = islands.Select(i =>
        {
            var instance = instances.Where(x => x.IslandId == i.Id).OrderByDescending(x => x.StartedAt).FirstOrDefault();
            var state = instance is null ? (i.RuntimeState is "starting" or "restarting" ? i.RuntimeState : "offline") : instance.Status == "running" ? "online" : instance.Status;
            liveLeaseByIsland.TryGetValue(i.Id, out var lease);
            return new { id = i.Id, name = i.Name, coverImageUrl = i.CoverImageUrl, region = IslandCommerce.RegionOf(i), salePrice = i.SalePrice, rentPrice = i.RentPrice, rentPeriodDays = i.RentPeriodDays, tenant = lease is null ? null : Name(lease.TenantUserId), leaseExpiresAt = lease?.ExpiresAt, leaseId = lease?.Id, state, visitors = instance?.PlayerCount ?? 0, maxPlayers = i.MaxPlayers, objects = counts.GetValueOrDefault(i.Id), maxObjects = LandBudget.IslandObjects(i), size = i.Size, isPublic = i.IsPublic, parcels = parcelsPerIsland.GetValueOrDefault(i.Id), createdAt = i.CreatedAt };
        }).ToList();

        // objects used per parcel, island by island
        var usage = new Dictionary<Guid, int>();
        foreach (var island in islandById.Values.Where(i => mine.Any(p => p.IslandId == i.Id)).Take(MaxIslandsForUsage))
            foreach (var view in (await LandService.ViewAsync(db, island, me, ct)).Parcels) usage[view.Id] = view.Objects;

        var liveByParcel = rentalsAsLandlord.Where(r => r.Status is "active" or "grace").GroupBy(r => r.ParcelId).ToDictionary(g => g.Key, g => g.First());
        var parcelRows = mine.OrderBy(p => islandById.GetValueOrDefault(p.IslandId)?.Name).ThenBy(p => p.Code).ThenBy(p => p.Name).Select(p =>
        {
            liveByParcel.TryGetValue(p.Id, out var rental); var kind = KindOf(p, rental is not null);
            return new
            {
                id = p.Id, islandId = p.IslandId, island = IslandName(p.IslandId), code = p.Code, name = p.Name, category = p.Category, area = p.Area, maxObjects = p.MaxObjects, objects = usage.GetValueOrDefault(p.Id), kind,
                salePrice = p.SalePrice, rentPrice = p.RentPrice, rentPeriodDays = p.RentPeriodDays, tenant = rental is null ? null : Name(rental.TenantUserId), rentExpiresAt = rental?.ExpiresAt, rentalId = rental?.Id,
                canVisit = p.AccessMode == "public" && p.TeleportMode != "disabled", imageUrl = p.ImageUrl, createdAt = p.CreatedAt, revision = p.Revision,
            };
        }).ToList();

        object Contract(ParcelRental r) => new
        {
            target = "parcel", id = r.Id, parcelId = r.ParcelId, islandId = IslandOf(r.ParcelId), island = IslandName(IslandOf(r.ParcelId)), code = Code(r.ParcelId), parcel = ParcelName(r.ParcelId), landlord = Name(r.LandlordUserId), tenant = Name(r.TenantUserId),
            role = r.TenantUserId == me ? "tenant" : "landlord", price = r.PricePerPeriod, periodDays = r.PeriodDays, status = r.Status, startsAt = r.StartsAt, expiresAt = r.ExpiresAt, endedAt = r.EndedAt, autoRenew = r.AutoRenew, deposit = r.DepositCoins,
            canVisit = parcelById.TryGetValue(r.ParcelId, out var p) && p.AccessMode == "public" && p.TeleportMode != "disabled",
        };
        object LeaseContract(IslandLease l) => new
        {
            target = "island", id = l.Id, parcelId = Guid.Empty, islandId = l.IslandId, island = IslandName(l.IslandId), code = "", parcel = IslandName(l.IslandId), landlord = Name(l.LandlordUserId), tenant = Name(l.TenantUserId),
            role = l.TenantUserId == me ? "tenant" : "landlord", price = l.PricePerPeriod, periodDays = l.PeriodDays, status = l.Status, startsAt = l.StartsAt, expiresAt = l.ExpiresAt, endedAt = l.EndedAt, autoRenew = false, deposit = l.DepositCoins,
            canVisit = islandById.TryGetValue(l.IslandId, out var leased) && !leased.AccessClosed,
        };
        var contracts = rentalsAsTenant.Concat(rentalsAsLandlord).GroupBy(r => r.Id).Select(g => g.First()).Select(r => (r.CreatedAt, Row: Contract(r)))
            .Concat(leasesAsTenant.Concat(leasesAsLandlord).GroupBy(l => l.Id).Select(g => g.First()).Select(l => (l.CreatedAt, Row: LeaseContract(l)))).OrderByDescending(x => x.CreatedAt).Take(250).Select(x => x.Row).ToList();
        var liveTenant = rentalsAsTenant.Where(r => r.Status is "active" or "grace").ToList();
        var liveLandlord = rentalsAsLandlord.Where(r => r.Status is "active" or "grace" && r.LandlordUserId == me).ToList();
        var leaseTenant = leasesAsTenant.Where(l => l.Status is "active" or "grace").ToList();
        var leaseLandlord = leasesAsLandlord.Where(l => l.Status is "active" or "grace" && l.LandlordUserId == me).ToList();

        var listings = mine.Where(p => p.SalePrice is not null || (p.RentPrice is not null && !liveByParcel.ContainsKey(p.Id))).SelectMany(p =>
        {
            var rows = new List<object>();
            if (p.SalePrice is { } sale) rows.Add(new { kind = "sale", parcelId = p.Id, islandId = p.IslandId, island = IslandName(p.IslandId), code = p.Code, name = p.Name, area = p.Area, price = sale, periodDays = 0, exclusive = p.SaleBuyerUserId is not null, since = p.UpdatedAt });
            if (p.RentPrice is { } rent && !liveByParcel.ContainsKey(p.Id)) rows.Add(new { kind = "rent", parcelId = p.Id, islandId = p.IslandId, island = IslandName(p.IslandId), code = p.Code, name = p.Name, area = p.Area, price = rent, periodDays = p.RentPeriodDays, exclusive = false, since = p.UpdatedAt });
            return rows;
        }).ToList();
        foreach (var i in islands)
        {
            if (i.SalePrice is { } islandSale) listings.Add(new { kind = "sale", target = "island", parcelId = Guid.Empty, islandId = i.Id, island = i.Name, code = "", name = i.Name, area = (double)i.Size * i.Size, price = islandSale, periodDays = 0, exclusive = i.SaleBuyerUserId is not null, since = i.UpdatedAt });
            if (i.RentPrice is { } islandRent && !liveLeaseByIsland.ContainsKey(i.Id)) listings.Add(new { kind = "rent", target = "island", parcelId = Guid.Empty, islandId = i.Id, island = i.Name, code = "", name = i.Name, area = (double)i.Size * i.Size, price = islandRent, periodDays = i.RentPeriodDays, exclusive = false, since = i.UpdatedAt });
        }

        var paymentRows = payments.Select(t => (t.CreatedAt, Row: (object)new
        {
            target = "parcel",
            id = t.Id, at = t.CreatedAt, type = t.Type, direction = t.ToUserId == me && t.FromUserId != me ? "in" : "out", amount = t.Amount, parcelId = t.ParcelId, island = IslandName(IslandOf(t.ParcelId)), code = Code(t.ParcelId), parcel = ParcelName(t.ParcelId),
            other = t.ToUserId == me ? (t.FromUserId is { } f ? Name(f) : "—") : (t.ToUserId is { } to ? Name(to) : "—"),
        }))
            .Concat(islandPayments.Select(t => (t.CreatedAt, Row: (object)new
            {
                target = "island", id = t.Id, at = t.CreatedAt, type = t.Type, direction = t.ToUserId == me && t.FromUserId != me ? "in" : "out", amount = t.Amount, parcelId = Guid.Empty, island = IslandName(t.IslandId), code = "", parcel = IslandName(t.IslandId),
                other = t.ToUserId == me ? (t.FromUserId is { } f ? Name(f) : "—") : (t.ToUserId is { } to ? Name(to) : "—"),
            }))).OrderByDescending(x => x.CreatedAt).Take(200).Select(x => x.Row).ToList();

        var rentedOut = parcelRows.Count(p => p.kind == "rented") + islandRows.Count(i => i.tenant is not null);
        var inUse = islandRows.Count(i => i.state == "online" && i.tenant is null) + parcelRows.Count(p => p.kind != "rented") + liveTenant.Count + leaseTenant.Count;
        var monthly = (long)Math.Round(liveLandlord.Sum(r => r.PricePerPeriod * 30d / Math.Max(1, r.PeriodDays)) + leaseLandlord.Sum(l => l.PricePerPeriod * 30d / Math.Max(1, l.PeriodDays)));
        var soon = liveLandlord.Concat(liveTenant).GroupBy(r => r.Id).Count(g => g.First().ExpiresAt <= now.AddDays(7)) + leaseLandlord.Concat(leaseTenant).GroupBy(l => l.Id).Count(g => g.First().ExpiresAt <= now.AddDays(7));
        return new
        {
            summary = new { total = islandRows.Count + parcelRows.Count + liveTenant.Count + leaseTenant.Count, inUse, rentedOut, monthlyRevenue = monthly, expiringSoon = soon, islands = islandRows.Count, parcels = parcelRows.Count, rentedIn = liveTenant.Count + leaseTenant.Count },
            islands = islandRows, parcels = parcelRows, contracts, listings, payments = paymentRows,
        };
    }
}
