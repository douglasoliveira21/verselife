using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Social;

public sealed record PersonResult(Guid Id, string Username, string DisplayName, string Relation);
public sealed record FriendRequestItem(Guid UserId, string Username, DateTimeOffset At, string Direction);
public sealed record GroupItem(Guid Id, string Name, string Description, string Role, int Members);
public sealed record GroupInviteItem(Guid InviteId, Guid GroupId, string GroupName, Guid FromUserId, string FromUsername, DateTimeOffset At);
public sealed record SocialSummary(int FriendRequests, int GroupInvites, int UnreadMessages);

/// Reads and rules behind the People window: finding players, pending friend requests, groups and invites.
/// Kept apart from the HTTP layer so each rule can be tested against a database directly.
public static class PeopleQueries
{
    public const int MinSearchLength = 2;
    public const int MaxResults = 20;

    public static async Task<IReadOnlyList<PersonResult>> SearchAsync(LifeVerseDbContext db, Guid me, string? query, CancellationToken ct)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length < MinSearchLength || text.Length > 64) return [];
        var needle = text.ToUpperInvariant();
        var blocked = db.UserBlocks.Where(b => b.UserId == me).Select(b => b.BlockedUserId)
            .Union(db.UserBlocks.Where(b => b.BlockedUserId == me).Select(b => b.UserId));
        var users = await db.Users.AsNoTracking().Include(u => u.Avatar)
            .Where(u => u.DeletedAt == null && u.Status == "active" && u.Id != me && u.NormalizedUsername.Contains(needle) && !blocked.Contains(u.Id))
            .OrderBy(u => u.NormalizedUsername.StartsWith(needle) ? 0 : 1).ThenBy(u => u.NormalizedUsername)
            .Take(MaxResults).ToListAsync(ct);
        var ids = users.Select(u => u.Id).ToList();
        var links = await db.Friendships.AsNoTracking().Where(f => (f.UserId == me && ids.Contains(f.FriendUserId)) || (f.FriendUserId == me && ids.Contains(f.UserId))).ToListAsync(ct);
        return users.Select(u =>
        {
            var link = links.FirstOrDefault(f => (f.UserId == me && f.FriendUserId == u.Id) || (f.UserId == u.Id && f.FriendUserId == me));
            var relation = link is null ? "none" : link.Status == "accepted" ? "friend" : link.UserId == me ? "pending_out" : "pending_in";
            return new PersonResult(u.Id, u.Username, u.Avatar?.DisplayName ?? u.Username, relation);
        }).ToList();
    }

    public static async Task<IReadOnlyList<FriendRequestItem>> FriendRequestsAsync(LifeVerseDbContext db, Guid me, CancellationToken ct)
    {
        var rows = await db.Friendships.AsNoTracking().Where(f => f.Status == "pending" && (f.UserId == me || f.FriendUserId == me)).OrderByDescending(f => f.CreatedAt).Take(100).ToListAsync(ct);
        var otherIds = rows.Select(f => f.UserId == me ? f.FriendUserId : f.UserId).ToList();
        var names = await db.Users.AsNoTracking().Where(u => otherIds.Contains(u.Id) && u.DeletedAt == null).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        return rows.Where(f => names.ContainsKey(f.UserId == me ? f.FriendUserId : f.UserId))
            .Select(f => { var other = f.UserId == me ? f.FriendUserId : f.UserId; return new FriendRequestItem(other, names[other], f.CreatedAt, f.UserId == me ? "out" : "in"); }).ToList();
    }

    /// Declines an incoming request or cancels one the caller sent. Returns false when there is nothing pending.
    public static async Task<bool> DeclineFriendAsync(LifeVerseDbContext db, Guid me, Guid other, CancellationToken ct)
    {
        var rows = await db.Friendships.Where(f => f.Status == "pending" && ((f.UserId == me && f.FriendUserId == other) || (f.UserId == other && f.FriendUserId == me))).ToListAsync(ct);
        if (rows.Count == 0) return false;
        db.Friendships.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public static async Task<IReadOnlyList<GroupItem>> MyGroupsAsync(LifeVerseDbContext db, Guid me, CancellationToken ct)
    {
        var mine = await db.GroupMembers.AsNoTracking().Where(m => m.UserId == me).ToListAsync(ct);
        var ids = mine.Select(m => m.GroupId).ToList();
        var groups = await db.SocialGroups.AsNoTracking().Where(g => ids.Contains(g.Id) && g.DeletedAt == null).OrderBy(g => g.Name).ToListAsync(ct);
        var counts = await db.GroupMembers.AsNoTracking().Where(m => ids.Contains(m.GroupId)).GroupBy(m => m.GroupId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        return groups.Select(g => new GroupItem(g.Id, g.Name, g.Description, mine.First(m => m.GroupId == g.Id).Role, counts.GetValueOrDefault(g.Id))).ToList();
    }

    public static async Task<IReadOnlyList<GroupInviteItem>> GroupInvitesAsync(LifeVerseDbContext db, Guid me, CancellationToken ct)
    {
        var invites = await db.GroupInvites.AsNoTracking().Where(i => i.InvitedUserId == me && i.Status == "pending").OrderByDescending(i => i.CreatedAt).Take(100).ToListAsync(ct);
        var groupIds = invites.Select(i => i.GroupId).ToList(); var fromIds = invites.Select(i => i.InvitedByUserId).ToList();
        var groups = await db.SocialGroups.AsNoTracking().Where(g => groupIds.Contains(g.Id) && g.DeletedAt == null).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var from = await db.Users.AsNoTracking().Where(u => fromIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        return invites.Where(i => groups.ContainsKey(i.GroupId)).Select(i => new GroupInviteItem(i.Id, i.GroupId, groups[i.GroupId], i.InvitedByUserId, from.GetValueOrDefault(i.InvitedByUserId, "?"), i.CreatedAt)).ToList();
    }

    /// Accepting makes the caller a member (once); declining just closes the invite. Null means the invite is not theirs or already answered.
    public static async Task<bool?> AnswerInviteAsync(LifeVerseDbContext db, Guid me, Guid inviteId, bool accept, TimeProvider clock, CancellationToken ct)
    {
        var invite = await db.GroupInvites.SingleOrDefaultAsync(i => i.Id == inviteId && i.InvitedUserId == me && i.Status == "pending", ct);
        if (invite is null) return null;
        invite.Status = accept ? "accepted" : "declined";
        if (accept && !await db.GroupMembers.AnyAsync(m => m.GroupId == invite.GroupId && m.UserId == me, ct)
            && await db.SocialGroups.AnyAsync(g => g.Id == invite.GroupId && g.DeletedAt == null, ct))
            db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = invite.GroupId, UserId = me, Role = "member", JoinedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public enum LeaveResult { Left, NotMember, OwnerCannotLeave }

    public static async Task<LeaveResult> LeaveGroupAsync(LifeVerseDbContext db, Guid me, Guid groupId, CancellationToken ct)
    {
        var member = await db.GroupMembers.SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == me, ct);
        if (member is null) return LeaveResult.NotMember;
        if (member.Role == "owner") return LeaveResult.OwnerCannotLeave;
        db.GroupMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        return LeaveResult.Left;
    }

    public static async Task<SocialSummary> SummaryAsync(LifeVerseDbContext db, Guid me, CancellationToken ct) => new(
        await db.Friendships.CountAsync(f => f.FriendUserId == me && f.Status == "pending", ct),
        await db.GroupInvites.CountAsync(i => i.InvitedUserId == me && i.Status == "pending", ct),
        await db.SocialMessages.CountAsync(m => m.RecipientUserId == me && m.ReadAt == null && m.DeletedAt == null, ct));
}

public static class PeopleEndpoints
{
    public static IEndpointRouteBuilder MapPeopleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var social = endpoints.MapGroup("/api/v1/social").WithTags("People").RequireAuthorization();
        social.MapGet("/people", async (string? q, HttpContext context, LifeVerseDbContext db, CancellationToken ct) =>
            User(context, out var me) ? Results.Ok(await PeopleQueries.SearchAsync(db, me, q, ct)) : Results.Unauthorized());
        social.MapGet("/summary", async (HttpContext context, LifeVerseDbContext db, CancellationToken ct) =>
            User(context, out var me) ? Results.Ok(await PeopleQueries.SummaryAsync(db, me, ct)) : Results.Unauthorized());
        social.MapGet("/friends/requests", async (HttpContext context, LifeVerseDbContext db, CancellationToken ct) =>
            User(context, out var me) ? Results.Ok(await PeopleQueries.FriendRequestsAsync(db, me, ct)) : Results.Unauthorized());
        social.MapPost("/friends/{userId:guid}/decline", async (Guid userId, HttpContext context, LifeVerseDbContext db, CancellationToken ct) =>
            !User(context, out var me) ? Results.Unauthorized() : await PeopleQueries.DeclineFriendAsync(db, me, userId, ct) ? Results.NoContent() : Results.NotFound());
        social.MapGet("/groups", async (HttpContext context, LifeVerseDbContext db, CancellationToken ct) =>
            User(context, out var me) ? Results.Ok(await PeopleQueries.MyGroupsAsync(db, me, ct)) : Results.Unauthorized());
        social.MapGet("/groups/invites", async (HttpContext context, LifeVerseDbContext db, CancellationToken ct) =>
            User(context, out var me) ? Results.Ok(await PeopleQueries.GroupInvitesAsync(db, me, ct)) : Results.Unauthorized());
        social.MapPost("/groups/invites/{inviteId:guid}/accept", (Guid inviteId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => AnswerAsync(inviteId, true, context, db, clock, ct));
        social.MapPost("/groups/invites/{inviteId:guid}/decline", (Guid inviteId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => AnswerAsync(inviteId, false, context, db, clock, ct));
        social.MapPost("/groups/{groupId:guid}/leave", async (Guid groupId, HttpContext context, LifeVerseDbContext db, CancellationToken ct) =>
        {
            if (!User(context, out var me)) return Results.Unauthorized();
            return await PeopleQueries.LeaveGroupAsync(db, me, groupId, ct) switch
            {
                PeopleQueries.LeaveResult.Left => Results.NoContent(),
                PeopleQueries.LeaveResult.OwnerCannotLeave => Results.Conflict(new { error = "The owner cannot leave the group." }),
                _ => Results.NotFound()
            };
        });
        return endpoints;
    }

    private static async Task<IResult> AnswerAsync(Guid inviteId, bool accept, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) =>
        !User(context, out var me) ? Results.Unauthorized() : await PeopleQueries.AnswerInviteAsync(db, me, inviteId, accept, clock, ct) is null ? Results.NotFound() : Results.NoContent();

    private static bool User(HttpContext context, out Guid id) => Guid.TryParse(context.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out id);
}
