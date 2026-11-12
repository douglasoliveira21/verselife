using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

public static partial class LandMarket
{
    /// <summary>The islands that are for sale or for rent, with the same filters as the land (a category filter keeps islands out: the categories of land are not the categories of islands).</summary>
    private static async Task<(int Total, List<MarketItem> Items)> IslandsAsync(LifeVerseDbContext db, Guid viewer, MarketQuery query, string? region, int wanted, CancellationToken ct)
    {
        var offer = query.Offer is "sale" or "rent" ? query.Offer : "any"; var onlyAvailable = query.OnlyAvailable;
        var leased = db.IslandLeases.AsNoTracking().Where(l => l.Status == "active" || l.Status == "grace");
        var joined = from i in db.Islands.AsNoTracking().Where(i => i.DeletedAt == null && i.IsPublic && !i.IsWelcome)
                     let isRented = leased.Any(l => l.IslandId == i.Id)
                     let saleOk = i.SalePrice != null && (i.SaleBuyerUserId == null || i.SaleBuyerUserId == viewer) && i.OwnerUserId != viewer
                     let rentOk = i.RentPrice != null && !isRented && i.OwnerUserId != viewer
                     let leasePrice = leased.Where(l => l.IslandId == i.Id).Select(l => (long?)l.PricePerPeriod).FirstOrDefault()
                     let leaseDays = leased.Where(l => l.IslandId == i.Id).Select(l => (int?)l.PeriodDays).FirstOrDefault()
                     let rentShown = i.OwnerUserId != viewer && (rentOk || (!onlyAvailable && isRented))
                     let price = saleOk ? i.SalePrice : (isRented ? leasePrice : i.RentPrice)
                     let area = (double)i.Size * i.Size
                     where (offer == "sale" && saleOk) || (offer == "rent" && rentShown) || (offer == "any" && (saleOk || rentShown))
                     select new { i, saleOk, isRented, price, leaseDays, area };
        if (region == "full") joined = joined.Where(x => x.i.Size >= 256); else if (region == "homestead") joined = joined.Where(x => x.i.Size < 256);
        if (query.WithVisit) joined = joined.Where(x => !x.i.AccessClosed);
        if (!string.IsNullOrWhiteSpace(query.Q)) { var q = query.Q.Trim().ToLowerInvariant(); joined = joined.Where(x => x.i.Name.ToLower().Contains(q) || x.i.Description.ToLower().Contains(q)); }
        if (query.MinArea is { } minArea) joined = joined.Where(x => x.area >= minArea);
        if (query.MaxArea is { } maxArea) joined = joined.Where(x => x.area <= maxArea);
        if (query.MinPrice is { } minPrice) joined = joined.Where(x => x.price >= minPrice);
        if (query.MaxPrice is { } maxPrice) joined = joined.Where(x => x.price <= maxPrice);
        var total = await joined.CountAsync(ct);
        var ordered = query.Sort switch
        {
            "price" => joined.OrderBy(x => x.price), "price_desc" => joined.OrderByDescending(x => x.price), "area" => joined.OrderByDescending(x => x.area),
            _ => joined.OrderByDescending(x => x.i.UpdatedAt), // "prims" is sorted after the objects limits are read (it lives in a JSON setting)
        };
        var rows = await ordered.ThenBy(x => x.i.Id).Take(query.Sort == "prims" ? Math.Max(wanted, 200) : wanted).ToListAsync(ct);
        var owners = rows.Select(r => r.i.OwnerUserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => owners.Contains(u.Id)).Select(u => new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        foreach (var avatar in await db.Avatars.AsNoTracking().Where(a => owners.Contains(a.UserId)).Select(a => new { a.UserId, a.DisplayName }).ToListAsync(ct)) names[avatar.UserId] = avatar.DisplayName;
        var items = rows.Select(r => new MarketItem(Guid.Empty, r.i.Id, r.i.Name, r.i.CoverImageUrl, "", r.i.Name, r.i.Description, r.i.Category, r.area, LandBudget.IslandObjects(r.i),
            r.saleOk ? "sale" : "rent", r.saleOk ? "sale" : r.isRented ? "rented" : "rent", r.price ?? 0, r.saleOk ? 0 : (r.isRented ? r.leaseDays ?? 0 : r.i.RentPeriodDays), names.GetValueOrDefault(r.i.OwnerUserId, "—"),
            !r.i.AccessClosed, r.i.UpdatedAt, r.i.RentMinPeriods, r.i.RentMaxPeriods, r.i.RentDeposit, "island", IslandCommerce.RegionOf(r.i), "", r.i.RentAutoRenewAllowed)).ToList();
        if (query.Sort == "prims") items = [.. items.OrderByDescending(i => i.MaxObjects).Take(wanted)];
        return (total, items);
    }
}
