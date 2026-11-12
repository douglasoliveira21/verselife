using System.Text.Json;
using LifeVerse.Client.Scripts.IslandManagement;
using LifeVerse.Client.Scripts.LandManagement;

namespace LifeVerse.Client.Scripts.RealEstate;

public sealed record PropIsland(Guid Id, string Name, string Cover, string State, int Visitors, int MaxPlayers, int Objects, int MaxObjects, bool IsPublic, int Parcels, string Region = "full", long? SalePrice = null, long? RentPrice = null, int RentPeriodDays = 30, string Tenant = "", DateTimeOffset? LeaseExpiresAt = null, Guid? LeaseId = null);

public sealed record PropParcel(Guid Id, Guid IslandId, string Island, string Code, string Name, string Category, double Area, int MaxObjects, int Objects, string Kind, long? SalePrice, long? RentPrice, int RentPeriodDays, string Tenant, DateTimeOffset? RentExpiresAt, Guid? RentalId, bool CanVisit, string Image = "")
{
    public string Title => Code.Length > 0 ? $"{Code} — {Name}" : Name;
}

public sealed record PropContract(Guid Id, Guid ParcelId, Guid IslandId, string Island, string Code, string Parcel, string Landlord, string Tenant, string Role, long Price, int PeriodDays, string Status, DateTimeOffset StartsAt, DateTimeOffset ExpiresAt, DateTimeOffset? EndedAt, bool AutoRenew, long Deposit, bool CanVisit, string Target = "parcel")
{
    public string Title => Code.Length > 0 ? $"{Code} — {Parcel}" : Parcel;
    public bool Live => Status is "active" or "grace";
}

public sealed record PropListing(string Kind, Guid ParcelId, Guid IslandId, string Island, string Code, string Name, double Area, long Price, int PeriodDays, bool Exclusive, DateTimeOffset Since, string Target = "parcel")
{
    public string Title => Code.Length > 0 ? $"{Code} — {Name}" : Name;
}

public sealed record PropPayment(Guid Id, DateTimeOffset At, string Type, bool Incoming, long Amount, Guid ParcelId, string Island, string Code, string Parcel, string Other, string Target = "parcel")
{
    public string Title => Code.Length > 0 ? $"{Code} — {Parcel}" : Parcel;
}

public sealed record PropSummary(int Total, int InUse, int RentedOut, long MonthlyRevenue, int ExpiringSoon);

/// <summary>What <c>GET /properties/mine</c> says the person holds.</summary>
public sealed class PropertiesData
{
    public PropSummary Summary { get; init; } = new(0, 0, 0, 0, 0);
    public IReadOnlyList<PropIsland> Islands { get; init; } = [];
    public IReadOnlyList<PropParcel> Parcels { get; init; } = [];
    public IReadOnlyList<PropContract> Contracts { get; init; } = [];
    public IReadOnlyList<PropListing> Listings { get; init; } = [];
    public IReadOnlyList<PropPayment> Payments { get; init; } = [];

    private static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int Int(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var x) ? x : 0;
    private static long Long(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : 0;
    private static long? LongOrNull(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : null;
    private static double Dbl(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var x) ? x : 0;
    private static bool Flag(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    private static Guid G(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g) ? g : Guid.Empty;
    private static Guid? GOrNull(JsonElement e, string n) => G(e, n) is var g && g != Guid.Empty ? g : null;
    private static DateTimeOffset D(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var d) ? d : default;
    private static DateTimeOffset? DOrNull(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var d) ? d : null;
    private static List<T> All<T>(JsonElement root, string name, Func<JsonElement, T> read) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? [.. v.EnumerateArray().Select(read)] : [];

    public static PropertiesData? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("summary", out var s)) return null;
        try
        {
            return new PropertiesData
            {
                Summary = new PropSummary(Int(s, "total"), Int(s, "inUse"), Int(s, "rentedOut"), Long(s, "monthlyRevenue"), Int(s, "expiringSoon")),
                Islands = All(root, "islands", i => new PropIsland(G(i, "id"), Str(i, "name"), Str(i, "coverImageUrl"), Str(i, "state"), Int(i, "visitors"), Int(i, "maxPlayers"), Int(i, "objects"), Int(i, "maxObjects"), Flag(i, "isPublic"), Int(i, "parcels"), Str(i, "region") is { Length: > 0 } region ? region : "full", LongOrNull(i, "salePrice"), LongOrNull(i, "rentPrice"), Int(i, "rentPeriodDays"), Str(i, "tenant"), DOrNull(i, "leaseExpiresAt"), GOrNull(i, "leaseId"))),
                Parcels = All(root, "parcels", p => new PropParcel(G(p, "id"), G(p, "islandId"), Str(p, "island"), Str(p, "code"), Str(p, "name"), Str(p, "category"), Dbl(p, "area"), Int(p, "maxObjects"), Int(p, "objects"), Str(p, "kind"), LongOrNull(p, "salePrice"), LongOrNull(p, "rentPrice"), Int(p, "rentPeriodDays"), Str(p, "tenant"), DOrNull(p, "rentExpiresAt"), GOrNull(p, "rentalId"), Flag(p, "canVisit"), Str(p, "imageUrl"))),
                Contracts = All(root, "contracts", c => new PropContract(G(c, "id"), G(c, "parcelId"), G(c, "islandId"), Str(c, "island"), Str(c, "code"), Str(c, "parcel"), Str(c, "landlord"), Str(c, "tenant"), Str(c, "role"), Long(c, "price"), Int(c, "periodDays"), Str(c, "status"), D(c, "startsAt"), D(c, "expiresAt"), DOrNull(c, "endedAt"), Flag(c, "autoRenew"), Long(c, "deposit"), Flag(c, "canVisit"), Str(c, "target") is { Length: > 0 } ct ? ct : "parcel")),
                Listings = All(root, "listings", l => new PropListing(Str(l, "kind"), G(l, "parcelId"), G(l, "islandId"), Str(l, "island"), Str(l, "code"), Str(l, "name"), Dbl(l, "area"), Long(l, "price"), Int(l, "periodDays"), Flag(l, "exclusive"), D(l, "since"), Str(l, "target") is { Length: > 0 } lt ? lt : "parcel")),
                Payments = All(root, "payments", p => new PropPayment(G(p, "id"), D(p, "at"), Str(p, "type"), Str(p, "direction") == "in", Long(p, "amount"), G(p, "parcelId"), Str(p, "island"), Str(p, "code"), Str(p, "parcel"), Str(p, "other"), Str(p, "target") is { Length: > 0 } pt ? pt : "parcel")),
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException) { return null; }
    }

    public static string PaymentText(string type) => type switch { "sale" => "Compra e venda", "rent_start" => "Aluguel (início e caução)", "rent_payment" => "Pagamento de aluguel", "rent_end" => "Caução devolvida", _ => type.Replace('_', ' ') };
    public static string StatusText(string status) => status switch { "active" => "Ativo", "grace" => "Em tolerância", "ended" => "Encerrado", _ => status };
}

/// <summary>The calls of the "Minhas Propriedades" window.</summary>
public sealed class PropertiesApi(IslandManagementApi inner)
{
    private static string Land(Guid island) => $"/api/v1/worlds/islands/{island}/land";
    public Task<IslandReply> Mine() => inner.Send("GET", "/api/v1/properties/mine");
    public Task<IslandReply> Notices() => inner.Send("GET", "/api/v1/properties/notices?take=40");
    public Task<IslandReply> ReadNotices(IEnumerable<Guid>? ids) => inner.Send("POST", "/api/v1/properties/notices/read", new { ids });
    public Task<IslandReply> OfferSale(Guid island, Guid parcel, long price, bool includeObjects) => inner.Send("POST", $"{Land(island)}/parcels/{parcel}/sale", new { price, includeObjects, description = "" });
    public Task<IslandReply> CancelSale(Guid island, Guid parcel) => inner.Send("DELETE", $"{Land(island)}/parcels/{parcel}/sale");
    public Task<IslandReply> OfferRent(Guid island, Guid parcel, long price, int days, int min, int max, long deposit) => inner.Send("POST", $"{Land(island)}/parcels/{parcel}/rental-offer", new { pricePerPeriod = price, periodDays = days, minPeriods = min, maxPeriods = Math.Max(min, max), depositCoins = deposit, graceDays = 3, autoRenewAllowed = true });
    public Task<IslandReply> CancelRent(Guid island, Guid parcel) => inner.Send("DELETE", $"{Land(island)}/parcels/{parcel}/rental-offer");
    public Task<IslandReply> Renew(Guid island, Guid rental, int periods) => inner.Send("POST", $"{Land(island)}/rentals/{rental}/renew", new { periods, idempotencyKey = Guid.NewGuid().ToString() });
    public Task<IslandReply> End(Guid island, Guid rental) => inner.Send("POST", $"{Land(island)}/rentals/{rental}/end", new { });

    // the same, for a whole island
    private static string Estate(Guid island) => $"/api/v1/worlds/islands/{island}/real-estate";
    public Task<IslandReply> IslandOfferSale(Guid island, long price, bool includeObjects) => inner.Send("POST", $"{Estate(island)}/sale", new { price, includeObjects, description = "" });
    public Task<IslandReply> IslandCancelSale(Guid island) => inner.Send("DELETE", $"{Estate(island)}/sale");
    public Task<IslandReply> IslandOfferRent(Guid island, long price, int days, int min, int max, long deposit, bool autoRenewAllowed = false) => inner.Send("POST", $"{Estate(island)}/rental-offer", new { pricePerPeriod = price, periodDays = days, minPeriods = min, maxPeriods = Math.Max(min, max), depositCoins = deposit, graceDays = 3, autoRenewAllowed });
    public Task<IslandReply> IslandCancelRent(Guid island) => inner.Send("DELETE", $"{Estate(island)}/rental-offer");
    public Task<IslandReply> IslandRenew(Guid island, Guid lease, int periods) => inner.Send("POST", $"{Estate(island)}/leases/{lease}/renew", new { periods, idempotencyKey = Guid.NewGuid().ToString() });
    public Task<IslandReply> IslandEnd(Guid island, Guid lease) => inner.Send("POST", $"{Estate(island)}/leases/{lease}/end", new { });
}
