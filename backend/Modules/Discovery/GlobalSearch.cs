using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Shared.Search;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Discovery;

public sealed record SearchRequest(string? Q, string[]? Types = null, int Page = 1, int PageSize = 20);
public sealed record SearchResultItem(string Kind, string KindLabel, Guid Id, string Title, string Subtitle, string Description, string Category, string? ThumbnailUrl, Guid? IslandId, float? X, float? Z, double Score, double Popularity, DateTimeOffset UpdatedAt, DateTimeOffset? StartsAt);
public sealed record SearchPage(string Query, int Total, int Page, int PageSize, IReadOnlyList<SearchResultItem> Items, IReadOnlyDictionary<string, int> Counts);
public sealed record Suggestion(string Text, string Kind, Guid Id);

/// <summary>
/// The global search. Candidates come from the index (PostgreSQL full-text search with a GIN index when the database is PostgreSQL, a plain scan of the same
/// normalized text otherwise); what is found is then checked against the real data and against who is asking (public only, not deleted, nothing from people
/// who blocked you or whom you blocked), and only then ordered by relevance and cut into a page. The order is computed in C#, so it is the same on any database.
/// </summary>
public static class GlobalSearch
{
    public const int CandidateLimit = 400, MaxPageSize = 50, MinQueryLength = 2;

    /// <summary>The kinds asked for; names that mean nothing are ignored, and when none is left the search is over everything.</summary>
    private static string[] Kinds(string[]? types) => types is { Length: > 0 } && SearchKinds.All.Where(k => types.Contains(k, StringComparer.OrdinalIgnoreCase)).ToArray() is { Length: > 0 } wanted ? wanted : SearchKinds.All;

    private static async Task<List<SearchEntry>> CandidatesAsync(LifeVerseDbContext db, IReadOnlyList<string> terms, string[] kinds, int limit, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
        {
            var query = SearchText.TsQuery(terms); if (query.Length == 0) return [];
            return await db.SearchEntries.FromSqlInterpolated($@"SELECT * FROM discovery.search_index WHERE NOT hidden AND kind = ANY({kinds}) AND search_vector @@ to_tsquery('simple', {query})
                ORDER BY ts_rank(search_vector, to_tsquery('simple', {query})) DESC, popularity DESC LIMIT {limit}").AsNoTracking().ToListAsync(ct);
        }
        var all = await db.SearchEntries.AsNoTracking().Where(e => !e.Hidden && kinds.Contains(e.Kind)).ToListAsync(ct);
        return [.. all.Where(e => SearchText.MatchesAll(e.SearchText, terms)).OrderByDescending(e => e.Popularity).ThenBy(e => e.Id).Take(limit)];
    }

    /// <summary>The people whose things the viewer must not see: they blocked the viewer, or the viewer blocked them.</summary>
    private static async Task<HashSet<Guid>> BlockedAsync(LifeVerseDbContext db, Guid viewer, CancellationToken ct)
    {
        var ids = await db.UserBlocks.AsNoTracking().Where(b => b.UserId == viewer).Select(b => b.BlockedUserId).Union(db.UserBlocks.AsNoTracking().Where(b => b.BlockedUserId == viewer).Select(b => b.UserId)).ToListAsync(ct);
        return [.. ids];
    }

    /// <summary>
    /// The lines the viewer may be shown. The index is a copy and can be a little behind, so each line is checked against the real thing: public, alive,
    /// on a public island, and not belonging to somebody the viewer is blocked with. Anything that fails is simply not there.
    /// </summary>
    public static async Task<List<SearchEntry>> AllowedAsync(LifeVerseDbContext db, Guid viewer, IReadOnlyList<SearchEntry> found, DateTimeOffset now, CancellationToken ct)
    {
        var blocked = await BlockedAsync(db, viewer, ct); var ok = new HashSet<Guid>();
        List<Guid> Ids(string kind) => [.. found.Where(e => e.Kind == kind).Select(e => e.RefId)];
        async Task Keep(string kind, IQueryable<Guid> live) { var ids = (await live.ToListAsync(ct)).ToHashSet(); foreach (var e in found.Where(e => e.Kind == kind && ids.Contains(e.RefId))) ok.Add(e.Id); }
        var open = db.Islands.AsNoTracking().Where(i => i.IsPublic && i.DeletedAt == null).Select(i => i.Id);

        var islands = Ids(SearchKinds.Island); if (islands.Count > 0) await Keep(SearchKinds.Island, db.Islands.AsNoTracking().Where(i => islands.Contains(i.Id) && i.IsPublic && i.DeletedAt == null).Select(i => i.Id));
        var parcels = Ids(SearchKinds.Parcel); if (parcels.Count > 0) await Keep(SearchKinds.Parcel, db.IslandParcels.AsNoTracking().Where(p => parcels.Contains(p.Id) && p.DeletedAt == null && p.AccessMode == "public" && open.Contains(p.IslandId)).Select(p => p.Id));
        var people = Ids(SearchKinds.Person); if (people.Count > 0) await Keep(SearchKinds.Person, db.Users.AsNoTracking().Where(u => people.Contains(u.Id) && u.Id != viewer && u.DeletedAt == null && u.Status == "active" && u.PasswordHash != "!" && !u.PrivacyJson.Contains("\"profile\":\"private\"") && !u.PrivacyJson.Contains("\"searchable\":\"no\"")).Select(u => u.Id));
        var groups = Ids(SearchKinds.Group); if (groups.Count > 0) await Keep(SearchKinds.Group, db.SocialGroups.AsNoTracking().Where(g => groups.Contains(g.Id) && g.DeletedAt == null && g.Privacy == "public").Select(g => g.Id));
        var stores = Ids(SearchKinds.Store); if (stores.Count > 0) await Keep(SearchKinds.Store, db.WorldStores.AsNoTracking().Where(s => stores.Contains(s.Id) && s.DeletedAt == null && open.Contains(s.IslandId) && db.MarketplaceProducts.Any(p => p.Id == s.ProductId && p.Status == "active")).Select(s => s.Id));
        var products = Ids(SearchKinds.Product); if (products.Count > 0) await Keep(SearchKinds.Product, db.MarketplaceProducts.AsNoTracking().Where(p => products.Contains(p.Id) && p.Status == "active").Select(p => p.Id));
        var events = Ids(SearchKinds.Event); if (events.Count > 0) await Keep(SearchKinds.Event, db.DiscoveryEvents.AsNoTracking().Where(e => events.Contains(e.Id) && e.IsPublic && e.GroupId == null && e.EndsAt >= now && open.Contains(e.IslandId)).Select(e => e.Id));
        var places = Ids(SearchKinds.Place); if (places.Count > 0) await Keep(SearchKinds.Place, db.Landmarks.AsNoTracking().Where(l => places.Contains(l.Id) && l.IsPublic && l.DeletedAt == null && open.Contains(l.IslandId)).Select(l => l.Id));

        return [.. found.Where(e => ok.Contains(e.Id) && !e.Hidden && (e.OwnerUserId is not { } owner || !blocked.Contains(owner)))];
    }

    private static SearchResultItem Item(SearchEntry e, double score) =>
        new(e.Kind, SearchKinds.Label(e.Kind), e.RefId, e.Title, e.Subtitle, e.Description, e.Category, e.ThumbnailUrl, e.IslandId, e.X, e.Z, score, e.Popularity, e.SourceUpdatedAt, e.StartsAt);

    /// <summary>Looks for what the text asks, among the kinds wanted (all by default), most relevant first.</summary>
    public static async Task<SearchPage> SearchAsync(LifeVerseDbContext db, Guid viewer, SearchRequest request, DateTimeOffset now, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page); var size = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, MaxPageSize);
        var text = (request.Q ?? string.Empty).Trim(); var terms = SearchText.Terms(text);
        if (terms.Length == 0 || SearchText.Normalize(text).Length < MinQueryLength) return new SearchPage(text, 0, page, size, [], new Dictionary<string, int>());
        var found = await AllowedAsync(db, viewer, await CandidatesAsync(db, terms, Kinds(request.Types), CandidateLimit, ct), now, ct);
        var ranked = found.Select(e => (Entry: e, Score: SearchText.Relevance(e.Title, e.SearchText, text, terms, e.Popularity, e.SourceUpdatedAt, now)))
            .OrderByDescending(r => r.Score).ThenByDescending(r => r.Entry.Popularity).ThenBy(r => r.Entry.Title, StringComparer.Ordinal).ThenBy(r => r.Entry.Id).ToList();
        var counts = ranked.GroupBy(r => r.Entry.Kind).ToDictionary(g => g.Key, g => g.Count());
        return new SearchPage(text, ranked.Count, page, size, [.. ranked.Skip((page - 1) * size).Take(size).Select(r => Item(r.Entry, r.Score))], counts);
    }

    /// <summary>What to offer while the person types: titles that have the words they wrote (the last one as the beginning of a word), the most relevant and popular first.</summary>
    public static async Task<IReadOnlyList<Suggestion>> SuggestAsync(LifeVerseDbContext db, Guid viewer, string? text, int take, DateTimeOffset now, CancellationToken ct)
    {
        var typed = (text ?? string.Empty).Trim(); var terms = SearchText.Terms(typed);
        if (terms.Length == 0 || SearchText.Normalize(typed).Length < MinQueryLength) return [];
        var found = await AllowedAsync(db, viewer, await CandidatesAsync(db, terms, SearchKinds.All, 60, ct), now, ct);
        return [.. found.Where(e => SearchText.MatchesAll(SearchText.Normalize(e.Title), terms))
            .Select(e => (Entry: e, Score: SearchText.Relevance(e.Title, e.SearchText, typed, terms, e.Popularity, e.SourceUpdatedAt, now)))
            .OrderByDescending(r => r.Score).ThenBy(r => r.Entry.Title, StringComparer.Ordinal)
            .GroupBy(r => SearchText.Normalize(r.Entry.Title)).Select(g => g.First()).Take(Math.Clamp(take, 1, 20))
            .Select(r => new Suggestion(r.Entry.Title, r.Entry.Kind, r.Entry.RefId))];
    }

    private static async Task<List<SearchResultItem>> ListPlacesAsync(LifeVerseDbContext db, Guid viewer, IQueryable<SearchEntry> ordered, int take, DateTimeOffset now, CancellationToken ct)
    {
        var size = Math.Clamp(take, 1, MaxPageSize);
        var found = await AllowedAsync(db, viewer, await ordered.Take(size * 3).ToListAsync(ct), now, ct);
        return [.. found.Take(size).Select(e => Item(e, 0))];
    }

    /// <summary>The places most people go to: islands (and points of interest) by how popular they are.</summary>
    public static Task<List<SearchResultItem>> PopularPlacesAsync(LifeVerseDbContext db, Guid viewer, int take, DateTimeOffset now, CancellationToken ct) =>
        ListPlacesAsync(db, viewer, db.SearchEntries.AsNoTracking().Where(e => !e.Hidden && (e.Kind == SearchKinds.Island || e.Kind == SearchKinds.Place)).OrderByDescending(e => e.Popularity).ThenByDescending(e => e.SourceUpdatedAt).ThenBy(e => e.Id), take, now, ct);

    /// <summary>The places that appeared or changed most recently.</summary>
    public static Task<List<SearchResultItem>> RecentPlacesAsync(LifeVerseDbContext db, Guid viewer, int take, DateTimeOffset now, CancellationToken ct) =>
        ListPlacesAsync(db, viewer, db.SearchEntries.AsNoTracking().Where(e => !e.Hidden && (e.Kind == SearchKinds.Island || e.Kind == SearchKinds.Parcel || e.Kind == SearchKinds.Place)).OrderByDescending(e => e.SourceUpdatedAt).ThenBy(e => e.Id), take, now, ct);
}

public static class GlobalSearchEndpoints
{
    public static IEndpointRouteBuilder MapGlobalSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/search").WithTags("Search").RequireAuthorization();
        group.MapGet("", async (string? q, string? types, int? page, int? pageSize, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            Who(c, out var me) ? Results.Ok(await GlobalSearch.SearchAsync(db, me, new SearchRequest(q, types?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), page ?? 1, pageSize ?? 20), t.GetUtcNow(), ct)) : Results.Unauthorized());
        group.MapGet("/suggest", async (string? q, int? take, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            Who(c, out var me) ? Results.Ok(await GlobalSearch.SuggestAsync(db, me, q, take ?? 8, t.GetUtcNow(), ct)) : Results.Unauthorized());
        group.MapGet("/popular", async (int? take, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            Who(c, out var me) ? Results.Ok(await GlobalSearch.PopularPlacesAsync(db, me, take ?? 12, t.GetUtcNow(), ct)) : Results.Unauthorized());
        group.MapGet("/recent", async (int? take, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            Who(c, out var me) ? Results.Ok(await GlobalSearch.RecentPlacesAsync(db, me, take ?? 12, t.GetUtcNow(), ct)) : Results.Unauthorized());
        // the operator asks for the whole index to be built again (after changing what is indexed, or to repair it)
        endpoints.MapPost("/api/v1/platform/search/reindex", async (HttpContext c, IConfiguration config, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            !Modules.Platform.PlatformEndpoints.HasValidServiceKey(c, config) ? Results.Unauthorized() : Results.Ok(await SearchIndexer.RebuildAsync(db, t.GetUtcNow(), ct))).WithTags("Platform");
        return endpoints;
    }

    private static bool Who(HttpContext context, out Guid user) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out user);
}
