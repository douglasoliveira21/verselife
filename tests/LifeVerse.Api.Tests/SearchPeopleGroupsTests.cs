using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Discovery;
using LifeVerse.Api.Modules.Social;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>What the People and Groups tabs of the search window need: how I am related to the people found, their pictures, and the category of groups.</summary>
public sealed class SearchPeopleGroupsTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _me, _friend, _asked, _asker, _stranger, _blocked;

    public SearchPeopleGroupsTests() => (_me, _friend, _asked, _asker, _stranger, _blocked) = (User("eu"), User("amiga"), User("pedida"), User("pedinte"), User("estranha"), User("bloqueado"));

    public void Dispose() => _db.Dispose();

    private Guid User(string name, string? photo = null)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now });
        _db.Avatars.Add(new Avatar { Id = Guid.NewGuid(), UserId = id, DisplayName = name, BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); return id;
    }

    [Fact]
    public async Task RelationsTellFriendshipStateForEachPerson()
    {
        _db.Friendships.AddRange(new Friendship { Id = Guid.NewGuid(), UserId = _me, FriendUserId = _friend, Status = "accepted", CreatedAt = _now, UpdatedAt = _now },
            new Friendship { Id = Guid.NewGuid(), UserId = _me, FriendUserId = _asked, Status = "pending", CreatedAt = _now, UpdatedAt = _now },
            new Friendship { Id = Guid.NewGuid(), UserId = _asker, FriendUserId = _me, Status = "pending", CreatedAt = _now, UpdatedAt = _now },
            new Friendship { Id = Guid.NewGuid(), UserId = _friend, FriendUserId = _stranger, Status = "accepted", CreatedAt = _now, UpdatedAt = _now });
        _db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = _blocked, BlockedUserId = _me, CreatedAt = _now }); _db.SaveChanges();
        var map = await PeopleQueries.RelationsAsync(_db, _me, [_friend, _asked, _asker, _stranger, _blocked, _me, Guid.NewGuid()], default);
        Assert.Equal("friend", map[_friend]); Assert.Equal("pending_out", map[_asked]); Assert.Equal("pending_in", map[_asker]); Assert.Equal("none", map[_stranger]); Assert.Equal("blocked", map[_blocked]);
        Assert.Equal(5, map.Count); // not myself, not somebody who does not exist
    }

    [Fact]
    public async Task RelationsAreLimitedAndIgnoreDeletedAccounts()
    {
        var gone = User("sumiu"); (await _db.Users.SingleAsync(u => u.Id == gone)).DeletedAt = _now; await _db.SaveChangesAsync();
        Assert.DoesNotContain(gone, (await PeopleQueries.RelationsAsync(_db, _me, [gone, _friend], default)).Keys);
        var many = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).Append(_friend).ToList();
        Assert.True((await PeopleQueries.RelationsAsync(_db, _me, many, default)).Count <= PeopleQueries.MaxRelationIds);
        Assert.Empty(await PeopleQueries.RelationsAsync(_db, _me, [], default));
    }

    [Fact]
    public async Task AFoundPersonWithAProfilePictureCarriesThePathOfThePicture()
    {
        var withPhoto = User("fotografa"); var user = await _db.Users.SingleAsync(u => u.Id == withPhoto); user.ProfilePhotoAssetId = Guid.NewGuid(); await _db.SaveChangesAsync();
        await SearchIndexer.RunAsync(_db, _now, default);
        var page = await GlobalSearch.SearchAsync(_db, _me, new SearchRequest("fotografa", ["person"]), _now, default);
        Assert.Equal($"/api/v1/users/{withPhoto}/profile/photo", page.Items.Single().ThumbnailUrl);
        var none = await GlobalSearch.SearchAsync(_db, _me, new SearchRequest("estranha", ["person"]), _now, default);
        Assert.Null(none.Items.Single().ThumbnailUrl);
    }

    [Fact]
    public async Task APrivateProfileIsNotFoundAndItsPictureIsNotListed()
    {
        var secret = User("reservada"); var user = await _db.Users.SingleAsync(u => u.Id == secret); user.ProfilePhotoAssetId = Guid.NewGuid(); user.PrivacyJson = "{\"profile\":\"private\"}"; await _db.SaveChangesAsync();
        await SearchIndexer.RunAsync(_db, _now, default);
        Assert.Equal(0, (await GlobalSearch.SearchAsync(_db, _me, new SearchRequest("reservada", ["person"]), _now, default)).Total);
    }

    [Fact]
    public async Task GroupsAreFoundWithTheirCategoryAndHiddenOnesAreNot()
    {
        _db.SocialGroups.Add(new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _friend, Name = "Pescadores do Lago", NameKey = "pescadores do lago", Category = "natureza", Privacy = "public", CreatedAt = _now, UpdatedAt = _now });
        _db.SocialGroups.Add(new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _friend, Name = "Pescadores Secretos", NameKey = "pescadores secretos", Category = "natureza", Privacy = "private", CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync();
        await SearchIndexer.RunAsync(_db, _now, default);
        var page = await GlobalSearch.SearchAsync(_db, _me, new SearchRequest("pescadores", ["group"], Category: "natureza"), _now, default);
        Assert.Equal(["Pescadores do Lago"], page.Items.Select(i => i.Title));
    }
}
