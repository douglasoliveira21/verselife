using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

public sealed record MarketQuery(string? Q, string? Offer, string? Category, long? MinPrice, long? MaxPrice, double? MinArea, double? MaxArea, bool OnlyAvailable, bool WithVisit, string? Sort, int Page = 1, int PageSize = 9);

public sealed record MarketItem(Guid ParcelId, Guid IslandId, string IslandName, string? IslandCover, string Code, string Name, string Description, string Category, double Area, int MaxObjects, string Offer, string State, long Price, int RentPeriodDays, string OwnerName, bool CanVisit, DateTimeOffset UpdatedAt, int RentMinPeriods = 1, int RentMaxPeriods = 1, long RentDeposit = 0);

public sealed record MarketPage(int Total, int Page, int PageSize, IReadOnlyList<MarketItem> Items);

/// <summary>
/// The real estate market: land that is for sale or for rent on the public islands, with the filters, sorting and pages of the market window. Like the plain search, only what the person could really take is
/// listed: a sale kept for somebody else, the person's own land and anything on a private island are left out. With "only available" off, land that is already rented is listed too (marked as rented).
/// </summary>
public static class LandMarket
{
    public const int MaxPageSize = 30;
    public static readonly string[] Categories = LandOverview.Categories;

    public static async Task<MarketPage> SearchAsync(LifeVerseDbContext db, Guid viewer, MarketQuery query, CancellationToken ct)
    {
        var offer = query.Offer is "sale" or "rent" ? query.Offer : "any";
        var category = query.Category is { } c && Categories.Contains(c) ? c : null;
        var rented = db.ParcelRentals.AsNoTracking().Where(r => r.Status == "active" || r.Status == "grace").Select(r => r.ParcelId);
        var parcels = db.IslandParcels.AsNoTracking().Where(p => p.DeletedAt == null);
        var islands = db.Islands.AsNoTracking().Where(i => i.DeletedAt == null && i.IsPublic);
        var onlyAvailable = query.OnlyAvailable;

        var joined = from p in parcels
                     join i in islands on p.IslandId equals i.Id
                     let isRented = rented.Contains(p.Id)
                     let saleOk = p.SalePrice != null && (p.SaleBuyerUserId == null || p.SaleBuyerUserId == viewer) && p.OwnerUserId != viewer
                     let rentOk = p.RentPrice != null && !isRented && p.OwnerUserId != viewer
                     let rentalPrice = db.ParcelRentals.AsNoTracking().Where(r => r.ParcelId == p.Id && (r.Status == "active" || r.Status == "grace")).Select(r => (long?)r.PricePerPeriod).FirstOrDefault()
                     let rentalDays = db.ParcelRentals.AsNoTracking().Where(r => r.ParcelId == p.Id && (r.Status == "active" || r.Status == "grace")).Select(r => (int?)r.PeriodDays).FirstOrDefault()
                     let rentShown = p.OwnerUserId != viewer && ((p.RentPrice != null && rentOk) || (!onlyAvailable && isRented))
                     let price = saleOk ? p.SalePrice : (isRented ? rentalPrice : p.RentPrice)
                     where (offer == "sale" && saleOk) || (offer == "rent" && rentShown) || (offer == "any" && (saleOk || rentShown))
                     select new { p, i.Name, i.CoverImageUrl, saleOk, rentOk, isRented, price, rentalDays };

        if (category is not null) joined = joined.Where(x => x.p.Category == category);
        if (query.WithVisit) joined = joined.Where(x => x.p.AccessMode == "public" && x.p.TeleportMode != "disabled");
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim().ToLowerInvariant();
            joined = joined.Where(x => x.p.Name.ToLower().Contains(q) || x.p.Description.ToLower().Contains(q) || x.Name.ToLower().Contains(q) || x.p.Code.ToLower().Contains(q));
        }
        if (query.MinArea is { } minArea) joined = joined.Where(x => x.p.Area >= minArea);
        if (query.MaxArea is { } maxArea) joined = joined.Where(x => x.p.Area <= maxArea);
        if (query.MinPrice is { } minPrice) joined = joined.Where(x => x.price >= minPrice);
        if (query.MaxPrice is { } maxPrice) joined = joined.Where(x => x.price <= maxPrice);

        var total = await joined.CountAsync(ct);
        var ordered = query.Sort switch
        {
            "price" => joined.OrderBy(x => x.saleOk ? x.p.SalePrice : x.p.RentPrice).ThenBy(x => x.p.Id),
            "price_desc" => joined.OrderByDescending(x => x.saleOk ? x.p.SalePrice : x.p.RentPrice).ThenBy(x => x.p.Id),
            "area" => joined.OrderByDescending(x => x.p.Area).ThenBy(x => x.p.Id),
            "prims" => joined.OrderByDescending(x => x.p.MaxObjects).ThenBy(x => x.p.Id),
            _ => joined.OrderByDescending(x => x.p.UpdatedAt).ThenBy(x => x.p.Id),
        };
        var size = Math.Clamp(query.PageSize, 1, MaxPageSize); var pages = Math.Max(1, (int)Math.Ceiling(total / (double)size)); var page = Math.Clamp(query.Page, 1, pages);
        var rows = await ordered.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var owners = rows.Select(r => r.p.OwnerUserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => owners.Contains(u.Id)).Select(u => new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        foreach (var avatar in await db.Avatars.AsNoTracking().Where(a => owners.Contains(a.UserId)).Select(a => new { a.UserId, a.DisplayName }).ToListAsync(ct)) names[avatar.UserId] = avatar.DisplayName;
        var items = rows.Select(r => new MarketItem(r.p.Id, r.p.IslandId, r.Name, r.CoverImageUrl, r.p.Code, r.p.Name, r.p.Description, r.p.Category, r.p.Area, r.p.MaxObjects,
            r.saleOk ? "sale" : "rent", r.saleOk ? "sale" : r.isRented ? "rented" : "rent", r.price ?? 0, r.saleOk ? 0 : (r.isRented ? r.rentalDays ?? 0 : r.p.RentPeriodDays), names.GetValueOrDefault(r.p.OwnerUserId, "—"),
            r.p.AccessMode == "public" && r.p.TeleportMode != "disabled", r.p.UpdatedAt, r.p.RentMinPeriods, r.p.RentMaxPeriods, r.p.RentDeposit)).ToList();
        return new MarketPage(total, page, size, items);
    }
}
