using System.Text.Json;
using LifeVerse.Shared.Search;

namespace LifeVerse.Client.Scripts;

/// <summary>One thing found by the global search, as the server describes it.</summary>
public sealed record SearchHit(string Kind, string KindLabel, Guid Id, string Title, string Subtitle, string Description, string Category, string? ThumbnailUrl, Guid? IslandId, float? X, float? Z, double Popularity, bool Favorite)
{
    /// <summary>Whether there is a place to go to: an island, and for most kinds a spot in it.</summary>
    public bool HasPlace => IslandId is not null && Kind != SearchKinds.Person && Kind != SearchKinds.Group && Kind != SearchKinds.Product;
}

public sealed record SearchPageData(int Total, int Page, int PageSize, IReadOnlyList<SearchHit> Items, IReadOnlyDictionary<string, int> Counts, IReadOnlyDictionary<string, int> Categories)
{
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)Math.Max(1, PageSize)));
    public static SearchPageData Empty { get; } = new(0, 1, 20, [], new Dictionary<string, int>(), new Dictionary<string, int>());
}

/// <summary>What is sent to the search API and how its answers are read. Pure code (no nodes), so it can be tested alone.</summary>
public static class SearchApi
{
    public static readonly string[] SortNames = ["relevance", "popularity", "updated"];
    public static readonly string[] SortLabels = ["Relevância", "Popularidade", "Atualização"];

    public static string SearchPath(string query, string? kind, string? category, string sort, int page, int pageSize = 20)
    {
        var parts = new List<string> { "q=" + Uri.EscapeDataString(query.Trim()), "page=" + Math.Max(1, page), "pageSize=" + Math.Clamp(pageSize, 1, 50), "sort=" + Uri.EscapeDataString(sort) };
        if (!string.IsNullOrEmpty(kind)) parts.Add("types=" + Uri.EscapeDataString(kind));
        if (!string.IsNullOrWhiteSpace(category)) parts.Add("category=" + Uri.EscapeDataString(category.Trim()));
        return "/api/v1/search?" + string.Join("&", parts);
    }

    public static string SuggestPath(string typed) => "/api/v1/search/suggest?take=6&q=" + Uri.EscapeDataString(typed.Trim());
    public static string FavoritesPath(string? kind, int page) => $"/api/v1/search/favorites?page={Math.Max(1, page)}&pageSize=20" + (string.IsNullOrEmpty(kind) ? string.Empty : "&types=" + Uri.EscapeDataString(kind));
    public static string FavoritePath(string kind, Guid id) => $"/api/v1/search/favorites/{kind}/{id}";
    public static string ReportPath(string kind, Guid id) => $"/api/v1/search/report/{kind}/{id}";
    public const string PopularPath = "/api/v1/search/popular?take=12", RecentPath = "/api/v1/search/recent?take=12", RecommendedPath = "/api/v1/search/recommended?take=20", InterestsPath = "/api/v1/search/interests", EventsPath = "/api/v1/search/events";

    public static string EventBody(string type, string query) => JsonSerializer.Serialize(new { type, query });
    public static string ReportBody(string reason) => JsonSerializer.Serialize(new { reason });
    public static string InterestsBody(IEnumerable<string> interests) => JsonSerializer.Serialize(new { interests });

    /// <summary>A page of results ({ total, page, items, counts, categories }); anything unreadable is an empty page.</summary>
    public static SearchPageData ParsePage(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json); var r = d.RootElement; if (r.ValueKind != JsonValueKind.Object) return SearchPageData.Empty;
            return new SearchPageData(Int(r, "total"), Math.Max(1, Int(r, "page")), Math.Max(1, Int(r, "pageSize")), Items(r.TryGetProperty("items", out var items) ? items : default), Counts(r, "counts"), Counts(r, "categories"));
        }
        catch (JsonException) { return SearchPageData.Empty; }
    }

    /// <summary>A plain list of results (popular, recent, recommended).</summary>
    public static IReadOnlyList<SearchHit> ParseList(string json)
    {
        try { using var d = JsonDocument.Parse(json); return Items(d.RootElement); } catch (JsonException) { return []; }
    }

    public static IReadOnlyList<string> ParseSuggestions(string json)
    {
        try { using var d = JsonDocument.Parse(json); return d.RootElement.ValueKind != JsonValueKind.Array ? [] : [.. d.RootElement.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? string.Empty : string.Empty).Where(t => t.Length > 0).Distinct()]; }
        catch (JsonException) { return []; }
    }

    public static IReadOnlyList<string> ParseStrings(string json)
    {
        try { using var d = JsonDocument.Parse(json); return d.RootElement.ValueKind != JsonValueKind.Array ? [] : [.. d.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? string.Empty).Where(t => t.Length > 0)]; }
        catch (JsonException) { return []; }
    }

    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
    private static float? Float(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var f) && float.IsFinite(f) ? f : null;

    private static IReadOnlyDictionary<string, int> Counts(JsonElement root, string name)
    {
        var map = new Dictionary<string, int>();
        if (root.TryGetProperty(name, out var c) && c.ValueKind == JsonValueKind.Object) foreach (var p in c.EnumerateObject()) if (p.Value.TryGetInt32(out var n)) map[p.Name] = n;
        return map;
    }

    private static List<SearchHit> Items(JsonElement array)
    {
        var list = new List<SearchHit>(); if (array.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in array.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object || !Guid.TryParse(Str(e, "id"), out var id) || Str(e, "kind") is not { Length: > 0 } kind) continue;
            Guid? island = Guid.TryParse(Str(e, "islandId"), out var i) ? i : null; var thumb = Str(e, "thumbnailUrl");
            list.Add(new SearchHit(kind, Str(e, "kindLabel") is { Length: > 0 } label ? label : SearchKinds.Label(kind), id, Str(e, "title"), Str(e, "subtitle"), Str(e, "description"), Str(e, "category"), thumb.Length == 0 ? null : thumb, island, Float(e, "x"), Float(e, "z"),
                e.TryGetProperty("popularity", out var p) && p.TryGetDouble(out var pop) ? pop : 0, e.TryGetProperty("favorite", out var f) && f.ValueKind == JsonValueKind.True));
        }
        return list;
    }

    /// <summary>An address a thumbnail can be loaded from: only http(s), with no login in it.</summary>
    public static bool IsLoadableImageUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) && u.UserInfo.Length == 0;
}

/// <summary>
/// Going to a search result in another island is two steps: join the island, then, once there, move to the spot of the result. This holds the second step
/// between the two (the island server is a different process, so the client has to remember it across the change).
/// </summary>
public static class SearchTravel
{
    private static (Guid Island, float X, float Z)? _pending;

    public static void Set(Guid island, float x, float z) { _parcel = null; _pending = (island, x, z); }
    public static void Clear() { _pending = null; _parcel = null; }
    public static bool HasPending => _pending is not null || _parcel is not null;

    private static (Guid Island, Guid Parcel)? _parcel;

    /// <summary>The same two steps, but the place is a parcel: once on the island, ask it to take the avatar to that parcel.</summary>
    public static void SetParcel(Guid island, Guid parcel) { _pending = null; _parcel = (island, parcel); }

    /// <summary>The parcel to go to if the person has just arrived at that island; asking for another island forgets it.</summary>
    public static Guid? TakeParcelFor(Guid island)
    {
        if (_parcel is not { } p) return null;
        _parcel = null;
        return p.Island == island ? p.Parcel : null;
    }

    /// <summary>The spot to go to if the person has just arrived at that island; asking for another island forgets it (they went somewhere else).</summary>
    public static (float X, float Z)? TakeFor(Guid island)
    {
        if (_pending is not { } p) return null;
        _pending = null;
        return p.Island == island ? (p.X, p.Z) : null;
    }
}
