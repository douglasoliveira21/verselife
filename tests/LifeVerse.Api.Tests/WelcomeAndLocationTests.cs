using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Networking;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The island new avatars arrive on, and bringing people back to the island and the spot where they were.</summary>
public sealed class WelcomeAndLocationTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => _db.Dispose();

    private User NewUser(string name) { var u = new User { Id = Guid.NewGuid(), Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now }; _db.Users.Add(u); _db.SaveChanges(); return u; }

    private Island NewIsland(User owner, string name, bool isPublic = true, int size = 256, TimeSpan? age = null)
    {
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = owner.Id, Name = name, Slug = name.ToLowerInvariant().Replace(' ', '-'), IsPublic = isPublic, Size = size, CreatedAt = _now - (age ?? TimeSpan.Zero), UpdatedAt = _now };
        _db.Islands.Add(island); _db.SaveChanges(); return island;
    }

    // ---- the welcome island ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task WithNoIslandsAtAllOneIsMadeForTheWelcome()
    {
        var island = await WelcomeIsland.ResolveAsync(_db, _now, default);
        Assert.Equal("Ilha de Boas-vindas", island.Name); Assert.True(island.IsPublic); Assert.True(island.IsWelcome); Assert.Equal("boas-vindas", island.Slug);
        var owner = await _db.Users.SingleAsync(); Assert.Equal("lifeverse", owner.Username); Assert.Equal("!", owner.PasswordHash); Assert.Equal(owner.Id, island.OwnerUserId); // an account nobody can sign in to
        var again = await WelcomeIsland.ResolveAsync(_db, _now, default);
        Assert.Equal(island.Id, again.Id); Assert.Equal(1, await _db.Islands.CountAsync()); Assert.Equal(1, await _db.Users.CountAsync());
    }

    [Fact]
    public async Task AnExistingPublicIslandIsUsedAndTheOldestWins()
    {
        var owner = NewUser("ana");
        NewIsland(owner, "Privada", isPublic: false, age: TimeSpan.FromDays(40));
        var deleted = NewIsland(owner, "Apagada", age: TimeSpan.FromDays(30)); deleted.DeletedAt = _now; _db.SaveChanges();
        var oldest = NewIsland(owner, "Praia", age: TimeSpan.FromDays(20)); NewIsland(owner, "Floresta", age: TimeSpan.FromDays(5));
        var chosen = await WelcomeIsland.ResolveAsync(_db, _now, default);
        Assert.Equal(oldest.Id, chosen.Id); Assert.Equal(1, await _db.Islands.CountAsync(i => i.IsWelcome)); Assert.Equal(3, await _db.Islands.CountAsync(i => i.DeletedAt == null)); // nothing was made
        Assert.DoesNotContain(await _db.Users.ToListAsync(), u => u.Username == "lifeverse");
    }

    [Fact]
    public async Task TheChoiceMovesOnWhenTheIslandIsDeletedOrClosed()
    {
        var owner = NewUser("ana"); var first = NewIsland(owner, "Praia", age: TimeSpan.FromDays(20)); var second = NewIsland(owner, "Floresta", age: TimeSpan.FromDays(5));
        Assert.Equal(first.Id, (await WelcomeIsland.ResolveAsync(_db, _now, default)).Id);
        first.IsPublic = false; await _db.SaveChangesAsync();
        Assert.Equal(second.Id, (await WelcomeIsland.ResolveAsync(_db, _now, default)).Id);
        Assert.Equal(1, await _db.Islands.CountAsync(i => i.IsWelcome)); Assert.False((await _db.Islands.SingleAsync(i => i.Id == first.Id)).IsWelcome);
        second.DeletedAt = _now; await _db.SaveChangesAsync(); first.IsPublic = true; await _db.SaveChangesAsync();
        Assert.Equal(first.Id, (await WelcomeIsland.ResolveAsync(_db, _now, default)).Id);
    }

    [Fact]
    public async Task AnOperatorChoosesAndTheOldOneStopsBeingIt()
    {
        var owner = NewUser("ana"); var a = NewIsland(owner, "Praia", age: TimeSpan.FromDays(20)); var b = NewIsland(owner, "Floresta", age: TimeSpan.FromDays(5));
        await WelcomeIsland.ResolveAsync(_db, _now, default);
        Assert.Equal(b.Id, (await WelcomeIsland.MarkAsync(_db, b, _now, default))!.Id);
        Assert.Equal(b.Id, (await WelcomeIsland.ResolveAsync(_db, _now, default)).Id); Assert.Equal([b.Id], await _db.Islands.Where(i => i.IsWelcome).Select(i => i.Id).ToListAsync());
        var closed = NewIsland(owner, "Fechada", isPublic: false); Assert.Null(await WelcomeIsland.MarkAsync(_db, closed, _now, default)); // a closed island cannot be the welcome
        Assert.Equal(b.Id, (await WelcomeIsland.ResolveAsync(_db, _now, default)).Id); _ = a;
    }

    [Fact]
    public async Task ARealPersonCalledLifeverseDoesNotBlockTheSystemAccount()
    {
        NewUser("lifeverse");
        var island = await WelcomeIsland.ResolveAsync(_db, _now, default);
        var owner = await _db.Users.SingleAsync(u => u.Id == island.OwnerUserId); Assert.Equal("lifeverse-sistema", owner.Username); Assert.Equal("!", owner.PasswordHash);
    }

    // ---- where people stood -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlySensibleSpotsOfKnownPeopleAreKept()
    {
        var ana = NewUser("ana"); var bia = NewUser("bia"); var island = NewIsland(ana, "Praia", size: 256);
        var kept = await PlayerLocations.SaveAsync(_db, island.Id, [
            new LocationEntry(ana.Id, 10, 1.5f, -20),
            new LocationEntry(bia.Id, 500, 1, 0),              // outside the island
            new LocationEntry(Guid.NewGuid(), 1, 1, 1),        // nobody we know
            new LocationEntry(bia.Id, float.NaN, 1, 0),        // not a number
            new LocationEntry(bia.Id, 0, 99999, 0)], _now, default); // higher than anything
        Assert.Equal(1, kept); var row = await _db.PlayerLocations.SingleAsync(); Assert.Equal(ana.Id, row.UserId); Assert.Equal((10f, 1.5f, -20f), (row.X, row.Y, row.Z)); Assert.Equal(island.Id, row.IslandId);
    }

    [Fact]
    public async Task OnlyTheLastSpotIsKeptAndItFollowsThePersonToAnotherIsland()
    {
        var ana = NewUser("ana"); var a = NewIsland(ana, "Praia"); var b = NewIsland(ana, "Floresta");
        await PlayerLocations.SaveAsync(_db, a.Id, [new LocationEntry(ana.Id, 1, 1, 1)], _now, default);
        await PlayerLocations.SaveAsync(_db, a.Id, [new LocationEntry(ana.Id, 2, 1, 2), new LocationEntry(ana.Id, 3, 1, 3)], _now.AddMinutes(1), default); // the last of a batch counts
        Assert.Equal((3f, 3f), ((await _db.PlayerLocations.SingleAsync()).X, (await _db.PlayerLocations.SingleAsync()).Z));
        await PlayerLocations.SaveAsync(_db, b.Id, [new LocationEntry(ana.Id, 7, 1, 8)], _now.AddMinutes(2), default);
        var row = await _db.PlayerLocations.SingleAsync(); Assert.Equal(b.Id, row.IslandId); Assert.Equal(7f, row.X);
        Assert.Null(await PlayerLocations.ResumeAsync(_db, ana.Id, a.Id, 256, default)); // she is not on that one any more
        Assert.Equal(new LocationEntry(ana.Id, 7, 1, 8), await PlayerLocations.ResumeAsync(_db, ana.Id, b.Id, 256, default));
        Assert.Null(await PlayerLocations.ResumeAsync(_db, ana.Id, b.Id, 10, default)); // the island got smaller and the spot is outside now
        Assert.Equal(0, await PlayerLocations.SaveAsync(_db, Guid.NewGuid(), [new LocationEntry(ana.Id, 1, 1, 1)], _now, default)); // an island that does not exist
    }

    [Fact]
    public async Task ANewAvatarStartsOnTheWelcomeIsland()
    {
        var owner = NewUser("ana"); var first = NewIsland(owner, "Praia", age: TimeSpan.FromDays(9)); NewIsland(owner, "Floresta");
        var newcomer = NewUser("novo");
        var place = await PlayerLocations.StartAsync(_db, newcomer.Id, _now, default);
        Assert.Equal(first.Id, place.IslandId); Assert.Equal("welcome", place.Reason); Assert.True(place.IsWelcome); Assert.Equal("Praia", place.Name);
    }

    [Fact]
    public async Task ASignedInPersonGoesBackToTheIslandTheyWereOn()
    {
        var owner = NewUser("ana"); NewIsland(owner, "Praia", age: TimeSpan.FromDays(9)); var forest = NewIsland(owner, "Floresta");
        var visitor = NewUser("visitante");
        await PlayerLocations.SaveAsync(_db, forest.Id, [new LocationEntry(visitor.Id, 5, 1, 5)], _now, default);
        var place = await PlayerLocations.StartAsync(_db, visitor.Id, _now, default);
        Assert.Equal(forest.Id, place.IslandId); Assert.Equal("last", place.Reason); Assert.False(place.IsWelcome);
    }

    [Fact]
    public async Task WhenTheLastIslandIsGoneOrClosedThePersonGoesToTheWelcomeIsland()
    {
        var owner = NewUser("ana"); var welcome = NewIsland(owner, "Praia", age: TimeSpan.FromDays(9)); var forest = NewIsland(owner, "Floresta");
        var visitor = NewUser("visitante"); await PlayerLocations.SaveAsync(_db, forest.Id, [new LocationEntry(visitor.Id, 5, 1, 5)], _now, default);
        forest.IsPublic = false; await _db.SaveChangesAsync();
        Assert.Equal(welcome.Id, (await PlayerLocations.StartAsync(_db, visitor.Id, _now, default)).IslandId); // closed: they may not enter
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = forest.Id, UserId = visitor.Id, Permission = "enter", PrincipalType = "user", GrantedByUserId = owner.Id }); await _db.SaveChangesAsync();
        Assert.Equal(forest.Id, (await PlayerLocations.StartAsync(_db, visitor.Id, _now, default)).IslandId); // invited: they may
        _db.IslandPermissions.Single().RevokedAt = _now; forest.DeletedAt = _now; await _db.SaveChangesAsync();
        var place = await PlayerLocations.StartAsync(_db, visitor.Id, _now, default);
        Assert.Equal(welcome.Id, place.IslandId); Assert.Equal("welcome", place.Reason);
        var host = await PlayerLocations.StartAsync(_db, owner.Id, _now, default); Assert.Equal("welcome", host.Reason); // the owner never left the welcome island
    }

    [Fact]
    public async Task TheOwnerOfAClosedIslandGoesBackToIt()
    {
        var owner = NewUser("ana"); NewIsland(owner, "Praia", age: TimeSpan.FromDays(9)); var home = NewIsland(owner, "Minha casa", isPublic: false);
        await PlayerLocations.SaveAsync(_db, home.Id, [new LocationEntry(owner.Id, 3, 1, 3)], _now, default);
        Assert.Equal(home.Id, (await PlayerLocations.StartAsync(_db, owner.Id, _now, default)).IslandId);
    }
}

public sealed class SpawnPlannerTests
{
    [Theory]
    [InlineData(0f, 0f, 256, true)] [InlineData(126f, 0f, 256, true)] [InlineData(127.5f, 0f, 256, false)] [InlineData(-130f, 0f, 256, false)] [InlineData(0f, 500f, 256, false)] [InlineData(float.NaN, 0f, 256, false)] [InlineData(4f, 4f, 10, true)] [InlineData(5f, 0f, 10, false)]
    public void ASpotIsInsideTheIslandWithRoomToTheEdge(float x, float z, int size, bool expected) => Assert.Equal(expected, SpawnPlanner.Inside(x, z, size));

    [Fact]
    public void NewcomersStandInRowsOfEightAroundTheSpawnPoint()
    {
        Assert.Equal((10f, 20f), SpawnPlanner.Around(10, 20, 0)); Assert.Equal((24f, 20f), SpawnPlanner.Around(10, 20, 7)); Assert.Equal((10f, 22f), SpawnPlanner.Around(10, 20, 8));
        var spots = Enumerable.Range(0, 40).Select(i => SpawnPlanner.Around(0, 0, i)).ToList(); Assert.Equal(40, spots.Distinct().Count());
    }

    [Fact]
    public void SomeoneComingBackStandsWhereTheyWereIfItIsStillOnTheIsland()
    {
        Assert.Equal((30f, -12f), SpawnPlanner.Choose(0, 0, 3, (30f, -12f), 256));
        Assert.Equal(SpawnPlanner.Around(5, 5, 3), SpawnPlanner.Choose(5, 5, 3, (900f, 0f), 256)); // outside: the spawn point
        Assert.Equal(SpawnPlanner.Around(5, 5, 3), SpawnPlanner.Choose(5, 5, 3, null, 256)); // never here
    }

    [Theory]
    [InlineData(10f, 10.5f, 10.5f)] [InlineData(10f, 3f, 10f)] [InlineData(10f, 40f, 13f)] [InlineData(10f, -5f, 10f)] [InlineData(10f, float.NaN, 10f)]
    public void TheHeightIsWhereTheyWereButNotUnderTheGroundNorHighInTheAir(float ground, float saved, float expected) => Assert.Equal(expected, SpawnPlanner.Height(ground, saved), .001f);
}
