using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

public sealed record PropertyQuery(string? Q, string? Offer, long? MinPrice, long? MaxPrice, double? MinArea, double? MaxArea, string? Sort, int Skip = 0, int Take = 20);

public sealed record PropertyHit(Guid ParcelId, Guid IslandId, string IslandName, string Name, string Description, double Area, string Offer, long Price, int RentPeriodDays, string OwnerName, DateTimeOffset UpdatedAt);

/// <summary>
/// Finds land that can be bought or rented across the public islands. Only offers the person could actually take show up: a sale kept for
/// somebody else, land that is already rented, and anything on a private island are left out.
/// </summary>
public static class LandSearch
{
    public const int MaxTake = 50;

    public static async Task<IReadOnlyList<PropertyHit>> SearchAsync(LifeVerseDbContext db, Guid viewer, PropertyQuery query, CancellationToken ct)
    {
        var offer = query.Offer is "sale" or "rent" ? query.Offer : "any";
        var rented = db.ParcelRentals.AsNoTracking().Where(r => r.Status == "active" || r.Status == "grace").Select(r => r.ParcelId);
        var parcels = db.IslandParcels.AsNoTracking().Where(p => p.DeletedAt == null);
        var islands = db.Islands.AsNoTracking().Where(i => i.DeletedAt == null && i.IsPublic);

        var joined = from p in parcels
                     join i in islands on p.IslandId equals i.Id
                     let saleOk = p.SalePrice != null && (p.SaleBuyerUserId == null || p.SaleBuyerUserId == viewer) && p.OwnerUserId != viewer
                     let rentOk = p.RentPrice != null && !rented.Contains(p.Id) && p.OwnerUserId != viewer
                     where (offer == "sale" && saleOk) || (offer == "rent" && rentOk) || (offer == "any" && (saleOk || rentOk))
                     select new { p, IslandName = i.Name, saleOk, rentOk };

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim().ToLowerInvariant();
            joined = joined.Where(x => x.p.Name.ToLower().Contains(q) || x.p.Description.ToLower().Contains(q) || x.IslandName.ToLower().Contains(q));
        }
        if (query.MinArea is { } minArea) joined = joined.Where(x => x.p.Area >= minArea);
        if (query.MaxArea is { } maxArea) joined = joined.Where(x => x.p.Area <= maxArea);
        // the price compared is the one of the offer being looked at (a sale price when both exist and the person wants "any")
        if (query.MinPrice is { } minPrice) joined = joined.Where(x => (x.saleOk ? x.p.SalePrice : x.p.RentPrice) >= minPrice);
        if (query.MaxPrice is { } maxPrice) joined = joined.Where(x => (x.saleOk ? x.p.SalePrice : x.p.RentPrice) <= maxPrice);

        var ordered = query.Sort switch
        {
            "price" => joined.OrderBy(x => x.saleOk ? x.p.SalePrice : x.p.RentPrice).ThenBy(x => x.p.Id),
            "price_desc" => joined.OrderByDescending(x => x.saleOk ? x.p.SalePrice : x.p.RentPrice).ThenBy(x => x.p.Id),
            "area" => joined.OrderByDescending(x => x.p.Area).ThenBy(x => x.p.Id),
            _ => joined.OrderByDescending(x => x.p.UpdatedAt).ThenBy(x => x.p.Id)
        };
        var rows = await ordered.Skip(Math.Max(0, query.Skip)).Take(Math.Clamp(query.Take, 1, MaxTake)).ToListAsync(ct);
        var owners = rows.Select(r => r.p.OwnerUserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => owners.Contains(u.Id)).Select(u => new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        foreach (var avatar in await db.Avatars.AsNoTracking().Where(a => owners.Contains(a.UserId)).Select(a => new { a.UserId, a.DisplayName }).ToListAsync(ct)) names[avatar.UserId] = avatar.DisplayName;
        return [.. rows.Select(r => new PropertyHit(r.p.Id, r.p.IslandId, r.IslandName, r.p.Name, r.p.Description, r.p.Area,
            r.saleOk ? "sale" : "rent", (r.saleOk ? r.p.SalePrice : r.p.RentPrice) ?? 0, r.saleOk ? 0 : r.p.RentPeriodDays, names.GetValueOrDefault(r.p.OwnerUserId, "—"), r.p.UpdatedAt))];
    }
}
