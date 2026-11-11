using LifeVerse.Api.Data;
using LifeVerse.Shared.Search;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Discovery;

public sealed record IndexReport(int Written, int Removed, TimeSpan Took);

/// <summary>
/// Keeps the search index (<see cref="SearchEntry"/>) in step with the real data without ever rebuilding all of it: for each kind of source it reads only
/// what changed since the last run (a watermark per source), writes or removes the lines of the things that changed, and sweeps away the lines of things
/// that are not public any more or are gone. The index only ever holds what anybody may find; what a given person may see is checked again when they search.
/// </summary>
public static class SearchIndexer
{
    public const int Batch = 500;
    private sealed record Stamped(Guid Id, DateTimeOffset At);

    private static readonly DateTimeOffset Never = DateTimeOffset.MinValue;

    /// <summary>One pass over every source. Safe to run any time and as often as wanted: running it twice in a row changes nothing the second time.</summary>
    public static async Task<IndexReport> RunAsync(LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow; int written = 0, removed = 0;
        written += await PumpAsync(db, "island", now, (wm, done) => db.Islands.AsNoTracking().Where(i => i.UpdatedAt >= wm && !done.Contains(i.Id)).OrderBy(i => i.UpdatedAt).ThenBy(i => i.Id).Select(i => new Stamped(i.Id, i.UpdatedAt)), ApplyIslandsAsync, ct);
        written += await PumpAsync(db, "parcel", now, (wm, done) => db.IslandParcels.AsNoTracking().Where(p => p.UpdatedAt >= wm && !done.Contains(p.Id)).OrderBy(p => p.UpdatedAt).ThenBy(p => p.Id).Select(p => new Stamped(p.Id, p.UpdatedAt)), ApplyParcelsAsync, ct);
        written += await PumpAsync(db, "person", now, (wm, done) => db.Users.AsNoTracking().Where(u => u.UpdatedAt >= wm && !done.Contains(u.Id)).OrderBy(u => u.UpdatedAt).ThenBy(u => u.Id).Select(u => new Stamped(u.Id, u.UpdatedAt)), ApplyPeopleAsync, ct);
        written += await PumpAsync(db, "person.avatar", now, (wm, done) => db.Avatars.AsNoTracking().Where(a => a.UpdatedAt >= wm && !done.Contains(a.UserId)).OrderBy(a => a.UpdatedAt).ThenBy(a => a.UserId).Select(a => new Stamped(a.UserId, a.UpdatedAt)), ApplyPeopleAsync, ct);
        written += await PumpAsync(db, "group", now, (wm, done) => db.SocialGroups.AsNoTracking().Where(g => g.UpdatedAt >= wm && !done.Contains(g.Id)).OrderBy(g => g.UpdatedAt).ThenBy(g => g.Id).Select(g => new Stamped(g.Id, g.UpdatedAt)), ApplyGroupsAsync, ct);
        written += await PumpAsync(db, "store", now, (wm, done) => db.WorldStores.AsNoTracking().Where(s => s.UpdatedAt >= wm && !done.Contains(s.Id)).OrderBy(s => s.UpdatedAt).ThenBy(s => s.Id).Select(s => new Stamped(s.Id, s.UpdatedAt)), ApplyStoresAsync, ct);
        written += await PumpAsync(db, "product", now, (wm, done) => db.MarketplaceProducts.AsNoTracking().Where(p => p.UpdatedAt >= wm && !done.Contains(p.Id)).OrderBy(p => p.UpdatedAt).ThenBy(p => p.Id).Select(p => new Stamped(p.Id, p.UpdatedAt)), ApplyProductsAsync, ct);
        written += await PumpAsync(db, "event", now, (wm, done) => db.DiscoveryEvents.AsNoTracking().Where(e => e.CreatedAt >= wm && !done.Contains(e.Id)).OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).Select(e => new Stamped(e.Id, e.CreatedAt)), ApplyEventsAsync, ct);
        written += await PumpAsync(db, "poi", now, (wm, done) => db.Landmarks.AsNoTracking().Where(l => l.UpdatedAt >= wm && !done.Contains(l.Id)).OrderBy(l => l.UpdatedAt).ThenBy(l => l.Id).Select(l => new Stamped(l.Id, l.UpdatedAt)), ApplyPlacesAsync, ct);
        removed += await SweepAsync(db, now, ct);
        written += await RefreshPopularityAsync(db, now, ct);
        return new IndexReport(written, removed, DateTimeOffset.UtcNow - started);
    }

    /// <summary>Forgets how far the sources were read, so the next run reads them all again (after a change in what is indexed, or to repair).</summary>
    public static async Task<IndexReport> RebuildAsync(LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        db.SearchStates.RemoveRange(await db.SearchStates.ToListAsync(ct)); await db.SaveChangesAsync(ct);
        return await RunAsync(db, now, ct);
    }

    // ---- reading only what changed -----------------------------------------------------------------------------------------------------

    private static async Task<int> PumpAsync(LifeVerseDbContext db, string kind, DateTimeOffset now, Func<DateTimeOffset, List<Guid>, IQueryable<Stamped>> changed, Func<LifeVerseDbContext, List<Guid>, DateTimeOffset, CancellationToken, Task<int>> apply, CancellationToken ct)
    {
        var state = await db.SearchStates.SingleOrDefaultAsync(s => s.Kind == kind, ct);
        if (state is null) { state = new SearchState { Kind = kind, Watermark = Never }; db.SearchStates.Add(state); }
        var done = new Dictionary<Guid, DateTimeOffset>(); var total = 0;
        while (true)
        {
            var batch = await changed(state.Watermark, [.. done.Keys]).Take(Batch).ToListAsync(ct);
            if (batch.Count == 0) break;
            total += await apply(db, [.. batch.Select(b => b.Id).Distinct()], now, ct);
            var top = batch.Max(b => b.At);
            // what shares the newest stamp is read again next time: remember it so it is not written twice now
            foreach (var old in done.Where(d => d.Value < top).Select(d => d.Key).ToList()) done.Remove(old);
            foreach (var b in batch.Where(b => b.At == top)) done[b.Id] = b.At;
            state.Watermark = top;
            if (batch.Count < Batch) break;
        }
        state.RanAt = now; state.Entries = await db.SearchEntries.CountAsync(e => e.Kind == KindOf(kind), ct);
        await db.SaveChangesAsync(ct);
        return total;
    }

    private static string KindOf(string source) => source.Split('.')[0];

    // ---- writing lines ---------------------------------------------------------------------------------------------------------------------

    private static async Task<Dictionary<Guid, SearchEntry>> EntriesAsync(LifeVerseDbContext db, string kind, List<Guid> refIds, CancellationToken ct) =>
        await db.SearchEntries.Where(e => e.Kind == kind && refIds.Contains(e.RefId)).ToDictionaryAsync(e => e.RefId, ct);

    /// <summary>Writes the line of something that is findable, or takes it out when it is not. Moderation's hiding is never undone by a rewrite.</summary>
    private static bool Put(LifeVerseDbContext db, Dictionary<Guid, SearchEntry> existing, string kind, Guid refId, bool findable, DateTimeOffset now, DateTimeOffset updated, Action<SearchEntry> fill)
    {
        existing.TryGetValue(refId, out var entry);
        if (!findable) { if (entry is not null) { db.SearchEntries.Remove(entry); existing.Remove(refId); return true; } return false; }
        if (entry is null) { entry = new SearchEntry { Id = Guid.NewGuid(), Kind = kind, RefId = refId, Title = string.Empty, SearchText = string.Empty }; db.SearchEntries.Add(entry); existing[refId] = entry; }
        fill(entry); entry.SourceUpdatedAt = updated;
        entry.Title = Cut(entry.Title, 160); entry.Subtitle = Cut(entry.Subtitle, 200); entry.Description = Cut(entry.Description, 400); entry.Category = Cut(entry.Category, 48);
        if (db.Entry(entry).State == EntityState.Unchanged) return false; // read again but identical: nothing to write
        entry.IndexedAt = now; return true;
    }

    private static string Cut(string? text, int max) => text is null ? string.Empty : text.Length <= max ? text : text[..max];
    private static string Words(params string?[] parts) => SearchText.Normalize(string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p))));

    private static async Task<int> ApplyIslandsAsync(LifeVerseDbContext db, List<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        var islands = await db.Islands.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(ct); var existing = await EntriesAsync(db, SearchKinds.Island, ids, ct);
        var flipped = new List<Guid>(); var written = 0;
        foreach (var i in islands)
        {
            var findable = i.DeletedAt == null && i.IsPublic && i.ShowInSearch; if (findable != existing.ContainsKey(i.Id)) flipped.Add(i.Id);
            if (Put(db, existing, SearchKinds.Island, i.Id, findable, now, i.UpdatedAt, e =>
            {
                e.Title = i.Name; e.Subtitle = "Ilha · " + i.Category; e.Category = i.Category; e.Description = SearchText.Snippet(i.Description, 400); e.SearchText = Words(i.Name, i.Category, i.Description);
                e.IslandId = i.Id; e.X = i.SpawnX; e.Z = i.SpawnZ; e.ThumbnailUrl = i.CoverImageUrl; e.OwnerUserId = i.OwnerUserId;
            })) written++;
        }
        await db.SaveChangesAsync(ct);
        // whatever stands on an island appears and disappears with it
        if (flipped.Count > 0)
        {
            written += await ApplyParcelsAsync(db, await db.IslandParcels.AsNoTracking().Where(p => flipped.Contains(p.IslandId)).Select(p => p.Id).ToListAsync(ct), now, ct);
            written += await ApplyStoresAsync(db, await db.WorldStores.AsNoTracking().Where(s => flipped.Contains(s.IslandId)).Select(s => s.Id).ToListAsync(ct), now, ct);
            written += await ApplyEventsAsync(db, await db.DiscoveryEvents.AsNoTracking().Where(e => flipped.Contains(e.IslandId)).Select(e => e.Id).ToListAsync(ct), now, ct);
            written += await ApplyPlacesAsync(db, await db.Landmarks.AsNoTracking().Where(l => flipped.Contains(l.IslandId)).Select(l => l.Id).ToListAsync(ct), now, ct);
        }
        return written;
    }

    private static async Task<int> ApplyParcelsAsync(LifeVerseDbContext db, List<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        if (ids.Count == 0) return 0;
        var rows = await (from p in db.IslandParcels.AsNoTracking() join i in db.Islands.AsNoTracking() on p.IslandId equals i.Id where ids.Contains(p.Id) select new { Parcel = p, IslandName = i.Name, Open = i.IsPublic && i.DeletedAt == null }).ToListAsync(ct);
        var existing = await EntriesAsync(db, SearchKinds.Parcel, ids, ct); var written = 0;
        foreach (var r in rows)
        {
            var p = r.Parcel; var offer = p.SalePrice != null ? "venda" : p.RentPrice != null ? "aluguel" : "terreno";
            if (Put(db, existing, SearchKinds.Parcel, p.Id, p.DeletedAt == null && p.AccessMode == "public" && r.Open, now, p.UpdatedAt, e =>
            {
                e.Title = p.Name; e.Subtitle = "Terreno em " + r.IslandName; e.Category = offer; e.Description = SearchText.Snippet(p.Description, 400);
                e.SearchText = Words(p.Name, r.IslandName, offer == "venda" ? "a venda" : offer, p.Description); e.IslandId = p.IslandId; e.X = p.SpawnX; e.Z = p.SpawnZ; e.OwnerUserId = p.OwnerUserId;
            })) written++;
        }
        foreach (var gone in ids.Except(rows.Select(r => r.Parcel.Id))) if (Put(db, existing, SearchKinds.Parcel, gone, false, now, now, _ => { })) written++;
        await db.SaveChangesAsync(ct); return written;
    }

    private static async Task<int> ApplyPeopleAsync(LifeVerseDbContext db, List<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().Include(u => u.Avatar).Where(u => ids.Contains(u.Id)).ToListAsync(ct); var existing = await EntriesAsync(db, SearchKinds.Person, ids, ct); var written = 0;
        foreach (var u in users)
        {
            var display = u.Avatar?.DisplayName is { Length: > 0 } name ? name : u.Username;
            if (Put(db, existing, SearchKinds.Person, u.Id, Discoverable(u), now, u.Avatar is { } a && a.UpdatedAt > u.UpdatedAt ? a.UpdatedAt : u.UpdatedAt, e =>
            {
                e.Title = display; e.Subtitle = "@" + u.Username; e.Description = SearchText.Snippet(u.Bio, 400); e.SearchText = Words(display, u.Username, u.Bio); e.ThumbnailUrl = u.ProfilePhotoUrl; e.OwnerUserId = u.Id;
            })) written++;
        }
        foreach (var gone in ids.Except(users.Select(u => u.Id))) if (Put(db, existing, SearchKinds.Person, gone, false, now, now, _ => { })) written++;
        await db.SaveChangesAsync(ct); return written;
    }

    /// <summary>A person can be found when they are active, not the system account, and have not made their profile private or asked not to be found.</summary>
    public static bool Discoverable(User u) => u.DeletedAt == null && u.Status == "active" && u.PasswordHash != "!" && !u.PrivacyJson.Contains("\"profile\":\"private\"", StringComparison.Ordinal) && !u.PrivacyJson.Contains("\"searchable\":\"no\"", StringComparison.Ordinal);

    private static async Task<int> ApplyGroupsAsync(LifeVerseDbContext db, List<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        var groups = await db.SocialGroups.AsNoTracking().Where(g => ids.Contains(g.Id)).ToListAsync(ct); var existing = await EntriesAsync(db, SearchKinds.Group, ids, ct); var written = 0;
        foreach (var g in groups)
            if (Put(db, existing, SearchKinds.Group, g.Id, g.DeletedAt == null && g.Privacy == "public", now, g.UpdatedAt, e =>
            {
                e.Title = g.Name; e.Subtitle = (g.Tag is { Length: > 0 } ? $"[{g.Tag}] " : string.Empty) + "Grupo"; e.Description = SearchText.Snippet(g.Description, 400); e.SearchText = Words(g.Name, g.Tag, "grupo", g.Description);
                e.ThumbnailUrl = g.ImageUrl; e.OwnerUserId = g.OwnerUserId;
            })) written++;
        foreach (var gone in ids.Except(groups.Select(g => g.Id))) if (Put(db, existing, SearchKinds.Group, gone, false, now, now, _ => { })) written++;
        await db.SaveChangesAsync(ct); return written;
    }

    private static async Task<int> ApplyStoresAsync(LifeVerseDbContext db, List<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        if (ids.Count == 0) return 0;
        var rows = await (from s in db.WorldStores.AsNoTracking() join i in db.Islands.AsNoTracking() on s.IslandId equals i.Id join p in db.MarketplaceProducts.AsNoTracking() on s.ProductId equals p.Id where ids.Contains(s.Id)
                          select new { Store = s, IslandName = i.Name, Open = i.IsPublic && i.DeletedAt == null, Product = p.Name, Active = p.Status == "active", p.ImageUrl }).ToListAsync(ct);
        var objectIds = rows.Where(r => r.Store.ObjectId != null).Select(r => r.Store.ObjectId!.Value).ToList();
        var spots = objectIds.Count == 0 ? [] : await db.BuildObjects.AsNoTracking().Where(o => objectIds.Contains(o.Id)).Select(o => new { o.Id, o.PositionX, o.PositionZ }).ToDictionaryAsync(o => o.Id, ct);
        var existing = await EntriesAsync(db, SearchKinds.Store, ids, ct); var written = 0;
        foreach (var r in rows)
        {
            var s = r.Store; var spot = s.ObjectId is { } o && spots.TryGetValue(o, out var at) ? at : null;
            if (Put(db, existing, SearchKinds.Store, s.Id, s.DeletedAt == null && r.Open && r.Active, now, s.UpdatedAt, e =>
            {
                e.Title = s.DisplayName; e.Subtitle = "Loja em " + r.IslandName; e.Description = r.Product; e.SearchText = Words(s.DisplayName, r.Product, r.IslandName, "loja"); e.IslandId = s.IslandId;
                e.X = spot?.PositionX; e.Z = spot?.PositionZ; e.ThumbnailUrl = r.ImageUrl; e.OwnerUserId = s.OwnerUserId;
            })) written++;
        }
        foreach (var gone in ids.Except(rows.Select(r => r.Store.Id))) if (Put(db, existing, SearchKinds.Store, gone, false, now, now, _ => { })) written++;
        await db.SaveChangesAsync(ct); return written;
    }

    private static async Task<int> ApplyProductsAsync(LifeVerseDbContext db, List<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        var products = await db.MarketplaceProducts.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct); var existing = await EntriesAsync(db, SearchKinds.Product, ids, ct);
        var groupIds = products.Where(p => p.GroupId != null).Select(p => p.GroupId!.Value).Distinct().ToList();
        var groups = groupIds.Count == 0 ? [] : await db.SocialGroups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct); var written = 0;
        foreach (var p in products)
        {
            var seller = p.GroupId is { } g && groups.TryGetValue(g, out var gn) ? gn : null;
            if (Put(db, existing, SearchKinds.Product, p.Id, p.Status == "active", now, p.UpdatedAt, e =>
            {
                e.Title = p.Name; e.Subtitle = $"{p.PriceCoins:N0} moedas · {p.Category}" + (seller is null ? string.Empty : $" · por {seller}"); e.Category = p.Category; e.Description = SearchText.Snippet(p.Description, 400);
                e.SearchText = Words(p.Name, p.Category, p.Description, seller); e.ThumbnailUrl = p.ImageUrl; e.OwnerUserId = p.SellerUserId;
            })) written++;
        }
        foreach (var gone in ids.Except(products.Select(p => p.Id))) if (Put(db, existing, SearchKinds.Product, gone, false, now, now, _ => { })) written++;
        await db.SaveChangesAsync(ct); return written;
    }

    private static async Task<int> ApplyEventsAsync(LifeVerseDbContext db, List<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        if (ids.Count == 0) return 0;
        var rows = await (from ev in db.DiscoveryEvents.AsNoTracking() join i in db.Islands.AsNoTracking() on ev.IslandId equals i.Id where ids.Contains(ev.Id) select new { Event = ev, IslandName = i.Name, Open = i.IsPublic && i.DeletedAt == null }).ToListAsync(ct);
        var existing = await EntriesAsync(db, SearchKinds.Event, ids, ct); var written = 0;
        foreach (var r in rows)
        {
            var ev = r.Event;
            if (Put(db, existing, SearchKinds.Event, ev.Id, ev.IsPublic && ev.GroupId == null && ev.EndsAt >= now && r.Open, now, ev.CreatedAt, e =>
            {
                e.Title = ev.Name; e.Subtitle = "Evento em " + r.IslandName; e.Description = SearchText.Snippet(ev.Description, 400); e.SearchText = Words(ev.Name, r.IslandName, "evento", ev.Description);
                e.IslandId = ev.IslandId; e.StartsAt = ev.StartsAt; e.ExpiresAt = ev.EndsAt; e.OwnerUserId = ev.OrganizerUserId;
            })) written++;
        }
        foreach (var gone in ids.Except(rows.Select(r => r.Event.Id))) if (Put(db, existing, SearchKinds.Event, gone, false, now, now, _ => { })) written++;
        await db.SaveChangesAsync(ct); return written;
    }

    private static async Task<int> ApplyPlacesAsync(LifeVerseDbContext db, List<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        if (ids.Count == 0) return 0;
        var rows = await (from l in db.Landmarks.AsNoTracking() join i in db.Islands.AsNoTracking() on l.IslandId equals i.Id where ids.Contains(l.Id) select new { Landmark = l, IslandName = i.Name, Open = i.IsPublic && i.DeletedAt == null }).ToListAsync(ct);
        var existing = await EntriesAsync(db, SearchKinds.Place, ids, ct); var written = 0;
        foreach (var r in rows)
        {
            var l = r.Landmark;
            if (Put(db, existing, SearchKinds.Place, l.Id, l.IsPublic && l.DeletedAt == null && r.Open, now, l.UpdatedAt, e =>
            {
                e.Title = l.Name; e.Subtitle = "Ponto de interesse em " + r.IslandName; e.SearchText = Words(l.Name, r.IslandName, "ponto de interesse"); e.IslandId = l.IslandId; e.X = l.PositionX; e.Z = l.PositionZ; e.OwnerUserId = l.OwnerUserId;
            })) written++;
        }
        foreach (var gone in ids.Except(rows.Select(r => r.Landmark.Id))) if (Put(db, existing, SearchKinds.Place, gone, false, now, now, _ => { })) written++;
        await db.SaveChangesAsync(ct); return written;
    }

    // ---- what is gone, and what is popular ------------------------------------------------------------------------------------------------------

    /// <summary>Takes out the lines of things that are not findable any more but did not say so by changing (deleted for good, made private, an island that closed, an event that ended).</summary>
    private static async Task<int> SweepAsync(LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var removed = 0;
        async Task Drop(IQueryable<SearchEntry> lines) { var list = await lines.ToListAsync(ct); if (list.Count == 0) return; db.SearchEntries.RemoveRange(list); removed += list.Count; await db.SaveChangesAsync(ct); }
        await Drop(db.SearchEntries.Where(e => e.Kind == SearchKinds.Island && !db.Islands.Any(i => i.Id == e.RefId && i.IsPublic && i.ShowInSearch && i.DeletedAt == null)));
        await Drop(db.SearchEntries.Where(e => e.Kind == SearchKinds.Parcel && !db.IslandParcels.Any(p => p.Id == e.RefId && p.DeletedAt == null && p.AccessMode == "public" && db.Islands.Any(i => i.Id == p.IslandId && i.IsPublic && i.DeletedAt == null))));
        await Drop(db.SearchEntries.Where(e => e.Kind == SearchKinds.Person && !db.Users.Any(u => u.Id == e.RefId && u.DeletedAt == null && u.Status == "active")));
        await Drop(db.SearchEntries.Where(e => e.Kind == SearchKinds.Group && !db.SocialGroups.Any(g => g.Id == e.RefId && g.DeletedAt == null && g.Privacy == "public")));
        await Drop(db.SearchEntries.Where(e => e.Kind == SearchKinds.Store && !db.WorldStores.Any(s => s.Id == e.RefId && s.DeletedAt == null && db.Islands.Any(i => i.Id == s.IslandId && i.IsPublic && i.DeletedAt == null) && db.MarketplaceProducts.Any(p => p.Id == s.ProductId && p.Status == "active"))));
        await Drop(db.SearchEntries.Where(e => e.Kind == SearchKinds.Product && !db.MarketplaceProducts.Any(p => p.Id == e.RefId && p.Status == "active")));
        await Drop(db.SearchEntries.Where(e => e.Kind == SearchKinds.Event && (e.ExpiresAt < now || !db.DiscoveryEvents.Any(v => v.Id == e.RefId && v.IsPublic && v.GroupId == null))));
        await Drop(db.SearchEntries.Where(e => e.Kind == SearchKinds.Place && !db.Landmarks.Any(l => l.Id == e.RefId && l.IsPublic && l.DeletedAt == null && db.Islands.Any(i => i.Id == l.IslandId && i.IsPublic && i.DeletedAt == null))));
        return removed;
    }

    /// <summary>
    /// How much people like each thing: islands by favourites and by people there in the last day, groups by members, products by sales, events by who joined.
    /// Recomputed each run, because these numbers change without the thing itself changing.
    /// </summary>
    private static async Task<int> RefreshPopularityAsync(LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var changed = 0;
        async Task Set(string kind, Dictionary<Guid, double> values)
        {
            foreach (var e in await db.SearchEntries.Where(x => x.Kind == kind).ToListAsync(ct)) { var v = values.GetValueOrDefault(e.RefId); if (Math.Abs(e.Popularity - v) > .0001) { e.Popularity = v; changed++; } }
            await db.SaveChangesAsync(ct);
        }
        var recent = now.AddDays(-1);
        var favourites = await db.IslandFavorites.AsNoTracking().GroupBy(f => f.IslandId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => (double)g.N, ct);
        var present = await db.PlayerLocations.AsNoTracking().Where(l => l.UpdatedAt >= recent).GroupBy(l => l.IslandId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => (double)g.N, ct);
        await Set(SearchKinds.Island, favourites.Keys.Union(present.Keys).ToDictionary(k => k, k => favourites.GetValueOrDefault(k) + present.GetValueOrDefault(k) * 2));
        await Set(SearchKinds.Group, await db.GroupMembers.AsNoTracking().GroupBy(m => m.GroupId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => (double)g.N, ct));
        await Set(SearchKinds.Product, await db.MarketplaceOrders.AsNoTracking().Where(o => o.Status == "completed").GroupBy(o => o.ProductId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => (double)g.N, ct));
        await Set(SearchKinds.Event, await db.EventAttendees.AsNoTracking().GroupBy(a => a.EventId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => (double)g.N, ct));
        return changed;
    }
}

/// <summary>Brings the index up to date every half minute, in the background.</summary>
public sealed class SearchIndexWorker(IServiceScopeFactory scopes, ILogger<SearchIndexWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var report = await SearchIndexer.RunAsync(scope.ServiceProvider.GetRequiredService<LifeVerseDbContext>(), DateTimeOffset.UtcNow, stoppingToken);
                if (scope.ServiceProvider.GetService<SearchMetrics>() is { } metrics) await metrics.FlushAsync(scope.ServiceProvider.GetRequiredService<LifeVerseDbContext>(), stoppingToken);
                if (report.Written + report.Removed > 0) logger.LogInformation("event=search_index_updated written={Written} removed={Removed} ms={Ms}", report.Written, report.Removed, (int)report.Took.TotalMilliseconds);
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { logger.LogWarning(exception, "event=search_index_failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
