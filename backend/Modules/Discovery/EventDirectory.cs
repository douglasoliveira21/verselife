using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Discovery;

/// <summary>The words and limits of events.</summary>
public static class EventRules
{
    public static readonly (string Key, string Label)[] Categories =
    [("show", "Show"), ("festa", "Festa"), ("encontro", "Encontro social"), ("rpg", "RPG"), ("competicao", "Competição"), ("aula", "Aula"), ("exposicao", "Exposição"), ("comercial", "Evento comercial"), ("comunitario", "Evento comunitário"), ("outros", "Outros")];
    public static readonly string[] Ratings = ["livre", "moderado", "adulto"];
    public const int MaxName = 100, MinName = 3, MaxDescription = 1500, MaxCapacity = 1000, MaxActivePerOrganizer = 5, MaxActivePerIsland = 50;
    public const long MaxPrice = 1_000_000;
    public static readonly TimeSpan MaxLead = TimeSpan.FromDays(365), MinLength = TimeSpan.FromMinutes(10), MaxLength = TimeSpan.FromHours(24), Grace = TimeSpan.FromMinutes(5);
    public static string LabelOf(string key) => Categories.FirstOrDefault(c => c.Key == key).Label ?? "Outros";
}

public sealed record EventCreate(string? Name, string? Description, Guid IslandId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Capacity = 0, bool IsPublic = true, string? Category = null, string? ImageUrl = null, long PriceCoins = 0, string? ContentRating = null, float? ArrivalX = null, float? ArrivalZ = null);

/// <summary>The outcome of changing an event: ok (with the event) or a code the client turns into words.</summary>
public sealed record EventOutcome(bool Ok, string? Error = null, Guid? Id = null);

/// <summary>
/// The events of the world as the search window shows them: the full detail of one (who organizes it, when, where, what it costs, how many go, and what this viewer can do), and the changes people make: announcing an event
/// (the owner of the island, or anybody when the owner allows guests), calling it off, signing up and leaving, and keeping it in the personal calendar. Visibility and permissions are decided here, never by the client.
/// </summary>
public static class EventDirectory
{
    public static string StatusOf(DiscoveryEvent e, DateTimeOffset now) => e.CancelledAt is not null ? "cancelled" : now < e.StartsAt ? "scheduled" : now <= e.EndsAt ? "live" : "ended";

    private static async Task<bool> BlockedAsync(LifeVerseDbContext db, Guid a, Guid b, CancellationToken ct) =>
        a != b && await db.UserBlocks.AsNoTracking().AnyAsync(x => x.UserId == a && x.BlockedUserId == b || x.UserId == b && x.BlockedUserId == a, ct);

    /// <summary>Whether the viewer may know the event exists: the organizer and the owner of the island always; others only when it is public, on a public island, of nobody's group (or a group they belong to), and not of someone they are blocked with.</summary>
    private static async Task<bool> VisibleAsync(LifeVerseDbContext db, DiscoveryEvent e, Island island, Guid viewer, CancellationToken ct)
    {
        if (e.OrganizerUserId == viewer || island.OwnerUserId == viewer) return true;
        if (!e.IsPublic || !island.IsPublic || island.DeletedAt != null) return false;
        if (e.GroupId is { } group && !await db.GroupMembers.AsNoTracking().AnyAsync(m => m.GroupId == group && m.UserId == viewer, ct)) return false;
        return !await BlockedAsync(db, viewer, e.OrganizerUserId, ct);
    }

    public static Task<int> GoingAsync(LifeVerseDbContext db, Guid eventId, CancellationToken ct) => db.EventAttendees.AsNoTracking().CountAsync(a => a.EventId == eventId && a.Going, ct);

    /// <summary>The detail of one event for this viewer, or null when it does not exist for them.</summary>
    public static async Task<object?> DetailAsync(LifeVerseDbContext db, Guid viewer, Guid eventId, DateTimeOffset now, CancellationToken ct)
    {
        var e = await db.DiscoveryEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId, ct); if (e is null) return null;
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == e.IslandId && x.DeletedAt == null, ct); if (island is null || !await VisibleAsync(db, e, island, viewer, ct)) return null;
        var organizer = await db.Users.AsNoTracking().Include(u => u.Avatar).Where(u => u.Id == e.OrganizerUserId).Select(u => new { u.Id, u.Username, Display = u.Avatar != null ? u.Avatar.DisplayName : u.Username }).SingleOrDefaultAsync(ct);
        var going = await GoingAsync(db, e.Id, ct); var mine = await db.EventAttendees.AsNoTracking().SingleOrDefaultAsync(a => a.EventId == e.Id && a.UserId == viewer, ct);
        var status = StatusOf(e, now); var manage = e.OrganizerUserId == viewer || island.OwnerUserId == viewer;
        return new
        {
            id = e.Id, name = e.Name, description = e.Description, category = e.Category, categoryLabel = EventRules.LabelOf(e.Category), imageUrl = string.IsNullOrEmpty(e.ImageUrl) ? null : e.ImageUrl,
            startsAt = e.StartsAt, endsAt = e.EndsAt, status, capacity = e.Capacity, going, full = e.Capacity > 0 && going >= e.Capacity, priceCoins = e.PriceCoins, contentRating = e.ContentRating,
            island = new { id = island.Id, name = island.Name }, arrivalX = e.ArrivalX ?? island.SpawnX, arrivalZ = e.ArrivalZ ?? island.SpawnZ, groupId = e.GroupId,
            organizer = organizer is null ? null : new { id = organizer.Id, username = organizer.Username, displayName = organizer.Display },
            joined = mine?.Going == true, inCalendar = mine?.InCalendar == true, canManage = manage && status is "scheduled" or "live",
            canJoin = status is "scheduled" or "live" && mine?.Going != true && !(e.Capacity > 0 && going >= e.Capacity), requiresSignup = e.Capacity > 0
        };
    }

    // ---- announcing --------------------------------------------------------------------------------------------------------------

    public static async Task<EventOutcome> CreateAsync(LifeVerseDbContext db, Guid me, EventCreate r, DateTimeOffset now, CancellationToken ct)
    {
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.IslandId && x.DeletedAt == null, ct); if (island is null) return new(false, "island_not_found");
        var owner = island.OwnerUserId == me;
        if (!owner && !(island.AllowGuestEvents && island.IsPublic && !island.AccessClosed && await IslandAccess.AllowsAsync(db, island, me, now, ct))) return new(false, "not_allowed");
        var name = (r.Name ?? string.Empty).Trim(); if (name.Length < EventRules.MinName || name.Length > EventRules.MaxName) return new(false, "bad_name");
        var description = (r.Description ?? string.Empty).Trim(); if (description.Length > EventRules.MaxDescription) return new(false, "bad_description");
        var category = string.IsNullOrWhiteSpace(r.Category) ? "outros" : r.Category.Trim().ToLowerInvariant(); if (!EventRules.Categories.Any(c => c.Key == category)) return new(false, "bad_category");
        var rating = string.IsNullOrWhiteSpace(r.ContentRating) ? island.ContentRating : r.ContentRating.Trim().ToLowerInvariant(); if (!EventRules.Ratings.Contains(rating)) return new(false, "bad_rating");
        if (r.PriceCoins is < 0 or > EventRules.MaxPrice) return new(false, "bad_price");
        if (r.Capacity is < 0 or > EventRules.MaxCapacity) return new(false, "bad_capacity");
        if (r.StartsAt < now - EventRules.Grace || r.StartsAt > now + EventRules.MaxLead) return new(false, "bad_start");
        var length = r.EndsAt - r.StartsAt; if (length < EventRules.MinLength || length > EventRules.MaxLength) return new(false, "bad_length");
        var image = (r.ImageUrl ?? string.Empty).Trim(); if (image.Length > 0 && (image.Length > 1024 || !Uri.TryCreate(image, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0)) return new(false, "bad_image");
        if (r.ArrivalX is { } x && (!float.IsFinite(x) || Math.Abs(x) > island.Size) || r.ArrivalZ is { } z && (!float.IsFinite(z) || Math.Abs(z) > island.Size) || r.ArrivalX is null != r.ArrivalZ is null) return new(false, "bad_arrival");
        if (await db.DiscoveryEvents.CountAsync(e => e.OrganizerUserId == me && e.CancelledAt == null && e.EndsAt >= now, ct) >= EventRules.MaxActivePerOrganizer) return new(false, "too_many_events");
        if (await db.DiscoveryEvents.CountAsync(e => e.IslandId == island.Id && e.CancelledAt == null && e.EndsAt >= now, ct) >= EventRules.MaxActivePerIsland) return new(false, "island_busy");
        var item = new DiscoveryEvent { Id = Guid.NewGuid(), IslandId = island.Id, OrganizerUserId = me, Name = name, Description = description, StartsAt = r.StartsAt, EndsAt = r.EndsAt, Capacity = r.Capacity, IsPublic = r.IsPublic, Category = category, ImageUrl = image, PriceCoins = r.PriceCoins, ContentRating = rating, ArrivalX = r.ArrivalX, ArrivalZ = r.ArrivalZ, CreatedAt = now, UpdatedAt = now };
        db.DiscoveryEvents.Add(item); await db.SaveChangesAsync(ct); return new(true, null, item.Id);
    }

    /// <summary>The organizer or the owner of the island calls the event off: it leaves the lists and the search, and the people who signed up see it as cancelled.</summary>
    public static async Task<EventOutcome> CancelAsync(LifeVerseDbContext db, Guid me, Guid eventId, DateTimeOffset now, CancellationToken ct)
    {
        var e = await db.DiscoveryEvents.SingleOrDefaultAsync(x => x.Id == eventId, ct); if (e is null) return new(false, "not_found");
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == e.IslandId, ct); if (island is null || (e.OrganizerUserId != me && island.OwnerUserId != me)) return new(false, e.OrganizerUserId == me || island?.OwnerUserId == me ? "not_found" : "forbidden");
        if (e.CancelledAt is not null) return new(true, null, e.Id);
        if (e.EndsAt < now) return new(false, "already_ended");
        e.CancelledAt = now; e.UpdatedAt = now; await db.SaveChangesAsync(ct); return new(true, null, e.Id);
    }

    /// <summary>The owner decides whether guests may announce events on the island.</summary>
    public static async Task<EventOutcome> SetGuestEventsAsync(LifeVerseDbContext db, Guid me, Guid islandId, bool allow, DateTimeOffset now, CancellationToken ct)
    {
        var island = await db.Islands.SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null && x.OwnerUserId == me, ct); if (island is null) return new(false, "not_found");
        island.AllowGuestEvents = allow; island.UpdatedAt = now; await db.SaveChangesAsync(ct); return new(true, null, island.Id);
    }

    // ---- going ---------------------------------------------------------------------------------------------------------------------

    private static async Task<(DiscoveryEvent? Event, EventOutcome? Refusal)> OpenAsync(LifeVerseDbContext db, Guid me, Guid eventId, DateTimeOffset now, bool forJoining, CancellationToken ct)
    {
        var e = await db.DiscoveryEvents.SingleOrDefaultAsync(x => x.Id == eventId, ct); if (e is null) return (null, new(false, "not_found"));
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == e.IslandId && x.DeletedAt == null, ct); if (island is null || !await VisibleAsync(db, e, island, me, ct)) return (null, new(false, "not_found"));
        if (forJoining) { if (e.CancelledAt is not null) return (null, new(false, "cancelled")); if (e.EndsAt < now) return (null, new(false, "ended")); }
        return (e, null);
    }

    /// <summary>Signs up. Asking twice changes nothing; a full event says so.</summary>
    public static async Task<EventOutcome> JoinAsync(LifeVerseDbContext db, Guid me, Guid eventId, DateTimeOffset now, CancellationToken ct)
    {
        var (e, refusal) = await OpenAsync(db, me, eventId, now, true, ct); if (e is null) return refusal!;
        var mine = await db.EventAttendees.SingleOrDefaultAsync(a => a.EventId == eventId && a.UserId == me, ct);
        if (mine?.Going == true) return new(true, null, eventId);
        if (e.Capacity > 0 && await GoingAsync(db, eventId, ct) >= e.Capacity) return new(false, "event_full");
        if (mine is null) db.EventAttendees.Add(new EventAttendee { Id = Guid.NewGuid(), EventId = eventId, UserId = me, JoinedAt = now, Going = true }); else { mine.Going = true; mine.JoinedAt = now; }
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { /* signed up twice at the same time: it is there */ }
        return new(true, null, eventId);
    }

    public static async Task<EventOutcome> LeaveAsync(LifeVerseDbContext db, Guid me, Guid eventId, CancellationToken ct)
    {
        var mine = await db.EventAttendees.SingleOrDefaultAsync(a => a.EventId == eventId && a.UserId == me, ct); if (mine is null) return new(true, null, eventId);
        if (mine.InCalendar) mine.Going = false; else db.EventAttendees.Remove(mine);
        await db.SaveChangesAsync(ct); return new(true, null, eventId);
    }

    /// <summary>Puts the event in the personal calendar (or takes it out). It does not sign up and takes no place.</summary>
    public static async Task<EventOutcome> CalendarAsync(LifeVerseDbContext db, Guid me, Guid eventId, bool on, DateTimeOffset now, CancellationToken ct)
    {
        var mine = await db.EventAttendees.SingleOrDefaultAsync(a => a.EventId == eventId && a.UserId == me, ct);
        if (!on)
        {
            if (mine is null) return new(true, null, eventId);
            mine.InCalendar = false; if (!mine.Going) db.EventAttendees.Remove(mine); await db.SaveChangesAsync(ct); return new(true, null, eventId);
        }
        var (e, refusal) = await OpenAsync(db, me, eventId, now, true, ct); if (e is null) return refusal!;
        if (mine is null) db.EventAttendees.Add(new EventAttendee { Id = Guid.NewGuid(), EventId = eventId, UserId = me, JoinedAt = now, Going = false, InCalendar = true }); else mine.InCalendar = true;
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { }
        return new(true, null, eventId);
    }

    /// <summary>My calendar: the events I signed up for or saved that have not ended and were not called off, soonest first.</summary>
    public static async Task<List<object>> MineAsync(LifeVerseDbContext db, Guid me, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await (from a in db.EventAttendees.AsNoTracking() join e in db.DiscoveryEvents.AsNoTracking() on a.EventId equals e.Id join i in db.Islands.AsNoTracking() on e.IslandId equals i.Id
                          where a.UserId == me && (a.Going || a.InCalendar) && e.CancelledAt == null && e.EndsAt >= now && i.DeletedAt == null orderby e.StartsAt select new { Event = e, IslandName = i.Name, a.Going, a.InCalendar }).Take(50).ToListAsync(ct);
        return [.. rows.Select(r => (object)new { id = r.Event.Id, name = r.Event.Name, startsAt = r.Event.StartsAt, endsAt = r.Event.EndsAt, island = new { id = r.Event.IslandId, name = r.IslandName }, going = r.Going, inCalendar = r.InCalendar, status = StatusOf(r.Event, now) })];
    }
}
