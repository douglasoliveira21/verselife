using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Social;

/// <summary>One line of the notification center, whichever store it came from.</summary>
public sealed record CenterItem(string Source, Guid Id, string Category, string Kind, string Title, string Body, DateTimeOffset At, bool Read, Guid? GroupId, string? GroupName, IReadOnlyList<string> Actions);

public sealed record CenterReadRequest(CenterRef[]? Items, bool All = false, string? Category = null);
public sealed record CenterRef(string Source, Guid Id);

/// <summary>
/// The one place where a person reads everything the game has to tell them: group notices (announcements, events), the invitations waiting for an answer and what the real estate wrote (sales, rentals, gifts, hosting).
/// It reads the existing stores instead of keeping a copy, so nothing is told twice and the old windows keep working. Categories: <c>grupos</c>, <c>convites</c> and <c>sistema</c>.
/// An invitation counts as unread while it waits; it leaves the list by being answered (accepting or declining), not by being read.
/// </summary>
public static class NotificationCenter
{
    public static readonly string[] Categories = ["grupos", "convites", "sistema"];
    public const int MaxPage = 50;

    public static IEndpointRouteBuilder MapNotificationCenterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications/center").WithTags("Notifications").RequireAuthorization();
        group.MapGet("", async (string? category, bool? unread, string? q, int? skip, int? take, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            !TryUser(c, out var me) ? Results.Unauthorized() : Answer(await ListAsync(db, me, category, unread == true, q, skip ?? 0, take ?? 20, t.GetUtcNow(), ct)));
        group.MapPost("/read", async (CenterReadRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            !TryUser(c, out var me) ? Results.Unauthorized() : Answer(await MarkReadAsync(db, me, r, t.GetUtcNow(), ct)));
        return endpoints;
    }

    private static bool TryUser(HttpContext c, out Guid user) => Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out user);
    private static IResult Answer(CommerceResultLike r) => r.Ok ? Results.Ok(r.Data) : Results.BadRequest(new { error = r.Error });

    /// <summary>The counters that sit on the bell: unread per category and in all.</summary>
    public static async Task<(int Grupos, int Convites, int Sistema)> UnreadAsync(LifeVerseDbContext db, Guid me, CancellationToken ct)
    {
        var grupos = await db.GroupNotifications.AsNoTracking().CountAsync(n => n.UserId == me && n.ReadAt == null, ct);
        var convites = await db.GroupInvites.AsNoTracking().CountAsync(i => i.InvitedUserId == me && i.Status == "pending", ct);
        var sistema = await db.PropertyNotices.AsNoTracking().CountAsync(n => n.UserId == me && n.ReadAt == null, ct);
        return (grupos, convites, sistema);
    }

    public static async Task<CommerceResultLike> ListAsync(LifeVerseDbContext db, Guid me, string? category, bool unreadOnly, string? search, int skip, int take, DateTimeOffset now, CancellationToken ct)
    {
        category = string.IsNullOrWhiteSpace(category) || category == "todas" ? null : category.Trim().ToLowerInvariant();
        if (category is not null && !Categories.Contains(category)) return CommerceResultLike.Fail("bad_category");
        skip = Math.Max(0, skip); take = Math.Clamp(take, 1, MaxPage); var want = skip + take; search = search?.Trim();
        var items = new List<CenterItem>();

        if (category is null or "grupos")
        {
            var query = db.GroupNotifications.AsNoTracking().Where(n => n.UserId == me);
            if (unreadOnly) query = query.Where(n => n.ReadAt == null);
            if (!string.IsNullOrEmpty(search)) query = query.Where(n => n.Title.ToLower().Contains(search.ToLower()) || n.Body.ToLower().Contains(search.ToLower()));
            var rows = await query.OrderByDescending(n => n.CreatedAt).Take(want).ToListAsync(ct);
            var names = await GroupNamesAsync(db, rows.Select(n => n.GroupId), ct);
            items.AddRange(rows.Select(n => new CenterItem("group", n.Id, "grupos", n.Type, n.Title, n.Body, n.CreatedAt, n.ReadAt != null, n.GroupId, names.GetValueOrDefault(n.GroupId), [])));
        }
        if (category is null or "convites")
        {
            var rows = await db.GroupInvites.AsNoTracking().Where(i => i.InvitedUserId == me && i.Status == "pending").OrderByDescending(i => i.CreatedAt).Take(want).ToListAsync(ct);
            var names = await GroupNamesAsync(db, rows.Select(i => i.GroupId), ct);
            var inviters = rows.Select(i => i.InvitedByUserId).Distinct().ToList();
            var who = await db.Users.AsNoTracking().Where(u => inviters.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
            foreach (var i in rows)
            {
                var name = names.GetValueOrDefault(i.GroupId) ?? "grupo"; var from = who.GetValueOrDefault(i.InvitedByUserId, "alguém");
                if (!string.IsNullOrEmpty(search) && !name.Contains(search, StringComparison.OrdinalIgnoreCase) && !from.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new CenterItem("invite", i.Id, "convites", "group_invite", "Convite para o grupo", $"{name} · convidado por {from}" + (i.Message.Length > 0 ? $" — “{i.Message}”" : ""), i.CreatedAt, false, i.GroupId, name, ["accept", "decline"]));
            }
        }
        if (category is null or "sistema")
        {
            var query = db.PropertyNotices.AsNoTracking().Where(n => n.UserId == me);
            if (unreadOnly) query = query.Where(n => n.ReadAt == null);
            if (!string.IsNullOrEmpty(search)) query = query.Where(n => n.Title.ToLower().Contains(search.ToLower()) || n.Body.ToLower().Contains(search.ToLower()));
            var rows = await query.OrderByDescending(n => n.CreatedAt).Take(want).ToListAsync(ct);
            items.AddRange(rows.Select(n => new CenterItem("property", n.Id, "sistema", n.Kind, n.Title, n.Body, n.CreatedAt, n.ReadAt != null, null, null, [])));
        }

        var page = items.OrderByDescending(i => i.At).Skip(skip).Take(take).ToList();
        var unread = await UnreadAsync(db, me, ct);
        return CommerceResultLike.Done(new { unread = unread.Grupos + unread.Convites + unread.Sistema, unreadByCategory = new { grupos = unread.Grupos, convites = unread.Convites, sistema = unread.Sistema }, skip, take, hasMore = items.Count > skip + take, items = page });
    }

    private static async Task<Dictionary<Guid, string>> GroupNamesAsync(LifeVerseDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList(); if (list.Count == 0) return [];
        return await db.SocialGroups.AsNoTracking().Where(g => list.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
    }

    /// <summary>Marks notifications as read: the ones named, or everything (of one category when given). Invitations are not touched: they leave by being answered.</summary>
    public static async Task<CommerceResultLike> MarkReadAsync(LifeVerseDbContext db, Guid me, CenterReadRequest request, DateTimeOffset now, CancellationToken ct)
    {
        var category = string.IsNullOrWhiteSpace(request.Category) || request.Category == "todas" ? null : request.Category.Trim().ToLowerInvariant();
        if (category is not null && !Categories.Contains(category)) return CommerceResultLike.Fail("bad_category");
        var marked = 0;
        var groupIds = request.All ? null : request.Items?.Where(r => r.Source == "group").Select(r => r.Id).ToList() ?? [];
        var propertyIds = request.All ? null : request.Items?.Where(r => r.Source == "property").Select(r => r.Id).ToList() ?? [];
        if (category is null or "grupos")
            foreach (var n in await db.GroupNotifications.Where(n => n.UserId == me && n.ReadAt == null && (groupIds == null || groupIds.Contains(n.Id))).ToListAsync(ct)) { n.ReadAt = now; marked++; }
        if (category is null or "sistema")
            foreach (var n in await db.PropertyNotices.Where(n => n.UserId == me && n.ReadAt == null && (propertyIds == null || propertyIds.Contains(n.Id))).ToListAsync(ct)) { n.ReadAt = now; marked++; }
        await db.SaveChangesAsync(ct);
        var unread = await UnreadAsync(db, me, ct);
        return CommerceResultLike.Done(new { marked, unread = unread.Grupos + unread.Convites + unread.Sistema, unreadByCategory = new { grupos = unread.Grupos, convites = unread.Convites, sistema = unread.Sistema } });
    }
}

/// <summary>A result with data or an error code (the notification center has no money to protect, so it needs nothing heavier).</summary>
public sealed record CommerceResultLike(bool Ok, object? Data, string? Error)
{
    public static CommerceResultLike Done(object? data) => new(true, data, null);
    public static CommerceResultLike Fail(string error) => new(false, null, error);
}
