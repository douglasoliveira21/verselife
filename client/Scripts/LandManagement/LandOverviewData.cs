using System.Text.Json;

namespace LifeVerse.Client.Scripts.LandManagement;

/// <summary>One parcel of the island as the land management window shows it. Everything is what the server counted.</summary>
public sealed class LandParcelRow
{
    public Guid Id { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "residencial";
    /// <summary>available, sale, rented, sold or common (how the map colours it).</summary>
    public string Kind { get; init; } = "available";
    public double Area { get; init; }
    public int CellCount { get; init; }
    public int[][] Runs { get; init; } = [];
    public Guid OwnerId { get; init; }
    public string Owner { get; init; } = "";
    public bool OwnedByIslandOwner { get; init; }
    public long? SalePrice { get; init; }
    public long? RentPrice { get; init; }
    public int RentPeriodDays { get; init; }
    public Guid? TenantId { get; init; }
    public string Tenant { get; init; } = "";
    public string RentStatus { get; init; } = "";
    public DateTimeOffset? RentExpiresAt { get; init; }
    public Guid? RentalId { get; init; }
    public int MaxObjects { get; init; }
    public int Objects { get; init; }
    public int Cost { get; init; }
    public int MaxCost { get; init; }
    public bool Scripts { get; init; }
    public int Builders { get; init; }
    public string AccessMode { get; init; } = "public";
    public string BuildMode { get; init; } = "owner";
    public int Revision { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    private HashSet<long>? _cells;
    /// <summary>The 4 x 4 m cells of the parcel, as (cx, cz) keys.</summary>
    public HashSet<long> Cells => _cells ??= [.. Runs.SelectMany(run => Enumerable.Range(run[1], run[2] - run[1] + 1).Select(cx => LandMath.Key(cx, run[0])))];

    public string CategoryText => LandMath.CategoryText(Category);
    public string KindText => LandMath.KindText(Kind);
    public double UsePercent => MaxObjects <= 0 ? 0 : Math.Clamp(Objects * 100.0 / MaxObjects, 0, 100);
}

public sealed record LandContractRow(Guid Id, Guid ParcelId, string Code, string Parcel, string Landlord, string Tenant, long Price, int PeriodDays, string Status, DateTimeOffset StartsAt, DateTimeOffset ExpiresAt, DateTimeOffset? EndedAt, bool AutoRenew, long Deposit);

public sealed record LandHistoryRow(DateTimeOffset At, string Kind, string What, string Actor, Guid ParcelId, string Code, string Parcel, long? Amount);

public sealed class LandTotals
{
    public double Area { get; init; }
    public double AreaParcelled { get; init; }
    public double AreaFree { get; init; }
    public int Parcels { get; init; }
    public int Available { get; init; }
    public int OnSale { get; init; }
    public int Rented { get; init; }
    public int Sold { get; init; }
    public int Common { get; init; }
    public long MonthlyRevenue { get; init; }
    public int ExpiringSoon { get; init; }
    public int ObjectsLimit { get; init; }
    public int ObjectsUsed { get; init; }
    public int ObjectsShared { get; init; }
}

/// <summary>What <c>GET …/land/overview</c> says about the island's land.</summary>
public sealed class LandOverviewData
{
    public Guid IslandId { get; init; }
    public string IslandName { get; init; } = "";
    public int Size { get; init; } = 256;
    public string CoverImageUrl { get; init; } = "";
    public string RuntimeState { get; init; } = "offline";
    public bool IsPublic { get; init; }
    public string OwnerName { get; init; } = "";
    public Guid OwnerId { get; init; }
    public long Version { get; init; }
    public LandTotals Totals { get; init; } = new();
    public IReadOnlyList<string> Categories { get; init; } = [];
    public IReadOnlyList<LandParcelRow> Parcels { get; init; } = [];
    public IReadOnlyList<LandContractRow> Contracts { get; init; } = [];
    public IReadOnlyList<LandHistoryRow> History { get; init; } = [];

    private static string Str(JsonElement e, string name, string fallback = "") => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
    private static long Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
    private static long? LongOrNull(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
    private static double Dbl(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) ? n : 0;
    private static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static Guid GuidOf(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g) ? g : Guid.Empty;
    private static Guid? GuidOrNull(JsonElement e, string name) => GuidOf(e, name) is var g && g != Guid.Empty ? g : null;
    private static DateTimeOffset Date(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var d) ? d : default;
    private static DateTimeOffset? DateOrNull(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var d) ? d : null;

    public static LandOverviewData? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("island", out var island) || !root.TryGetProperty("totals", out var t)) return null;
        try
        {
            var parcels = root.TryGetProperty("parcels", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(ParseParcel).ToList() : [];
            var contracts = root.TryGetProperty("contracts", out var c) && c.ValueKind == JsonValueKind.Array
                ? c.EnumerateArray().Select(x => new LandContractRow(GuidOf(x, "id"), GuidOf(x, "parcelId"), Str(x, "code"), Str(x, "parcel"), Str(x, "landlord"), Str(x, "tenant"), Long(x, "price"), Int(x, "periodDays"), Str(x, "status"), Date(x, "startsAt"), Date(x, "expiresAt"), DateOrNull(x, "endedAt"), Flag(x, "autoRenew"), Long(x, "deposit"))).ToList() : [];
            var history = root.TryGetProperty("history", out var h) && h.ValueKind == JsonValueKind.Array
                ? h.EnumerateArray().Select(x => new LandHistoryRow(Date(x, "at"), Str(x, "kind"), Str(x, "what"), Str(x, "actor"), GuidOf(x, "parcelId"), Str(x, "code"), Str(x, "parcel"), LongOrNull(x, "amount"))).ToList() : [];
            return new LandOverviewData
            {
                IslandId = GuidOf(island, "id"), IslandName = Str(island, "name"), Size = Math.Max(16, Int(island, "size")), CoverImageUrl = Str(island, "coverImageUrl"), RuntimeState = Str(island, "runtimeState", "offline"), IsPublic = Flag(island, "isPublic"),
                OwnerName = Str(island, "owner"), OwnerId = GuidOf(island, "ownerId"), Version = Long(root, "version"),
                Totals = new LandTotals
                {
                    Area = Dbl(t, "area"), AreaParcelled = Dbl(t, "areaParcelled"), AreaFree = Dbl(t, "areaFree"), Parcels = Int(t, "parcels"), Available = Int(t, "available"), OnSale = Int(t, "onSale"), Rented = Int(t, "rented"), Sold = Int(t, "sold"), Common = Int(t, "common"),
                    MonthlyRevenue = Long(t, "monthlyRevenue"), ExpiringSoon = Int(t, "expiringSoon"), ObjectsLimit = Int(t, "objectsLimit"), ObjectsUsed = Int(t, "objectsUsed"), ObjectsShared = Int(t, "objectsShared"),
                },
                Categories = root.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array ? [.. cats.EnumerateArray().Select(x => x.GetString() ?? "")] : [],
                Parcels = parcels, Contracts = contracts, History = history,
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or KeyNotFoundException) { return null; }
    }

    private static LandParcelRow ParseParcel(JsonElement p)
    {
        var runs = p.TryGetProperty("runs", out var r) && r.ValueKind == JsonValueKind.Array ? r.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Array && x.GetArrayLength() == 3).Select(x => new[] { x[0].GetInt32(), x[1].GetInt32(), x[2].GetInt32() }).ToArray() : [];
        return new LandParcelRow
        {
            Id = GuidOf(p, "id"), Code = Str(p, "code"), Name = Str(p, "name"), Description = Str(p, "description"), Category = Str(p, "category", "residencial"), Kind = Str(p, "kind", "available"), Area = Dbl(p, "area"), CellCount = Int(p, "cells"), Runs = runs,
            OwnerId = GuidOf(p, "ownerId"), Owner = Str(p, "owner"), OwnedByIslandOwner = Flag(p, "ownedByIslandOwner"), SalePrice = LongOrNull(p, "salePrice"), RentPrice = LongOrNull(p, "rentPrice"), RentPeriodDays = Int(p, "rentPeriodDays"),
            TenantId = GuidOrNull(p, "tenantId"), Tenant = Str(p, "tenant"), RentStatus = Str(p, "rentStatus"), RentExpiresAt = DateOrNull(p, "rentExpiresAt"), RentalId = GuidOrNull(p, "rentalId"),
            MaxObjects = Int(p, "maxObjects"), Objects = Int(p, "objects"), Cost = Int(p, "cost"), MaxCost = Int(p, "maxCost"), Scripts = Flag(p, "scripts"), Builders = Int(p, "builders"), AccessMode = Str(p, "accessMode", "public"), BuildMode = Str(p, "buildMode", "owner"), Revision = Int(p, "revision"), CreatedAt = Date(p, "createdAt"),
        };
    }
}

/// <summary>The small rules the window shares: cell keys (the same packing the server uses), the words for states and categories, and money and area text.</summary>
public static class LandMath
{
    public const int CellSize = 4;

    public static long Key(int cx, int cz) => ((long)cx << 32) | (uint)cz;
    public static (int Cx, int Cz) Unkey(long key) => ((int)(key >> 32), (int)(key & 0xFFFFFFFF));

    public static string CategoryText(string category) => category switch { "residencial" => "Residencial", "comercial" => "Comercial", "mista" => "Mista", "social" => "Social", "comum" => "Área Comum", _ => category };
    public static string KindText(string kind) => kind switch { "available" => "Disponível", "sale" => "À Venda", "rented" => "Alugado", "sold" => "Vendido", "common" => "Área Comum", _ => kind };
    public static string Money(long value) => "L$ " + value.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
    public static string Area(double value) => value.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + " m²";
    public static string Number(long value) => value.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    public static string WhatText(string what) => what switch
    {
        "claim" => "Parcela criada", "split" or "split_from" => "Parcela dividida", "merge" or "merge_into" => "Parcelas unidas", "category" => "Finalidade alterada", "sale_listed" or "list_sale" => "Colocada à venda", "sale_cancelled" or "cancel_sale" => "Venda retirada",
        "rent_listed" or "list_rent" => "Colocada para alugar", "rent_cancelled" or "cancel_rent_offer" => "Aluguel retirado", "sale" or "sold" => "Venda", "rent_start" or "rented" => "Aluguel iniciado", "rent_payment" => "Pagamento de aluguel", "rent_end" => "Aluguel encerrado",
        "land_transfer" => "Titularidade transferida", "land_deposit_refund" => "Caução devolvida", "access_mode" or "access" => "Acesso alterado", "environment" => "Ambiente alterado", "media" => "Música alterada", "arrival" => "Ponto de chegada alterado",
        "options" => "Regras alteradas", "update" => "Dados alterados", "eject" => "Pessoa retirada", "auto_return" => "Devolução automática de objetos", "return" or "return_objects" => "Objetos devolvidos", _ => what.Replace('_', ' '),
    };
}
