using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Platform;

public sealed record PanelBlockRequest(string Reason, bool Ban = false);
public sealed record PanelReasonRequest(string Reason);
public sealed record PanelPasswordRequest(string NewPassword, string Reason);

/// <summary>
/// The administration panel (/admin): the numbers of the whole game, the people (search, details, blocking, a new password, sessions), what they own (inventory, friends, groups, wallet) and the logs.
/// Every call checks the administrator (id in the list + MFA token) again; every change asks for a reason and leaves an audit record; nothing here ever returns or logs a password or a hash.
/// </summary>
public static class AdminPanel
{
    public const int MaxPage = 100;

    public static IEndpointRouteBuilder MapAdminPanel(this IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup("/api/v1/admin/panel").WithTags("Administration panel").RequireAuthorization();
        g.MapGet("/overview", OverviewAsync);
        g.MapGet("/users", UsersAsync); g.MapGet("/users/{id:guid}", UserAsync);
        g.MapPost("/users/{id:guid}/block", BlockAsync); g.MapPost("/users/{id:guid}/unblock", UnblockAsync);
        g.MapPost("/users/{id:guid}/password", PasswordAsync); g.MapPost("/users/{id:guid}/revoke-sessions", RevokeSessionsAsync);
        g.MapGet("/users/{id:guid}/inventory", InventoryAsync); g.MapGet("/users/{id:guid}/friends", FriendsAsync); g.MapGet("/users/{id:guid}/groups", UserGroupsAsync);
        g.MapGet("/groups", GroupsAsync); g.MapGet("/groups/{id:guid}", GroupAsync);
        g.MapGet("/logs", LogsAsync);
        return endpoints;
    }

    private static bool IsAdmin(HttpContext c, out Guid admin) => AdminEndpoints.Admin(c, out admin);
    private static int Take(int? size, int fallback = 25) => Math.Clamp(size ?? fallback, 1, MaxPage);
    private static int Skip(int? page, int size) => Math.Max(0, ((page ?? 1) - 1) * size);
    private static string Like(string q) => q.Trim().ToLowerInvariant();

    // ---- dashboard -----------------------------------------------------------------------------------------------------------------------

    private static async Task<List<object>> PerDay<T>(IQueryable<T> source, System.Linq.Expressions.Expression<Func<T, DateTimeOffset>> when, DateTimeOffset now, int days, CancellationToken ct)
    {
        var start = new DateTimeOffset(now.UtcDateTime.Date.AddDays(-(days - 1)), TimeSpan.Zero);
        var dates = await source.Where(Between(when, start)).Select(when).ToListAsync(ct);
        var counts = dates.GroupBy(d => d.UtcDateTime.Date).ToDictionary(x => x.Key, x => x.Count());
        return [.. Enumerable.Range(0, days).Select(i => start.UtcDateTime.Date.AddDays(i)).Select(d => (object)new { day = d.ToString("yyyy-MM-dd"), count = counts.GetValueOrDefault(d) })];
    }

    private static System.Linq.Expressions.Expression<Func<T, bool>> Between<T>(System.Linq.Expressions.Expression<Func<T, DateTimeOffset>> when, DateTimeOffset start)
    {
        var parameter = when.Parameters[0];
        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(System.Linq.Expressions.Expression.GreaterThanOrEqual(when.Body, System.Linq.Expressions.Expression.Constant(start)), parameter);
    }

    internal static async Task<object> OverviewDataAsync(LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var d1 = now.AddDays(-1); var d7 = now.AddDays(-7); var d30 = now.AddDays(-30);
        var users = db.Users.AsNoTracking().Where(x => x.DeletedAt == null);
        var topBalances = await (from w in db.Wallets.AsNoTracking() join u in db.Users.AsNoTracking() on w.UserId equals u.Id orderby w.AvailableCoins descending select new { u.Id, u.Username, coins = w.AvailableCoins }).Take(5).ToListAsync(ct);
        var recent = await (from a in db.AdminAudits.AsNoTracking() join u in db.Users.AsNoTracking() on a.AdminUserId equals u.Id into admins from u in admins.DefaultIfEmpty() orderby a.CreatedAt descending select new { a.Action, a.TargetType, a.TargetId, a.CreatedAt, admin = u == null ? null : u.Username }).Take(10).ToListAsync(ct);
        return new
        {
            generatedAt = now,
            users = new
            {
                total = await users.CountAsync(ct), active = await users.CountAsync(x => x.Status == "active", ct), blocked = await users.CountAsync(x => x.Status != "active", ct),
                new24h = await users.CountAsync(x => x.CreatedAt >= d1, ct), new7d = await users.CountAsync(x => x.CreatedAt >= d7, ct), new30d = await users.CountAsync(x => x.CreatedAt >= d30, ct),
                online = await users.CountAsync(x => x.PresenceStatus != "offline" && x.PresenceUpdatedAt >= now.AddMinutes(-5), ct),
                activeSessions = await db.Sessions.CountAsync(x => x.RevokedAt == null && x.ExpiresAt > now, ct), logins24h = await db.Sessions.CountAsync(x => x.CreatedAt >= d1, ct)
            },
            islands = new { total = await db.Islands.CountAsync(x => x.DeletedAt == null, ct), running = await db.Islands.CountAsync(x => x.DeletedAt == null && x.RuntimeState == "running", ct), servers = await db.ServerInstances.CountAsync(x => x.StoppedAt == null && x.LastHeartbeatAt > now.AddSeconds(-45), ct) },
            content = new { objects = await db.BuildObjects.CountAsync(x => x.DeletedAt == null, ct), assets = await db.Assets.CountAsync(x => x.DeletedAt == null, ct), inventoryItems = await db.InventoryItems.CountAsync(x => x.DeletedAt == null, ct), products = await db.MarketplaceProducts.CountAsync(x => x.Status == "active", ct) },
            social = new { groups = await db.SocialGroups.CountAsync(ct), memberships = await db.GroupMembers.CountAsync(ct), friendships = await db.Friendships.CountAsync(x => x.Status == "accepted", ct), openReports = await db.SocialReports.CountAsync(x => x.Status == "open", ct) },
            economy = new
            {
                coinsInWallets = await db.Wallets.SumAsync(x => (long?)x.AvailableCoins, ct) ?? 0, wallets = await db.Wallets.CountAsync(ct), blockedWallets = await db.Wallets.CountAsync(x => x.IsBlocked, ct),
                transactions24h = await db.LedgerTransactions.CountAsync(x => x.CreatedAt >= d1, ct), volume24h = await db.LedgerTransactions.Where(x => x.CreatedAt >= d1).SumAsync(x => (long?)x.Amount, ct) ?? 0, pendingCoinOrders = await db.CoinOrders.CountAsync(x => x.Status == "pending", ct)
            },
            charts = new { signups = await PerDay(users, x => x.CreatedAt, now, 14, ct), logins = await PerDay(db.Sessions.AsNoTracking(), x => x.CreatedAt, now, 14, ct), transactions = await PerDay(db.LedgerTransactions.AsNoTracking(), x => x.CreatedAt, now, 14, ct) },
            topBalances, recentActions = recent
        };
    }

    internal static async Task<IResult> OverviewAsync(HttpContext c, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => !IsAdmin(c, out _) ? Results.Forbid() : Results.Ok(await OverviewDataAsync(db, clock.GetUtcNow(), ct));

    // ---- people ----------------------------------------------------------------------------------------------------------------------------

    internal static async Task<object> UsersDataAsync(LifeVerseDbContext db, string? q, string? status, string? sort, int? page, int? pageSize, CancellationToken ct)
    {
        var size = Take(pageSize); var query = db.Users.AsNoTracking().Where(x => x.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(q)) { var t = Like(q); query = query.Where(x => x.Username.ToLower().Contains(t) || x.Email.ToLower().Contains(t)); }
        if (status is "active" or "suspended" or "banned") query = query.Where(x => x.Status == status);
        query = sort switch { "newest" => query.OrderByDescending(x => x.CreatedAt), "oldest" => query.OrderBy(x => x.CreatedAt), _ => query.OrderBy(x => x.Username) };
        var total = await query.CountAsync(ct);
        var rows = await query.Skip(Skip(page, size)).Take(size).Select(x => new { x.Id, x.Username, x.Email, x.Status, x.CreatedAt, x.PresenceStatus, x.PresenceUpdatedAt }).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var balances = await db.Wallets.AsNoTracking().Where(w => ids.Contains(w.UserId)).ToDictionaryAsync(w => w.UserId, w => new { w.AvailableCoins, w.IsBlocked }, ct);
        var logins = (await db.Sessions.AsNoTracking().Where(s => ids.Contains(s.UserId)).GroupBy(s => s.UserId).Select(g => new { g.Key, Last = g.Max(s => s.CreatedAt) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Last);
        var groups = (await db.GroupMembers.AsNoTracking().Where(m => ids.Contains(m.UserId)).GroupBy(m => m.UserId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Count);
        return new
        {
            total, page = page ?? 1, pageSize = size,
            items = rows.Select(r => new { r.Id, r.Username, r.Email, r.Status, r.CreatedAt, lastLoginAt = logins.TryGetValue(r.Id, out var l) ? (DateTimeOffset?)l : null, balance = balances.TryGetValue(r.Id, out var b) ? (long?)b.AvailableCoins : null, walletBlocked = balances.TryGetValue(r.Id, out var w) && w.IsBlocked, groups = groups.GetValueOrDefault(r.Id) })
        };
    }

    internal static async Task<IResult> UsersAsync(string? q, string? status, string? sort, int? page, int? pageSize, HttpContext c, LifeVerseDbContext db, CancellationToken ct) => !IsAdmin(c, out _) ? Results.Forbid() : Results.Ok(await UsersDataAsync(db, q, status, sort, page, pageSize, ct));

    internal static async Task<IResult> UserAsync(Guid id, HttpContext c, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!IsAdmin(c, out _)) return Results.Forbid();
        var u = await db.Users.AsNoTracking().Where(x => x.Id == id && x.DeletedAt == null).Select(x => new { x.Id, x.Username, x.Email, x.Status, x.CreatedAt, x.UpdatedAt, x.Bio, x.PresenceStatus, x.PresenceUpdatedAt }).SingleOrDefaultAsync(ct);
        if (u is null) return Results.NotFound(); var now = clock.GetUtcNow();
        var avatar = await db.Avatars.AsNoTracking().Where(a => a.UserId == id).Select(a => new { a.DisplayName, a.BaseModel }).SingleOrDefaultAsync(ct);
        var wallet = await db.Wallets.AsNoTracking().Where(w => w.UserId == id).Select(w => new { w.AvailableCoins, w.IsBlocked, w.DailyTransferLimit, w.UpdatedAt }).SingleOrDefaultAsync(ct);
        var sessions = await db.Sessions.AsNoTracking().Where(s => s.UserId == id).OrderByDescending(s => s.CreatedAt).Take(10).Select(s => new { s.Id, s.CreatedAt, s.ExpiresAt, s.RevokedAt, s.IpAddress, s.UserAgent, active = s.RevokedAt == null && s.ExpiresAt > now }).ToListAsync(ct);
        var history = await (from a in db.AdminAudits.AsNoTracking() where a.TargetId == id orderby a.CreatedAt descending select new { a.Action, a.CreatedAt, a.MetadataJson }).Take(10).ToListAsync(ct);
        return Results.Ok(new
        {
            user = u, avatar, wallet, sessions, history,
            counts = new
            {
                inventory = await db.InventoryItems.CountAsync(x => x.OwnerUserId == id && x.DeletedAt == null, ct), friends = await db.Friendships.CountAsync(x => x.Status == "accepted" && (x.UserId == id || x.FriendUserId == id), ct),
                groups = await db.GroupMembers.CountAsync(x => x.UserId == id, ct), objects = await db.BuildObjects.CountAsync(x => x.OwnerUserId == id && x.DeletedAt == null, ct), islands = await db.Islands.CountAsync(x => x.OwnerUserId == id && x.DeletedAt == null, ct)
            }
        });
    }

    private static bool ReasonOk(string? reason) => reason is { } r && r.Trim().Length is >= 3 and <= 200;

    internal static async Task<(int Status, object Body)> SetStatusAsync(LifeVerseDbContext db, Guid admin, Guid id, string status, string reason, DateTimeOffset now, CancellationToken ct)
    {
        if (!ReasonOk(reason)) return (400, new { error = "reason_required" });
        if (id == admin) return (409, new { error = "cannot_block_yourself" });
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct); if (user is null) return (404, new { error = "not_found" });
        user.Status = status; user.UpdatedAt = now; var revoked = 0;
        if (status != "active") foreach (var s in await db.Sessions.Where(x => x.UserId == id && x.RevokedAt == null).ToListAsync(ct)) { s.RevokedAt = now; revoked++; }       // a blocked person is signed out at once
        db.AdminAudits.Add(new AdminAudit { Id = Guid.NewGuid(), AdminUserId = admin, Action = status == "active" ? "user_unblock" : status == "banned" ? "user_ban" : "user_block", TargetType = "user", TargetId = id, MetadataJson = JsonSerializer.Serialize(new { reason = reason.Trim(), revokedSessions = revoked }), CreatedAt = now });
        await db.SaveChangesAsync(ct); return (200, new { status = user.Status, revokedSessions = revoked });
    }

    internal static async Task<IResult> BlockAsync(Guid id, [FromBody] PanelBlockRequest request, HttpContext c, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    { if (!IsAdmin(c, out var admin)) return Results.Forbid(); var (s, body) = await SetStatusAsync(db, admin, id, request.Ban ? "banned" : "suspended", request.Reason, clock.GetUtcNow(), ct); return Results.Json(body, statusCode: s); }

    internal static async Task<IResult> UnblockAsync(Guid id, [FromBody] PanelReasonRequest request, HttpContext c, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    { if (!IsAdmin(c, out var admin)) return Results.Forbid(); var (s, body) = await SetStatusAsync(db, admin, id, "active", request.Reason, clock.GetUtcNow(), ct); return Results.Json(body, statusCode: s); }

    internal static async Task<(int Status, object Body)> SetPasswordAsync(LifeVerseDbContext db, Guid admin, Guid id, string? newPassword, string reason, DateTimeOffset now, CancellationToken ct)
    {
        if (!ReasonOk(reason)) return (400, new { error = "reason_required" });
        if (AuthValidation.PasswordProblem(newPassword) is { } problem) return (400, new { error = "weak_password", message = problem });
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct); if (user is null) return (404, new { error = "not_found" });
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, newPassword!); user.UpdatedAt = now; var revoked = 0;
        foreach (var s in await db.Sessions.Where(x => x.UserId == id && x.RevokedAt == null).ToListAsync(ct)) { s.RevokedAt = now; revoked++; }                // the old password may be known by somebody else: every session ends
        db.AdminAudits.Add(new AdminAudit { Id = Guid.NewGuid(), AdminUserId = admin, Action = "user_password_reset", TargetType = "user", TargetId = id, MetadataJson = JsonSerializer.Serialize(new { reason = reason.Trim(), revokedSessions = revoked }), CreatedAt = now });   // never the password
        await db.SaveChangesAsync(ct); return (200, new { changed = true, revokedSessions = revoked });
    }

    internal static async Task<IResult> PasswordAsync(Guid id, [FromBody] PanelPasswordRequest request, HttpContext c, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    { if (!IsAdmin(c, out var admin)) return Results.Forbid(); var (s, body) = await SetPasswordAsync(db, admin, id, request.NewPassword, request.Reason, clock.GetUtcNow(), ct); return Results.Json(body, statusCode: s); }

    internal static async Task<IResult> RevokeSessionsAsync(Guid id, [FromBody] PanelReasonRequest request, HttpContext c, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!IsAdmin(c, out var admin)) return Results.Forbid(); if (!ReasonOk(request.Reason)) return Results.BadRequest(new { error = "reason_required" });
        var now = clock.GetUtcNow(); var sessions = await db.Sessions.Where(x => x.UserId == id && x.RevokedAt == null).ToListAsync(ct); foreach (var s in sessions) s.RevokedAt = now;
        db.AdminAudits.Add(new AdminAudit { Id = Guid.NewGuid(), AdminUserId = admin, Action = "user_sessions_revoked", TargetType = "user", TargetId = id, MetadataJson = JsonSerializer.Serialize(new { reason = request.Reason.Trim(), revokedSessions = sessions.Count }), CreatedAt = now });
        await db.SaveChangesAsync(ct); return Results.Ok(new { revoked = sessions.Count });
    }

    // ---- what a person has -----------------------------------------------------------------------------------------------------------------

    internal static async Task<IResult> InventoryAsync(Guid id, string? q, int? page, int? pageSize, HttpContext c, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!IsAdmin(c, out _)) return Results.Forbid(); var size = Take(pageSize, 50);
        var query = from i in db.InventoryItems.AsNoTracking() join a in db.Assets.AsNoTracking() on i.AssetId equals a.Id into assets from a in assets.DefaultIfEmpty() where i.OwnerUserId == id && i.DeletedAt == null select new { i.Id, i.DisplayName, i.AssetId, category = a == null ? null : a.Category, i.OwnerRights, i.NextOwnerRights, i.IsFavorite, i.CreatedAt };
        if (!string.IsNullOrWhiteSpace(q)) { var t = Like(q); query = query.Where(x => x.DisplayName.ToLower().Contains(t)); }
        var byCategory = await query.GroupBy(x => x.category).Select(g => new { category = g.Key, count = g.Count() }).ToListAsync(ct);
        return Results.Ok(new { total = await query.CountAsync(ct), page = page ?? 1, pageSize = size, byCategory, items = await query.OrderByDescending(x => x.CreatedAt).Skip(Skip(page, size)).Take(size).ToListAsync(ct) });
    }

    internal static async Task<IResult> FriendsAsync(Guid id, HttpContext c, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!IsAdmin(c, out _)) return Results.Forbid();
        var links = await db.Friendships.AsNoTracking().Where(x => x.UserId == id || x.FriendUserId == id).ToListAsync(ct);
        var otherIds = links.Select(l => l.UserId == id ? l.FriendUserId : l.UserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => otherIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        var items = links.Select(l => { var other = l.UserId == id ? l.FriendUserId : l.UserId; return new { userId = other, username = names.GetValueOrDefault(other, "?"), l.Status, direction = l.Status == "accepted" ? "friend" : l.UserId == id ? "sent" : "received", since = l.UpdatedAt }; }).OrderBy(x => x.Status == "accepted" ? 0 : 1).ThenBy(x => x.username).ToList();
        var blocked = await (from b in db.UserBlocks.AsNoTracking() join u in db.Users.AsNoTracking() on b.BlockedUserId equals u.Id where b.UserId == id select new { userId = u.Id, username = u.Username }).ToListAsync(ct);
        return Results.Ok(new { friends = items.Where(x => x.Status == "accepted"), pending = items.Where(x => x.Status != "accepted"), blocked });
    }

    internal static async Task<IResult> UserGroupsAsync(Guid id, HttpContext c, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!IsAdmin(c, out _)) return Results.Forbid();
        return Results.Ok(await (from m in db.GroupMembers.AsNoTracking() join g in db.SocialGroups.AsNoTracking() on m.GroupId equals g.Id where m.UserId == id orderby g.Name select new { groupId = g.Id, g.Name, g.Tag, m.Role, g.Privacy }).ToListAsync(ct));
    }

    internal static async Task<IResult> GroupsAsync(string? q, int? page, int? pageSize, HttpContext c, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!IsAdmin(c, out _)) return Results.Forbid(); var size = Take(pageSize);
        var query = db.SocialGroups.AsNoTracking().AsQueryable(); if (!string.IsNullOrWhiteSpace(q)) { var t = Like(q); query = query.Where(g => g.Name.ToLower().Contains(t) || (g.Tag != null && g.Tag.ToLower().Contains(t))); }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(g => g.Name).Skip(Skip(page, size)).Take(size).Select(g => new { g.Id, g.Name, g.Tag, g.Privacy, g.JoinPolicy, g.OwnerUserId, g.CreatedAt }).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList(); var owners = rows.Select(r => r.OwnerUserId).Distinct().ToList();
        var members = (await db.GroupMembers.AsNoTracking().Where(m => ids.Contains(m.GroupId)).GroupBy(m => m.GroupId).Select(x => new { x.Key, Count = x.Count() }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Count);
        var ownerNames = await db.Users.AsNoTracking().Where(u => owners.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        return Results.Ok(new { total, page = page ?? 1, pageSize = size, items = rows.Select(r => new { r.Id, r.Name, r.Tag, r.Privacy, r.JoinPolicy, r.CreatedAt, owner = ownerNames.GetValueOrDefault(r.OwnerUserId, "?"), ownerId = r.OwnerUserId, members = members.GetValueOrDefault(r.Id) }) });
    }

    internal static async Task<IResult> GroupAsync(Guid id, HttpContext c, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!IsAdmin(c, out _)) return Results.Forbid();
        var g = await db.SocialGroups.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Id, x.Name, x.Tag, x.Description, x.Privacy, x.JoinPolicy, x.OwnerUserId, x.CreatedAt }).SingleOrDefaultAsync(ct); if (g is null) return Results.NotFound();
        var members = await (from m in db.GroupMembers.AsNoTracking() join u in db.Users.AsNoTracking() on m.UserId equals u.Id where m.GroupId == id orderby u.Username select new { userId = u.Id, u.Username, m.Role }).ToListAsync(ct);
        return Results.Ok(new { group = g, members });
    }

    // ---- logs --------------------------------------------------------------------------------------------------------------------------------

    internal static async Task<IResult> LogsAsync(string? kind, Guid? userId, string? q, int? days, int? page, int? pageSize, HttpContext c, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!IsAdmin(c, out _)) return Results.Forbid(); var size = Take(pageSize, 50); var since = days is { } d ? clock.GetUtcNow().AddDays(-Math.Clamp(d, 1, 365)) : DateTimeOffset.MinValue;
        switch (kind)
        {
            case "logins":
            {
                var query = from s in db.Sessions.AsNoTracking() join u in db.Users.AsNoTracking() on s.UserId equals u.Id where s.CreatedAt >= since && (userId == null || s.UserId == userId) select new { s.Id, userId = u.Id, u.Username, s.CreatedAt, s.IpAddress, s.UserAgent, s.RevokedAt };
                if (!string.IsNullOrWhiteSpace(q)) { var t = Like(q); query = query.Where(x => x.Username.ToLower().Contains(t) || (x.IpAddress != null && x.IpAddress.Contains(t))); }
                return Results.Ok(new { kind, total = await query.CountAsync(ct), items = await query.OrderByDescending(x => x.CreatedAt).Skip(Skip(page, size)).Take(size).ToListAsync(ct) });
            }
            case "economy":
            {
                var query = db.LedgerTransactions.AsNoTracking().Where(t => t.CreatedAt >= since && (userId == null || t.InitiatorUserId == userId || t.CounterpartyUserId == userId));
                var total = await query.CountAsync(ct); var rows = await query.OrderByDescending(t => t.CreatedAt).Skip(Skip(page, size)).Take(size).ToListAsync(ct);
                var ids = rows.SelectMany(r => new[] { r.InitiatorUserId, r.CounterpartyUserId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
                var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
                return Results.Ok(new { kind, total, items = rows.Select(r => new { r.Id, r.Type, r.Status, r.Amount, r.CreatedAt, from = r.InitiatorUserId is { } a ? names.GetValueOrDefault(a, "?") : null, to = r.CounterpartyUserId is { } b ? names.GetValueOrDefault(b, "?") : null }) });
            }
            default:
            {
                var query = from a in db.AdminAudits.AsNoTracking() join u in db.Users.AsNoTracking() on a.AdminUserId equals u.Id into admins from u in admins.DefaultIfEmpty() where a.CreatedAt >= since && (userId == null || a.TargetId == userId || a.AdminUserId == userId) select new { a.Id, a.Action, a.TargetType, a.TargetId, a.MetadataJson, a.CreatedAt, admin = u == null ? null : u.Username };
                if (!string.IsNullOrWhiteSpace(q)) { var t = Like(q); query = query.Where(x => x.Action.ToLower().Contains(t) || (x.admin != null && x.admin.ToLower().Contains(t))); }
                return Results.Ok(new { kind = "admin", total = await query.CountAsync(ct), items = await query.OrderByDescending(x => x.CreatedAt).Skip(Skip(page, size)).Take(size).ToListAsync(ct) });
            }
        }
    }
}
