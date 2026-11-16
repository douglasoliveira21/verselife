using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Users;

/// <summary>
/// The feed of a profile: the publications of the person (a short text, maybe one picture of theirs, for everybody or only for friends) and the achievements they unlocked, newest first, with the date of the
/// record. Who sees what is decided here: a private profile shows nothing to others, blocked people see nothing, friends-only posts are for friends, and hidden achievements do not appear.
/// </summary>
public static class ProfileFeed
{
    public const int MaxText = 500;
    public const int PageSize = 20;
    public const int MaxPostsPerDay = 20;

    public sealed record Item(string Kind, string Key, DateTimeOffset At, string Title, string Text, string? Icon, Guid? PostId, bool HasImage, string? Visibility);

    private static async Task<bool> BlockedAsync(LifeVerseDbContext db, Guid a, Guid b, CancellationToken ct) =>
        await db.UserBlocks.AsNoTracking().AnyAsync(x => x.UserId == a && x.BlockedUserId == b || x.UserId == b && x.BlockedUserId == a, ct);

    private static async Task<bool> FriendsAsync(LifeVerseDbContext db, Guid a, Guid b, CancellationToken ct) =>
        await db.Friendships.AsNoTracking().AnyAsync(x => x.Status == "accepted" && (x.UserId == a && x.FriendUserId == b || x.UserId == b && x.FriendUserId == a), ct);

    /// <summary>Whether <paramref name="viewer"/> may see the publications of <paramref name="target"/> (null: yes; otherwise the reason).</summary>
    public static async Task<string?> WhyHiddenAsync(LifeVerseDbContext db, User user, Guid viewer, CancellationToken ct)
    {
        if (viewer == user.Id) return null;
        if (ProfileService.PrivacyOf(user.PrivacyJson).ProfilePrivate) return "private";
        return await BlockedAsync(db, viewer, user.Id, ct) ? "blocked" : null;
    }

    /// <summary>One page of the feed, older than <paramref name="before"/> (null: the newest). <c>more</c> says whether there is more to load.</summary>
    public static async Task<object?> PageAsync(LifeVerseDbContext db, Guid viewer, Guid target, DateTimeOffset? before, DateTimeOffset now, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == target && x.DeletedAt == null, ct);
        if (user is null) return null;
        var self = viewer == target;
        if (await WhyHiddenAsync(db, user, viewer, ct) is { } reason) return new { items = Array.Empty<object>(), more = false, hidden = reason, canPost = false };
        var privacy = ProfileService.PrivacyOf(user.PrivacyJson);
        var friend = self || await FriendsAsync(db, viewer, target, ct);

        var posts = db.ProfilePosts.AsNoTracking().Where(p => p.UserId == target && p.DeletedAt == null && (before == null || p.CreatedAt < before) && (friend || p.Visibility == "public"));
        var items = (await posts.OrderByDescending(p => p.CreatedAt).Take(PageSize + 1).ToListAsync(ct))
            .Select(p => new Item("post", p.Id.ToString(), p.CreatedAt, string.Empty, p.Text, null, p.Id, p.AssetId is not null, p.Visibility)).ToList();

        if (self || !privacy.AchievementsHidden)
        {
            var marriage = self || !privacy.MarriageHidden ? await ProfileService.CurrentMarriageAsync(db, target, ct) : null;
            foreach (var a in await ProfileSections.AchievementListAsync(db, user, privacy, self, ProfileService.MarriedSince(marriage), now, ct))
                if (a.UnlockedAt <= now && (before is null || a.UnlockedAt < before)) items.Add(new("achievement", a.Key, a.UnlockedAt, a.Title, a.Description, a.Icon, null, false, null));
        }
        var ordered = items.OrderByDescending(i => i.At).ToList();
        var page = ordered.Take(PageSize).Select(i => (object)new { kind = i.Kind, key = i.Key, at = i.At, title = i.Title, text = i.Text, icon = i.Icon, postId = i.PostId, hasImage = i.HasImage, visibility = i.Visibility }).ToList();
        return new { items = page, more = ordered.Count > PageSize, hidden = (string?)null, canPost = self };
    }

    public static async Task<ProfileFailure?> PostAsync(LifeVerseDbContext db, Guid userId, string? text, Guid? assetId, string? visibility, DateTimeOffset now, CancellationToken ct)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0 && assetId is null) return new("empty_post", "Escreva algo ou escolha uma imagem.");
        if (text.Length > MaxText) return new("too_long", $"A publicação aceita até {MaxText} letras.");
        visibility = visibility is null or "" ? "public" : visibility;
        if (visibility is not ("public" or "friends")) return new("bad_visibility", "Escolha quem pode ver: todos ou só amigos.");
        if (assetId is { } asset && await ProfileGallery.CheckImageAsync(db, userId, asset, ct) is { } bad) return bad;
        var since = now.AddDays(-1);
        if (await db.ProfilePosts.CountAsync(p => p.UserId == userId && p.CreatedAt > since, ct) >= MaxPostsPerDay) return new("too_many", $"Você já publicou {MaxPostsPerDay} vezes nas últimas 24 horas.");
        db.ProfilePosts.Add(new ProfilePost { Id = Guid.NewGuid(), UserId = userId, Text = text, AssetId = assetId, Visibility = visibility, CreatedAt = now });
        await db.SaveChangesAsync(ct); return null;
    }

    public static async Task<ProfileFailure?> DeleteAsync(LifeVerseDbContext db, Guid userId, Guid postId, DateTimeOffset now, CancellationToken ct)
    {
        var post = await db.ProfilePosts.SingleOrDefaultAsync(p => p.Id == postId && p.UserId == userId && p.DeletedAt == null, ct);
        if (post is null) return new("not_found", "Publicação não encontrada.");
        post.DeletedAt = now; await db.SaveChangesAsync(ct); return null;
    }

    /// <summary>The asset of the picture of a post, when <paramref name="viewer"/> may see that post.</summary>
    public static async Task<Guid?> ImageOfAsync(LifeVerseDbContext db, Guid viewer, Guid owner, Guid postId, CancellationToken ct)
    {
        var post = await db.ProfilePosts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == postId && p.UserId == owner && p.DeletedAt == null, ct);
        if (post?.AssetId is null) return null;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == owner && x.DeletedAt == null, ct);
        if (user is null || await WhyHiddenAsync(db, user, viewer, ct) is not null) return null;
        if (post.Visibility == "friends" && viewer != owner && !await FriendsAsync(db, viewer, owner, ct)) return null;
        return post.AssetId;
    }
}
