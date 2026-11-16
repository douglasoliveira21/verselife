using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Discovery;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Browsing a kind with no text typed (the tabs of the search window before anything is searched): the most popular first, still only what the viewer may see, and people only by name.</summary>
public sealed class SearchBrowseTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _me = Guid.NewGuid(), _ana = Guid.NewGuid();

    public SearchBrowseTests()
    {
        foreach (var (id, name) in new[] { (_me, "eu"), (_ana, "ana") })
        { _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now }); _db.Avatars.Add(new Avatar { Id = Guid.NewGuid(), UserId = id, DisplayName = name, BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now }); }
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Island Island(string name, string category, bool isPublic = true)
    {
        var i = new Island { Id = Guid.NewGuid(), OwnerUserId = _ana, Name = name, Slug = Guid.NewGuid().ToString("N")[..12], IsPublic = isPublic, Description = "", Category = category, CreatedAt = _now, UpdatedAt = _now };
        _db.Islands.Add(i); _db.SaveChanges(); return i;
    }

    private async Task<SearchPage> Find(SearchRequest request) { await SearchIndexer.RunAsync(_db, _now, default); return await GlobalSearch.SearchAsync(_db, _me, request, _now, default); }

    [Fact]
    public async Task NoTextAndNoKindIsStillNothing()
    {
        Island("Mar Azul", "social");
        Assert.Equal(0, (await Find(new SearchRequest(""))).Total);
        Assert.Equal(0, (await Find(new SearchRequest(null, []))).Total);
    }

    [Fact]
    public async Task NoTextWithAKindListsThePopularOnesAndHonorsFilters()
    {
        Island("Mar Azul", "social"); Island("Mar Verde", "negocios"); var popular = Island("Lago Famoso", "social");
        for (var i = 0; i < 3; i++) _db.IslandFavorites.Add(new IslandFavorite { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IslandId = popular.Id, CreatedAt = _now }); await _db.SaveChangesAsync();
        var all = await Find(new SearchRequest("", ["island"]));
        Assert.Equal(3, all.Total); Assert.Equal("Lago Famoso", all.Items[0].Title);
        var social = await Find(new SearchRequest("", ["island"], Category: "social")); Assert.Equal(2, social.Total);
        Assert.Equal(3, (await Find(new SearchRequest("", ["island"], PageSize: 2))).Total);
    }

    [Fact]
    public async Task BrowsingNeverShowsWhatIsPrivateOrPeopleWithoutAName()
    {
        Island("Ilha Publica", "social"); Island("Ilha Secreta", "social", isPublic: false);
        var islands = await Find(new SearchRequest("", ["island"]));
        Assert.Equal(["Ilha Publica"], islands.Items.Select(i => i.Title));
        Assert.Equal(0, (await Find(new SearchRequest("", ["person"]))).Total); // people are found by name only
    }
}
