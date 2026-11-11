using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Shared.Search;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Discovery;

public sealed record SearchRequest(string? Q, string[]? Types = null, int Page = 1, int PageSize = 20, string? Category = null, Guid? IslandId = null, string? Sort = null);
public sealed record SearchResultItem(string Kind, string KindLabel, Guid Id, string Title, string Subtitle, string Description, string Category, string? ThumbnailUrl, Guid? IslandId, float? X, float? Z, double Score, double Popularity, DateTimeOffset UpdatedAt, DateTimeOffset? StartsAt, bool Favorite = false);
public sealed record SearchPage(string Query, int Total, int Page, int PageSize, IReadOnlyList<SearchResultItem> Items, IReadOnlyDictionary<string, int> Counts, IReadOnlyDictionary<string, int>? Categories = null, string Sort = GlobalSearch.SortRelevance);
public sealed record InterestsRequest(string?[]? Interests);
public sealed record Suggestion(string Text, string Kind, Guid Id);

/// <summary>
/// The global search. Candidates come from the index (PostgreSQL full-text search with a GIN index when the database is PostgreSQL, a plain scan of the same
/// normalized text otherwise); what is found is then checked against the real data and against who is asking (public only, not deleted, nothing from people
/// who blocked you or whom you blocked), and only then ordered by relevance and cut into a page. The order is computed in C#, so it is the same on any database.
/// </summary>
public static class GlobalSearch
{
    public const int CandidateLimit = 400, MaxPageSize = 50, MinQueryLength = 2, MaxFavorites = 500, MaxInterests = 20;
    public const string SortRelevance = "relevance", SortPopularity = "popularity", SortUpdated = "updated";

    /// <summary>The order asked for; anything that is not known means by relevance.</summary>
    public static string NormalizeSort(string? sort) => sort?.Trim().ToLowerInvariant() switch { SortPopularity or "popular" => SortPopularity, SortUpdated or "recent" => SortUpdated, _ => SortRelevance };

    /// <summary>The kinds asked for; names that mean nothing are ignored, and when none is left the search is over everything.</summary>
    private static string[] Kinds(string[]? types) => types is { Length: > 0 } && SearchKinds.All.Where(k => types.Contains(k, StringComparer.OrdinalIgnoreCase)).ToArray() is { Length: > 0 } wanted ? wanted : SearchKinds.All;

    private static async Task<List<SearchEntry>> CandidatesAsync(LifeVerseDbContext db, IReadOnlyList<string> terms, string[] kinds, int limit, CancellationToken ct, string? category = null, Guid? island = null)
    {
        if (db.Database.IsNpgsql())
        {
            var query = SearchText.TsQuery(terms); if (query.Length == 0) return [];
            return await db.SearchEntries.FromSqlInterpolated($@"SELECT * FROM discovery.search_index WHERE NOT hidden AND kind = ANY({kinds}) AND search_vector @@ to_tsquery('simple', {query})
                AND ({category}::text IS NULL OR lower(category) = lower({category})) AND ({island}::uuid IS NULL OR island_id = {island})
                ORDER BY ts_rank(search_vector, to_tsquery('simple', {query})) DESC, popularity DESC LIMIT {limit}").AsNoTracking().ToListAsync(ct);
        }
        var all = await db.SearchEntries.AsNoTracking().Where(e => !e.Hidden && kinds.Contains(e.Kind) && (island == null || e.IslandId == island)).ToListAsync(ct);
        return [.. all.Where(e => (category is null || string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase)) && SearchText.MatchesAll(e.SearchText, terms)).OrderByDescending(e => e.Popularity).ThenBy(e => e.Id).Take(limit)];
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
        var open = db.Islands.AsNoTracking().Where(i => i.IsPublic && i.ShowInSearch && i.DeletedAt == null).Select(i => i.Id);

        var islands = Ids(SearchKinds.Island); if (islands.Count > 0) await Keep(SearchKinds.Island, db.Islands.AsNoTracking().Where(i => islands.Contains(i.Id) && i.IsPublic && i.ShowInSearch && i.DeletedAt == null).Select(i => i.Id));
        var parcels = Ids(SearchKinds.Parcel); if (parcels.Count > 0) await Keep(SearchKinds.Parcel, db.IslandParcels.AsNoTracking().Where(p => parcels.Contains(p.Id) && p.DeletedAt == null && p.AccessMode == "public" && open.Contains(p.IslandId)).Select(p => p.Id));
        var people = Ids(SearchKinds.Person); if (people.Count > 0) await Keep(SearchKinds.Person, db.Users.AsNoTracking().Where(u => people.Contains(u.Id) && u.Id != viewer && u.DeletedAt == null && u.Status == "active" && u.PasswordHash != "!" && !u.PrivacyJson.Contains("\"profile\":\"private\"") && !u.PrivacyJson.Contains("\"searchable\":\"no\"")).Select(u => u.Id));
        var groups = Ids(SearchKinds.Group); if (groups.Count > 0) await Keep(SearchKinds.Group, db.SocialGroups.AsNoTracking().Where(g => groups.Contains(g.Id) && g.DeletedAt == null && g.Privacy == "public").Select(g => g.Id));
        var stores = Ids(SearchKinds.Store); if (stores.Count > 0) await Keep(SearchKinds.Store, db.WorldStores.AsNoTracking().Where(s => stores.Contains(s.Id) && s.DeletedAt == null && open.Contains(s.IslandId) && db.MarketplaceProducts.Any(p => p.Id == s.ProductId && p.Status == "active")).Select(s => s.Id));
        var products = Ids(SearchKinds.Product); if (products.Count > 0) await Keep(SearchKinds.Product, db.MarketplaceProducts.AsNoTracking().Where(p => products.Contains(p.Id) && p.Status == "active").Select(p => p.Id));
        var events = Ids(SearchKinds.Event); if (events.Count > 0) await Keep(SearchKinds.Event, db.DiscoveryEvents.AsNoTracking().Where(e => events.Contains(e.Id) && e.IsPublic && e.GroupId == null && e.EndsAt >= now && open.Contains(e.IslandId)).Select(e => e.Id));
        var places = Ids(SearchKinds.Place); if (places.Count > 0) await Keep(SearchKinds.Place, db.Landmarks.AsNoTracking().Where(l => places.Contains(l.Id) && l.IsPublic && l.DeletedAt == null && open.Contains(l.IslandId)).Select(l => l.Id));

        return [.. found.Where(e => ok.Contains(e.Id) && !e.Hidden && (e.OwnerUserId is not { } owner || !blocked.Contains(owner)))];
    }

    private static SearchResultItem Item(SearchEntry e, double score, bool favorite = false) =>
        new(e.Kind, SearchKinds.Label(e.Kind), e.RefId, e.Title, e.Subtitle, e.Description, e.Category, e.ThumbnailUrl, e.IslandId, e.X, e.Z, score, e.Popularity, e.SourceUpdatedAt, e.StartsAt, favorite);

    private static async Task<HashSet<(string Kind, Guid Id)>> FavoriteKeysAsync(LifeVerseDbContext db, Guid viewer, CancellationToken ct) =>
        [.. (await db.SearchFavorites.AsNoTracking().Where(f => f.UserId == viewer).Select(f => new { f.Kind, f.RefId }).ToListAsync(ct)).Select(f => (f.Kind, f.RefId))];

    /// <summary>
    /// Looks for what the text asks, among the kinds wanted (all by default), optionally in one category and/or one island, ordered by relevance
    /// (default), popularity or how recently it changed. Counts by kind and by category describe everything that was found, not only the page.
    /// </summary>
    public static async Task<SearchPage> SearchAsync(LifeVerseDbContext db, Guid viewer, SearchRequest request, DateTimeOffset now, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page); var size = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, MaxPageSize); var sort = NormalizeSort(request.Sort);
        var text = (request.Q ?? string.Empty).Trim(); var terms = SearchText.Terms(text);
        if (terms.Length == 0 || SearchText.Normalize(text).Length < MinQueryLength) return new SearchPage(text, 0, page, size, [], new Dictionary<string, int>(), new Dictionary<string, int>(), sort);
        var category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
        var found = await AllowedAsync(db, viewer, await CandidatesAsync(db, terms, Kinds(request.Types), CandidateLimit, ct, category, request.IslandId), now, ct);
        var ranked = found.Select(e => (Entry: e, Score: SearchText.Relevance(e.Title, e.SearchText, text, terms, e.Popularity, e.SourceUpdatedAt, now))).ToList();
        var ordered = sort switch
        {
            SortPopularity => ranked.OrderByDescending(r => r.Entry.Popularity).ThenByDescending(r => r.Score),
            SortUpdated => ranked.OrderByDescending(r => r.Entry.SourceUpdatedAt).ThenByDescending(r => r.Score),
            _ => ranked.OrderByDescending(r => r.Score).ThenByDescending(r => r.Entry.Popularity),
        };
        var list = ordered.ThenBy(r => r.Entry.Title, StringComparer.Ordinal).ThenBy(r => r.Entry.Id).ToList();
        var counts = list.GroupBy(r => r.Entry.Kind).ToDictionary(g => g.Key, g => g.Count());
        var categories = list.Where(r => r.Entry.Category.Length > 0).GroupBy(r => r.Entry.Category, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(20).ToDictionary(g => g.Key, g => g.Count());
        var pageItems = list.Skip((page - 1) * size).Take(size).ToList(); var favorites = pageItems.Count == 0 ? [] : await FavoriteKeysAsync(db, viewer, ct);
        return new SearchPage(text, list.Count, page, size, [.. pageItems.Select(r => Item(r.Entry, r.Score, favorites.Contains((r.Entry.Kind, r.Entry.RefId))))], counts, categories, sort);
    }

    // ---- favorites -------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Marks a result as favorite. It must be something the person can see now; marking it twice changes nothing. False when it cannot be found or the list is full.</summary>
    public static async Task<(bool Ok, string? Error)> AddFavoriteAsync(LifeVerseDbContext db, Guid viewer, string kind, Guid refId, DateTimeOffset now, CancellationToken ct)
    {
        kind = SearchKinds.All.FirstOrDefault(k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        if (kind.Length == 0) return (false, "unknown_kind");
        var entry = await db.SearchEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Kind == kind && e.RefId == refId && !e.Hidden, ct);
        if (entry is null || (await AllowedAsync(db, viewer, [entry], now, ct)).Count == 0) return (false, "not_found");
        if (await db.SearchFavorites.AnyAsync(f => f.UserId == viewer && f.Kind == kind && f.RefId == refId, ct)) return (true, null);
        if (await db.SearchFavorites.CountAsync(f => f.UserId == viewer, ct) >= MaxFavorites) return (false, "favorites_full");
        db.SearchFavorites.Add(new SearchFavorite { Id = Guid.NewGuid(), UserId = viewer, Kind = kind, RefId = refId, CreatedAt = now });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { /* the same favorite was added at the same time: it is there */ }
        return (true, null);
    }

    public static async Task RemoveFavoriteAsync(LifeVerseDbContext db, Guid viewer, string kind, Guid refId, CancellationToken ct)
    {
        var lower = kind.ToLowerInvariant();
        db.SearchFavorites.RemoveRange(await db.SearchFavorites.Where(f => f.UserId == viewer && f.Kind == lower && f.RefId == refId).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The favorites, the last one marked first. What is no longer public (or was removed) is not shown, but stays marked in case it comes back.</summary>
    public static async Task<SearchPage> FavoritesAsync(LifeVerseDbContext db, Guid viewer, string[]? types, int page, int pageSize, DateTimeOffset now, CancellationToken ct)
    {
        page = Math.Max(1, page); var size = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, MaxPageSize); var kinds = Kinds(types);
        var marks = await db.SearchFavorites.AsNoTracking().Where(f => f.UserId == viewer && kinds.Contains(f.Kind)).OrderByDescending(f => f.CreatedAt).ThenBy(f => f.Id).ToListAsync(ct);
        var ids = marks.Select(f => f.RefId).ToList();
        var entries = (await db.SearchEntries.AsNoTracking().Where(e => ids.Contains(e.RefId) && !e.Hidden).ToListAsync(ct)).GroupBy(e => (e.Kind, e.RefId)).ToDictionary(g => g.Key, g => g.First());
        var wanted = marks.Select(f => entries.GetValueOrDefault((f.Kind, f.RefId))).Where(e => e is not null).Select(e => e!).ToList();
        var allowed = (await AllowedAsync(db, viewer, wanted, now, ct)).Select(e => e.Id).ToHashSet();
        var shown = wanted.Where(e => allowed.Contains(e.Id)).ToList();
        var counts = shown.GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count());
        return new SearchPage(string.Empty, shown.Count, page, size, [.. shown.Skip((page - 1) * size).Take(size).Select(e => Item(e, 0, true))], counts);
    }

    // ---- interests and recommendations ----------------------------------------------------------------------------------------------------------------

    /// <summary>What the person said they like, in the order they said it.</summary>
    public static async Task<List<string>> InterestsAsync(LifeVerseDbContext db, Guid viewer, CancellationToken ct) =>
        await db.SearchInterests.AsNoTracking().Where(i => i.UserId == viewer).OrderBy(i => i.CreatedAt).ThenBy(i => i.Term).Select(i => i.Term).ToListAsync(ct);

    /// <summary>Replaces the interests: each is cleaned (no accents, no punctuation, at most 3 words), repeated ones are dropped, and at most 20 are kept.</summary>
    public static async Task<List<string>> SetInterestsAsync(LifeVerseDbContext db, Guid viewer, IEnumerable<string?> interests, DateTimeOffset now, CancellationToken ct)
    {
        var clean = interests.Select(i => string.Join(' ', SearchText.Normalize(i).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3))).Select(i => i.Length > 32 ? i[..32].TrimEnd() : i)
            .Where(i => i.Length >= 2).Distinct().Take(MaxInterests).ToList();
        db.SearchInterests.RemoveRange(await db.SearchInterests.Where(i => i.UserId == viewer).ToListAsync(ct));
        var at = now; foreach (var term in clean) db.SearchInterests.Add(new SearchInterest { Id = Guid.NewGuid(), UserId = viewer, Term = term, CreatedAt = at = at.AddMilliseconds(1) });
        await db.SaveChangesAsync(ct);
        return clean;
    }

    /// <summary>
    /// Things that match what the person likes: the more of their interests a result has, the higher it goes; then how well it fits and how popular it is.
    /// People are not recommended (only things), and what is already a favorite is left out. Without interests there is nothing to recommend.
    /// </summary>
    public static async Task<List<SearchResultItem>> RecommendationsAsync(LifeVerseDbContext db, Guid viewer, int take, DateTimeOffset now, CancellationToken ct)
    {
        var size = Math.Clamp(take, 1, MaxPageSize); var interests = await InterestsAsync(db, viewer, ct); if (interests.Count == 0) return [];
        var kinds = SearchKinds.All.Where(k => k != SearchKinds.Person).ToArray(); var hits = new Dictionary<Guid, (SearchEntry Entry, int Matches, double Best)>();
        foreach (var interest in interests)
        {
            var terms = SearchText.Terms(interest); if (terms.Length == 0) continue;
            foreach (var e in await CandidatesAsync(db, terms, kinds, 60, ct))
            {
                var score = SearchText.Relevance(e.Title, e.SearchText, interest, terms, e.Popularity, e.SourceUpdatedAt, now);
                hits[e.Id] = hits.TryGetValue(e.Id, out var old) ? (old.Entry, old.Matches + 1, Math.Max(old.Best, score)) : (e, 1, score);
            }
        }
        var favorites = await FavoriteKeysAsync(db, viewer, ct); var allowed = (await AllowedAsync(db, viewer, [.. hits.Values.Select(h => h.Entry)], now, ct)).Select(e => e.Id).ToHashSet();
        return [.. hits.Values.Where(h => allowed.Contains(h.Entry.Id) && !favorites.Contains((h.Entry.Kind, h.Entry.RefId)))
            .OrderByDescending(h => h.Matches).ThenByDescending(h => h.Best).ThenByDescending(h => h.Entry.Popularity).ThenBy(h => h.Entry.Title, StringComparer.Ordinal).ThenBy(h => h.Entry.Id)
            .Take(size).Select(h => Item(h.Entry, h.Matches * 100 + h.Best))];
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
        group.MapGet("", async (string? q, string? types, int? page, int? pageSize, string? category, Guid? islandId, string? sort, HttpContext c, LifeVerseDbContext db, SearchMetrics metrics, TimeProvider t, CancellationToken ct) =>
        {
            if (!Who(c, out var me)) return Results.Unauthorized();
            var clock = System.Diagnostics.Stopwatch.StartNew(); var result = await GlobalSearch.SearchAsync(db, me, new SearchRequest(q, Split(types), page ?? 1, pageSize ?? 20, category, islandId, sort), t.GetUtcNow(), ct);
            if ((page ?? 1) <= 1) metrics.Searched(q, result.Total, clock.ElapsedMilliseconds, t.GetUtcNow()); // the first page is the search; the next pages are the same search read further
            return Results.Ok(result);
        });
        group.MapGet("/favorites", async (string? types, int? page, int? pageSize, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            Who(c, out var me) ? Results.Ok(await GlobalSearch.FavoritesAsync(db, me, Split(types), page ?? 1, pageSize ?? 20, t.GetUtcNow(), ct)) : Results.Unauthorized());
        group.MapPut("/favorites/{kind}/{id:guid}", async (string kind, Guid id, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
        {
            if (!Who(c, out var me)) return Results.Unauthorized();
            var (ok, error) = await GlobalSearch.AddFavoriteAsync(db, me, kind, id, t.GetUtcNow(), ct);
            return ok ? Results.NoContent() : error == "favorites_full" ? Results.Conflict(new { error }) : error == "unknown_kind" ? Results.BadRequest(new { error }) : Results.NotFound(new { error });
        });
        group.MapDelete("/favorites/{kind}/{id:guid}", async (string kind, Guid id, HttpContext c, LifeVerseDbContext db, CancellationToken ct) =>
        { if (!Who(c, out var me)) return Results.Unauthorized(); await GlobalSearch.RemoveFavoriteAsync(db, me, kind, id, ct); return Results.NoContent(); });
        group.MapGet("/interests", async (HttpContext c, LifeVerseDbContext db, CancellationToken ct) =>
            Who(c, out var me) ? Results.Ok(await GlobalSearch.InterestsAsync(db, me, ct)) : Results.Unauthorized());
        group.MapPut("/interests", async (InterestsRequest body, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            Who(c, out var me) ? Results.Ok(await GlobalSearch.SetInterestsAsync(db, me, body.Interests ?? [], t.GetUtcNow(), ct)) : Results.Unauthorized());
        group.MapGet("/recommended", async (int? take, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            Who(c, out var me) ? Results.Ok(await GlobalSearch.RecommendationsAsync(db, me, take ?? 12, t.GetUtcNow(), ct)) : Results.Unauthorized());
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

    private static string[]? Split(string? types) => types?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool Who(HttpContext context, out Guid user) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out user);
}
