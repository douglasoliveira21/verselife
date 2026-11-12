using System.Text.Json;
using LifeVerse.Client.Scripts.IslandManagement;
using LifeVerse.Client.Scripts.LandManagement;

namespace LifeVerse.Client.Scripts.RealEstate;

/// <summary>One listing of the real estate market, as the server sent it.</summary>
public sealed class MarketListing
{
    public Guid ParcelId { get; init; }
    public Guid IslandId { get; init; }
    public string IslandName { get; init; } = "";
    public string IslandCover { get; init; } = "";
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "residencial";
    public double Area { get; init; }
    public int MaxObjects { get; init; }
    /// <summary>sale or rent: what is being offered.</summary>
    public string Offer { get; init; } = "sale";
    /// <summary>sale, rent or rented: what the badge says.</summary>
    public string State { get; init; } = "sale";
    public long Price { get; init; }
    public int RentPeriodDays { get; init; }
    public string OwnerName { get; init; } = "";
    public bool CanVisit { get; init; }
    public int RentMinPeriods { get; init; } = 1;
    public int RentMaxPeriods { get; init; } = 1;
    public long RentDeposit { get; init; }

    public string Title => Code.Length > 0 ? $"{Code} — {Name}" : Name;
    public string TypeText => $"Terreno {LandMath.CategoryText(Category)}";
    public string PriceText => Offer == "sale" ? LandMath.Money(Price) : $"{LandMath.Money(Price)} / {(RentPeriodDays == 30 ? "mensal" : RentPeriodDays == 7 ? "semanal" : $"{RentPeriodDays} dias")}";
}

public sealed class MarketPageData
{
    public int Total { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 9;
    public IReadOnlyList<MarketListing> Items { get; init; } = [];
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)Math.Max(1, PageSize)));

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static long Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
    private static double Dbl(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) ? n : 0;

    public static MarketPageData? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;
        var list = new List<MarketListing>();
        foreach (var item in items.EnumerateArray())
        {
            if (!Guid.TryParse(Str(item, "parcelId"), out var parcel) || !Guid.TryParse(Str(item, "islandId"), out var island)) continue;
            list.Add(new MarketListing
            {
                ParcelId = parcel, IslandId = island, IslandName = Str(item, "islandName"), IslandCover = Str(item, "islandCover"), Code = Str(item, "code"), Name = Str(item, "name"), Description = Str(item, "description"), Category = Str(item, "category"),
                Area = Dbl(item, "area"), MaxObjects = (int)Long(item, "maxObjects"), Offer = Str(item, "offer"), State = Str(item, "state"), Price = Long(item, "price"), RentPeriodDays = (int)Long(item, "rentPeriodDays"), OwnerName = Str(item, "ownerName"),
                CanVisit = item.TryGetProperty("canVisit", out var v) && v.ValueKind == JsonValueKind.True, RentMinPeriods = Math.Max(1, (int)Long(item, "rentMinPeriods")), RentMaxPeriods = Math.Max(1, (int)Long(item, "rentMaxPeriods")), RentDeposit = Long(item, "rentDeposit"),
            });
        }
        return new MarketPageData { Total = (int)Long(root, "total"), Page = Math.Max(1, (int)Long(root, "page")), PageSize = Math.Max(1, (int)Long(root, "pageSize")), Items = list };
    }
}

/// <summary>What the filters of the market window say, turned into the query the server understands.</summary>
public sealed record MarketFilters(string Query = "", string Offer = "any", string Category = "", long? MinPrice = null, long? MaxPrice = null, double? MinArea = null, double? MaxArea = null, bool OnlyAvailable = true, bool WithVisit = false, string Sort = "new", int Page = 1, int PageSize = 9)
{
    public string ToQuery()
    {
        var parts = new List<string> { $"offer={Offer}", $"sort={Sort}", $"page={Page}", $"pageSize={PageSize}", $"onlyAvailable={(OnlyAvailable ? "true" : "false")}", $"withVisit={(WithVisit ? "true" : "false")}" };
        if (Query.Trim().Length > 0) parts.Add("q=" + Uri.EscapeDataString(Query.Trim()));
        if (Category.Length > 0) parts.Add("category=" + Category);
        if (MinPrice is { } a) parts.Add($"minPrice={a}"); if (MaxPrice is { } b) parts.Add($"maxPrice={b}");
        if (MinArea is { } c) parts.Add("minArea=" + c.ToString(System.Globalization.CultureInfo.InvariantCulture)); if (MaxArea is { } d) parts.Add("maxArea=" + d.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return string.Join("&", parts);
    }
}

/// <summary>The calls of the market window, through the same transport as the other management windows.</summary>
public sealed class MarketApi(IslandManagementApi inner)
{
    public Task<IslandReply> Search(MarketFilters filters) => inner.Send("GET", $"/api/v1/discovery/market?{filters.ToQuery()}");
    public Task<IslandReply> Buy(MarketListing item) => inner.Send("POST", $"/api/v1/worlds/islands/{item.IslandId}/land/parcels/{item.ParcelId}/buy", new { expectedPrice = item.Price, idempotencyKey = Guid.NewGuid().ToString() });
    public Task<IslandReply> Rent(MarketListing item, int periods) => inner.Send("POST", $"/api/v1/worlds/islands/{item.IslandId}/land/parcels/{item.ParcelId}/rent", new { periods, autoRenew = false, expectedPrice = item.Price, idempotencyKey = Guid.NewGuid().ToString() });
}
