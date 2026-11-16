using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The feed of a profile: publications and unlocked achievements, with who may see what decided on the server.</summary>
public sealed class ProfileFeedTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _ana, _bia, _caio;

    public ProfileFeedTests() => (_ana, _bia, _caio) = (User("ana"), User("bia"), User("caio"));

    public void Dispose() => _db.Dispose();

    private Guid User(string name)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now.AddDays(-30), UpdatedAt = _now.AddDays(-30) });
        _db.SaveChanges(); return id;
    }

    private Guid Image(Guid owner, string mime = "image/png")
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner, CreatorUserId = owner, Name = "foto", Category = "textures", CreatedAt = _now, UpdatedAt = _now };
        _db.Assets.Add(asset); _db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = 1, ContentHash = Guid.NewGuid().ToString("N"), StorageKey = "t/foto", MimeType = mime, ByteSize = 1000 });
        _db.SaveChanges(); return asset.Id;
    }

    private async Task Post(Guid user, string text, string? visibility = null, Guid? asset = null) { Assert.Null(await ProfileFeed.PostAsync(_db, user, text, asset, visibility, _now, default)); _now = _now.AddMinutes(1); }
    private async Task<JsonElement> Feed(Guid viewer, Guid target, DateTimeOffset? before = null) => JsonSerializer.SerializeToElement(await ProfileFeed.PageAsync(_db, viewer, target, before, _now, default));
    private static List<string> Texts(JsonElement feed) => [.. feed.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("kind").GetString() == "post").Select(i => i.GetProperty("text").GetString()!)];
    private void Friends(Guid a, Guid b) { _db.Friendships.Add(new Friendship { Id = Guid.NewGuid(), UserId = a, FriendUserId = b, Status = "accepted", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); }

    [Fact]
    public async Task PostsAreListedNewestFirstAndOnlyTheOwnerCanPost()
    {
        await Post(_ana, "primeira"); await Post(_ana, "segunda");
        Assert.Equal(["segunda", "primeira"], Texts(await Feed(_bia, _ana)));
        Assert.True((await Feed(_ana, _ana)).GetProperty("canPost").GetBoolean()); Assert.False((await Feed(_bia, _ana)).GetProperty("canPost").GetBoolean());
    }

    [Fact]
    public async Task ABrandNewResidentHasAnEmptyFeed()
    {
        var feed = await Feed(_bia, _caio);
        Assert.Equal(0, feed.GetProperty("items").GetArrayLength()); Assert.False(feed.GetProperty("more").GetBoolean());
    }

    [Fact]
    public async Task FriendsOnlyPostsAreSeenByFriendsAndTheOwnerOnly()
    {
        await Post(_ana, "para todos"); await Post(_ana, "só amigos", "friends"); Friends(_ana, _bia);
        Assert.Equal(["só amigos", "para todos"], Texts(await Feed(_bia, _ana)));
        Assert.Equal(["para todos"], Texts(await Feed(_caio, _ana)));
        Assert.Equal(2, Texts(await Feed(_ana, _ana)).Count);
    }

    [Fact]
    public async Task APrivateProfileOrABlockedViewerSeesNothing()
    {
        await Post(_ana, "oi");
        _db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = _ana, BlockedUserId = _bia, CreatedAt = _now }); await _db.SaveChangesAsync();
        var blocked = await Feed(_bia, _ana); Assert.Equal("blocked", blocked.GetProperty("hidden").GetString()); Assert.Empty(Texts(blocked));
        Assert.Equal(["oi"], Texts(await Feed(_caio, _ana)));
        Assert.Null(await ProfileService.UpdateAsync(_db, _ana, new ProfileUpdate { PrivateProfile = true }, _now, default));
        var hidden = await Feed(_caio, _ana); Assert.Equal("private", hidden.GetProperty("hidden").GetString()); Assert.Empty(Texts(hidden));
        Assert.Equal(["oi"], Texts(await Feed(_ana, _ana)));
    }

    [Fact]
    public async Task PostsAreValidated()
    {
        Assert.Equal("empty_post", (await ProfileFeed.PostAsync(_db, _ana, "   ", null, null, _now, default))?.Code);
        Assert.Equal("too_long", (await ProfileFeed.PostAsync(_db, _ana, new string('a', ProfileFeed.MaxText + 1), null, null, _now, default))?.Code);
        Assert.Equal("bad_visibility", (await ProfileFeed.PostAsync(_db, _ana, "oi", null, "secret", _now, default))?.Code);
        Assert.Equal("photo_not_owned", (await ProfileFeed.PostAsync(_db, _ana, "oi", Image(_bia), null, _now, default))?.Code);
        Assert.Equal("photo_not_image", (await ProfileFeed.PostAsync(_db, _ana, "oi", Image(_ana, "application/zip"), null, _now, default))?.Code);
        Assert.Null(await ProfileFeed.PostAsync(_db, _ana, "", Image(_ana), null, _now, default));
    }

    [Fact]
    public async Task ThereIsADailyLimit()
    {
        for (var i = 0; i < ProfileFeed.MaxPostsPerDay; i++) await Post(_ana, $"p{i}");
        Assert.Equal("too_many", (await ProfileFeed.PostAsync(_db, _ana, "mais uma", null, null, _now, default))?.Code);
        _now = _now.AddDays(2); await Post(_ana, "outro dia");
    }

    [Fact]
    public async Task OnlyTheOwnerDeletesAndDeletedPostsDisappear()
    {
        await Post(_ana, "apagar"); var id = (await _db.ProfilePosts.SingleAsync()).Id;
        Assert.Equal("not_found", (await ProfileFeed.DeleteAsync(_db, _bia, id, _now, default))?.Code);
        Assert.Null(await ProfileFeed.DeleteAsync(_db, _ana, id, _now, default));
        Assert.Empty(Texts(await Feed(_ana, _ana))); Assert.Equal("not_found", (await ProfileFeed.DeleteAsync(_db, _ana, id, _now, default))?.Code);
    }

    [Fact]
    public async Task PagesStopAtTheCursorAndSayWhenThereIsMore()
    {
        for (var i = 0; i < ProfileFeed.PageSize + 5; i++) _db.ProfilePosts.Add(new ProfilePost { Id = Guid.NewGuid(), UserId = _ana, Text = $"p{i}", CreatedAt = _now.AddMinutes(i) });
        await _db.SaveChangesAsync(); _now = _now.AddHours(2);
        var first = await Feed(_ana, _ana); Assert.Equal(ProfileFeed.PageSize, first.GetProperty("items").GetArrayLength()); Assert.True(first.GetProperty("more").GetBoolean());
        var last = first.GetProperty("items").EnumerateArray().Last().GetProperty("at").GetDateTimeOffset();
        var second = await Feed(_ana, _ana, last); Assert.Equal(5, Texts(second).Count); Assert.False(second.GetProperty("more").GetBoolean());
        Assert.DoesNotContain(Texts(second), t => Texts(first).Contains(t));
    }

    [Fact]
    public async Task AchievementsAppearWithTheirRealDateAndFollowPrivacy()
    {
        _db.GroupMembers.Add(new GroupMember { GroupId = Guid.NewGuid(), UserId = _ana, Role = "member", JoinedAt = _now.AddDays(-3), ShowInProfile = true });
        var group = new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _bia, Name = "Cafe", NameKey = "cafe", CreatedAt = _now, UpdatedAt = _now };
        _db.SocialGroups.Add(group); _db.GroupMembers.Add(new GroupMember { GroupId = group.Id, UserId = _ana, Role = "member", JoinedAt = _now.AddDays(-3), ShowInProfile = true }); await _db.SaveChangesAsync();
        var feed = await Feed(_bia, _ana); var item = feed.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "achievement");
        Assert.Equal("Faz parte de uma comunidade", item.GetProperty("title").GetString()); Assert.Equal(_now.AddDays(-3), item.GetProperty("at").GetDateTimeOffset());
        Assert.Null(await ProfileService.UpdateAsync(_db, _ana, new ProfileUpdate { HideAchievements = true }, _now, default));
        Assert.DoesNotContain(( await Feed(_bia, _ana)).GetProperty("items").EnumerateArray(), i => i.GetProperty("kind").GetString() == "achievement");
        Assert.Contains((await Feed(_ana, _ana)).GetProperty("items").EnumerateArray(), i => i.GetProperty("kind").GetString() == "achievement");
    }

    [Fact]
    public async Task ThePictureOfAPostFollowsTheVisibilityOfThePost()
    {
        var asset = Image(_ana); await Post(_ana, "foto", "friends", asset); var post = (await _db.ProfilePosts.SingleAsync()).Id;
        Assert.Equal(asset, await ProfileFeed.ImageOfAsync(_db, _ana, _ana, post, default));
        Assert.Null(await ProfileFeed.ImageOfAsync(_db, _caio, _ana, post, default));
        Friends(_ana, _bia); Assert.Equal(asset, await ProfileFeed.ImageOfAsync(_db, _bia, _ana, post, default));
        Assert.Null(await ProfileFeed.ImageOfAsync(_db, _bia, _caio, post, default));
    }
}
