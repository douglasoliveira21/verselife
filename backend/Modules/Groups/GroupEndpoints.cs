using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Social;
using LifeVerse.Shared.Groups;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Groups;

public sealed record JoinRequest(string? Message);
public sealed record InviteRequest(Guid? UserId, string? Username);
public sealed record ReasonRequest(string? Reason);
public sealed record BanRequest(string? Reason, int? Days);
public sealed record MuteRequest(int Minutes, string? Reason);
public sealed record AssignRoleRequest(Guid RoleId);
public sealed record ConfirmRequest(Guid? UserId, string? Confirm);

/// <summary>The HTTP face of <see cref="GroupService"/>: who is asking comes from the token, everything else is checked by the service.</summary>
public static class GroupEndpoints
{
    public static IEndpointRouteBuilder MapGroupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup("/api/v1/groups").WithTags("Groups").RequireAuthorization();
        g.MapGet("/permissions", () => Results.Ok(GroupPermissions.All.Select(p => new { key = p.Key, label = p.Label, help = p.Help })));
        g.MapGet("/mine", Run(async (c, db, me, now, hub) => GroupResult.Done(await GroupService.MineAsync(db, me, now, c.RequestAborted))));
        g.MapGet("/search", async (string? q, int? page, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Results.Ok(await GroupService.SearchAsync(db, me, q, page ?? 0, t.GetUtcNow(), ct)));
        g.MapPost("", async (GroupProfileRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.CreateAsync(db, me, r, t.GetUtcNow(), ct), created: true));
        g.MapGet("/{groupId:guid}", async (Guid groupId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.ViewAsync(db, me, groupId, t.GetUtcNow(), ct)));
        g.MapPut("/{groupId:guid}", async (Guid groupId, GroupProfileRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.UpdateProfileAsync(db, me, groupId, r, t.GetUtcNow(), ct)));

        // coming in
        g.MapPost("/{groupId:guid}/join", async (Guid groupId, JoinRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, NotificationHub hub, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.JoinAsync(db, me, groupId, r.Message, t.GetUtcNow(), Push(hub, ct), ct)));
        g.MapDelete("/{groupId:guid}/join", async (Guid groupId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.CancelRequestAsync(db, me, groupId, t.GetUtcNow(), ct)));
        g.MapGet("/{groupId:guid}/requests", async (Guid groupId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.RequestsAsync(db, me, groupId, t.GetUtcNow(), ct)));
        g.MapPost("/{groupId:guid}/requests/{requestId:guid}/approve", async (Guid groupId, Guid requestId, HttpContext c, LifeVerseDbContext db, TimeProvider t, NotificationHub hub, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.DecideRequestAsync(db, me, groupId, requestId, true, t.GetUtcNow(), Push(hub, ct), ct)));
        g.MapPost("/{groupId:guid}/requests/{requestId:guid}/deny", async (Guid groupId, Guid requestId, HttpContext c, LifeVerseDbContext db, TimeProvider t, NotificationHub hub, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.DecideRequestAsync(db, me, groupId, requestId, false, t.GetUtcNow(), Push(hub, ct), ct)));
        g.MapPost("/{groupId:guid}/invites", async (Guid groupId, InviteRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, NotificationHub hub, CancellationToken ct) =>
        {
            if (!Who(c, out var me)) return Results.Unauthorized();
            var target = r.UserId;
            if (target is null && !string.IsNullOrWhiteSpace(r.Username)) { var name = r.Username.Trim().ToLowerInvariant(); target = await db.Users.AsNoTracking().Where(u => u.NormalizedUsername == name.ToUpperInvariant() && u.DeletedAt == null).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct); }
            return target is null ? Results.NotFound(new { error = "not_found" }) : Answer(await GroupService.InviteAsync(db, me, groupId, target.Value, t.GetUtcNow(), Push(hub, ct), ct));
        });

        // members and moderation
        g.MapGet("/{groupId:guid}/members", async (Guid groupId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.MembersAsync(db, me, groupId, t.GetUtcNow(), ct)));
        g.MapPost("/{groupId:guid}/members/{userId:guid}/kick", async (Guid groupId, Guid userId, ReasonRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, NotificationHub hub, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.KickAsync(db, me, groupId, userId, r.Reason, t.GetUtcNow(), Push(hub, ct), ct)));
        g.MapPost("/{groupId:guid}/members/{userId:guid}/ban", async (Guid groupId, Guid userId, BanRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, NotificationHub hub, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.BanAsync(db, me, groupId, userId, r.Reason, r.Days, t.GetUtcNow(), Push(hub, ct), ct)));
        g.MapPost("/{groupId:guid}/members/{userId:guid}/mute", async (Guid groupId, Guid userId, MuteRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.MuteAsync(db, me, groupId, userId, r.Minutes, r.Reason, t.GetUtcNow(), ct)));
        g.MapPut("/{groupId:guid}/members/{userId:guid}/role", async (Guid groupId, Guid userId, AssignRoleRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, NotificationHub hub, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.SetRoleAsync(db, me, groupId, userId, r.RoleId, t.GetUtcNow(), Push(hub, ct), ct)));
        g.MapGet("/{groupId:guid}/bans", async (Guid groupId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.BansAsync(db, me, groupId, t.GetUtcNow(), ct)));
        g.MapDelete("/{groupId:guid}/bans/{userId:guid}", async (Guid groupId, Guid userId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.UnbanAsync(db, me, groupId, userId, t.GetUtcNow(), ct)));

        // cargos
        g.MapGet("/{groupId:guid}/roles", async (Guid groupId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.RolesAsync(db, me, groupId, t.GetUtcNow(), ct)));
        g.MapPost("/{groupId:guid}/roles", async (Guid groupId, RoleRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.CreateRoleAsync(db, me, groupId, r, t.GetUtcNow(), ct), created: true));
        g.MapPut("/{groupId:guid}/roles/{roleId:guid}", async (Guid groupId, Guid roleId, RoleRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.UpdateRoleAsync(db, me, groupId, roleId, r, t.GetUtcNow(), ct)));
        g.MapDelete("/{groupId:guid}/roles/{roleId:guid}", async (Guid groupId, Guid roleId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.DeleteRoleAsync(db, me, groupId, roleId, t.GetUtcNow(), ct)));

        // history, leaving, leadership, the end
        g.MapGet("/{groupId:guid}/audit", async (Guid groupId, DateTimeOffset? before, int? take, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.AuditAsync(db, me, groupId, before, take ?? 50, t.GetUtcNow(), ct)));
        g.MapPost("/{groupId:guid}/leave", async (Guid groupId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.LeaveAsync(db, me, groupId, t.GetUtcNow(), ct)));
        g.MapPost("/{groupId:guid}/transfer", async (Guid groupId, ConfirmRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, NotificationHub hub, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : r.UserId is null ? Results.BadRequest(new { error = "bad_target" }) : Answer(await GroupService.TransferLeadershipAsync(db, me, groupId, r.UserId.Value, r.Confirm, t.GetUtcNow(), Push(hub, ct), ct)));
        g.MapPost("/{groupId:guid}/dissolve", async (Guid groupId, ConfirmRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, NotificationHub hub, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.DissolveAsync(db, me, groupId, r.Confirm, t.GetUtcNow(), Push(hub, ct), ct)));

        // chat of the group
        g.MapGet("/{groupId:guid}/chat", async (Guid groupId, DateTimeOffset? before, int? take, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.ChatAsync(db, me, groupId, before, take ?? 50, t.GetUtcNow(), ct)));
        g.MapPost("/{groupId:guid}/chat", async (Guid groupId, ChatRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, NotificationHub hub, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.SendChatAsync(db, me, groupId, r.Content, r.ClientMessageId, t.GetUtcNow(), Push(hub, ct), ct)));
        g.MapDelete("/{groupId:guid}/chat/{messageId:guid}", async (Guid groupId, Guid messageId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.DeleteChatMessageAsync(db, me, groupId, messageId, t.GetUtcNow(), ct)));

        // announcements
        g.MapGet("/{groupId:guid}/announcements", async (Guid groupId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.AnnouncementsAsync(db, me, groupId, t.GetUtcNow(), ct)));
        g.MapPost("/{groupId:guid}/announcements", async (Guid groupId, AnnouncementRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, NotificationHub hub, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.PostAnnouncementAsync(db, me, groupId, r, t.GetUtcNow(), Push(hub, ct), ct), created: true));
        g.MapPut("/{groupId:guid}/announcements/{announcementId:guid}", async (Guid groupId, Guid announcementId, AnnouncementRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.UpdateAnnouncementAsync(db, me, groupId, announcementId, r, t.GetUtcNow(), ct)));
        g.MapDelete("/{groupId:guid}/announcements/{announcementId:guid}", async (Guid groupId, Guid announcementId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.DeleteAnnouncementAsync(db, me, groupId, announcementId, t.GetUtcNow(), ct)));

        // events only for members
        g.MapGet("/{groupId:guid}/events", async (Guid groupId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.EventsAsync(db, me, groupId, t.GetUtcNow(), ct)));
        g.MapPost("/{groupId:guid}/events", async (Guid groupId, GroupEventRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, NotificationHub hub, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.CreateEventAsync(db, me, groupId, r, t.GetUtcNow(), Push(hub, ct), ct), created: true));
        g.MapPost("/{groupId:guid}/events/{eventId:guid}/join", async (Guid groupId, Guid eventId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.JoinEventAsync(db, me, groupId, eventId, t.GetUtcNow(), ct)));
        g.MapDelete("/{groupId:guid}/events/{eventId:guid}/join", async (Guid groupId, Guid eventId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.LeaveEventAsync(db, me, groupId, eventId, t.GetUtcNow(), ct)));
        g.MapDelete("/{groupId:guid}/events/{eventId:guid}", async (Guid groupId, Guid eventId, HttpContext c, LifeVerseDbContext db, TimeProvider t, NotificationHub hub, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.CancelEventAsync(db, me, groupId, eventId, t.GetUtcNow(), Push(hub, ct), ct)));

        // notifications
        g.MapGet("/notifications", async (bool? unread, int? take, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.NotificationsAsync(db, me, unread == true, take ?? 50, t.GetUtcNow(), ct)));
        g.MapPost("/notifications/read", async (ReadRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.MarkNotificationsReadAsync(db, me, r.Ids, r.GroupId, t.GetUtcNow(), ct)));
        g.MapPut("/{groupId:guid}/notification-settings", async (Guid groupId, MutedRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await GroupService.SetNotificationsMutedAsync(db, me, groupId, r.Muted, t.GetUtcNow(), ct)));
        return endpoints;
    }


    private static Delegate Run(Func<HttpContext, LifeVerseDbContext, Guid, DateTimeOffset, NotificationHub, Task<GroupResult>> operation) =>
        async (HttpContext c, LifeVerseDbContext db, TimeProvider t, NotificationHub hub) => !Who(c, out var me) ? Results.Unauthorized() : Answer(await operation(c, db, me, t.GetUtcNow(), hub));

    private static GroupNotifier Push(NotificationHub hub, CancellationToken ct) => (user, type, data) => hub.NotifyAsync(user, type, data, ct);

    public static bool Who(HttpContext context, out Guid user) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out user);

    /// <summary>The answer of the service as an HTTP answer: not found, forbidden, refused for a reason of the request (400), refused for the state of the group (409), too fast (429).</summary>
    public static IResult Answer(GroupResult result, bool created = false) => result.Ok ? created ? Results.Created(string.Empty, result.Data) : Results.Ok(result.Data ?? new { }) : result.Error switch
    {
        "not_found" => Results.NotFound(new { error = result.Error }),
        "forbidden" or "outranked" or "cannot_grant" or "muted" => Results.Json(new { error = result.Error }, statusCode: 403),
        "rate_limited" => Results.Json(new { error = result.Error }, statusCode: 429),
        var e when e is not null && (e.StartsWith("bad_", StringComparison.Ordinal) || e is "confirmation_needed") => Results.BadRequest(new { error = e }),
        _ => Results.Conflict(new { error = result.Error, detail = result.Data })
    };
}
