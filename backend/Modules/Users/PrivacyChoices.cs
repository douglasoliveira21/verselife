using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Users;

/// <summary>Who may see a person online, write to them, invite them to groups and ask for their friendship.</summary>
public sealed record PrivacySettings(string OnlineVisibility, string PrivateMessages, string GroupInvites, string FriendRequests);

/// <summary>A change to those choices: only what is sent changes.</summary>
public sealed record PrivacyPatch(string? OnlineVisibility = null, string? PrivateMessages = null, string? GroupInvites = null, string? FriendRequests = null);

/// <summary>
/// The privacy choices of an account, kept in the account's privacy document and enforced here, whatever the client shows:
/// <c>GET/PUT /api/v1/users/me/privacy</c>. They are not "synced settings": they work whether or not a person syncs their settings.
/// </summary>
public static class PrivacyChoices
{
    public const string Everyone = "everyone", Friends = "friends", Nobody = "nobody";
    private static readonly string[] Three = [Everyone, Friends, Nobody];
    private static readonly string[] Two = [Everyone, Nobody]; // a friend cannot ask for a friendship again

    public static PrivacySettings Read(string? json)
    {
        string Rule(string key) => ProfileService.PrivacyValue(json, key) is var value && value is Friends or Nobody ? value : Everyone;
        // "presence" is older: "private" hides from everybody; its default "friends" never restricted anyone, so it does not now
        var online = ProfileService.PrivacyOf(json).PresencePrivate ? Nobody : Rule("online");
        return new PrivacySettings(online, Rule("messages"), Rule("invites"), Rule("friend_requests") == Nobody ? Nobody : Everyone);
    }

    public static Dictionary<string, string[]> Problems(PrivacyPatch patch)
    {
        var problems = new Dictionary<string, string[]>();
        void Check(string name, string? value, string[] allowed) { if (value is not null && !allowed.Contains(value)) problems[name] = [$"Deve ser um de: {string.Join(", ", allowed)}."]; }
        Check("onlineVisibility", patch.OnlineVisibility, Three); Check("privateMessages", patch.PrivateMessages, Three); Check("groupInvites", patch.GroupInvites, Three); Check("friendRequests", patch.FriendRequests, Two);
        return problems;
    }

    /// <summary>The document with the patch applied; what it does not name stays.</summary>
    public static string Write(string? json, PrivacyPatch patch)
    {
        JsonObject root;
        try { root = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? []; _ = root.Count; }
        catch (Exception e) when (e is JsonException or ArgumentException) { root = []; }
        void Set(string key, string? value) { if (value is null) return; if (value == Everyone) root.Remove(key); else root[key] = value; }
        if (patch.OnlineVisibility is { } online)
        {
            if (online == Nobody) root["presence"] = "private"; else if (ProfileService.PrivacyOf(json).PresencePrivate) root.Remove("presence");
            Set("online", online == Friends ? Friends : Everyone);
        }
        Set("messages", patch.PrivateMessages); Set("invites", patch.GroupInvites); Set("friend_requests", patch.FriendRequests);
        return root.ToJsonString();
    }

    /// <summary>Whether <paramref name="viewer"/> is in the audience a person chose.</summary>
    public static async Task<bool> AllowsAsync(LifeVerseDbContext db, string audience, Guid owner, Guid viewer, CancellationToken ct) =>
        owner == viewer || audience == Everyone || audience == Friends && await db.Friendships.AnyAsync(f => f.UserId == viewer && f.FriendUserId == owner && f.Status == "accepted", ct);

    /// <summary>Whether <paramref name="viewer"/> may know if this person is online.</summary>
    public static Task<bool> SeesPresenceAsync(LifeVerseDbContext db, User owner, Guid viewer, CancellationToken ct) => AllowsAsync(db, Read(owner.PrivacyJson).OnlineVisibility, owner.Id, viewer, ct);

    public static async Task<IResult> GetAsync(HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var me)) return Results.Unauthorized();
        var json = await db.Users.AsNoTracking().Where(u => u.Id == me && u.DeletedAt == null).Select(u => u.PrivacyJson).SingleOrDefaultAsync(ct);
        return json is null ? Results.NotFound() : Results.Ok(Read(json));
    }

    public static async Task<IResult> PutAsync(PrivacyPatch patch, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var me)) return Results.Unauthorized();
        var problems = Problems(patch); if (problems.Count > 0) return Results.ValidationProblem(problems);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == me && u.DeletedAt == null, ct); if (user is null) return Results.NotFound();
        user.PrivacyJson = Write(user.PrivacyJson, patch); user.UpdatedAt = clock.GetUtcNow(); // the search index follows UpdatedAt
        await db.SaveChangesAsync(ct);
        return Results.Ok(Read(user.PrivacyJson));
    }

    public static RouteGroupBuilder MapPrivacyChoiceEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/privacy", GetAsync);
        group.MapPut("/privacy", PutAsync);
        return group;
    }
}
