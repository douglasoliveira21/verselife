using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The General tab of the island management window: who may read and save it, what is validated, the conflict between two administrators, and what the search shows.</summary>
public sealed class IslandManagementTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly TimeProvider _clock = new FixedClock(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _owner, _admin, _stranger, _island;

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    public IslandManagementTests()
    {
        _owner = User("alice"); _admin = User("bruno"); _stranger = User("carla");
        _island = Guid.NewGuid();
        _db.Islands.Add(new Island { Id = _island, OwnerUserId = _owner, Name = "Ilha Tropical", Slug = "tropical", Description = "Praias.", IsPublic = true, MaxPlayers = 60, Size = 256, CreatedAt = _clock.GetUtcNow().AddDays(-30), UpdatedAt = _clock.GetUtcNow().AddDays(-1) });
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island, UserId = _admin, Permission = "admin", PrincipalType = "user", GrantedByUserId = _owner, CreatedAt = _clock.GetUtcNow() });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Guid User(string name)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _clock.GetUtcNow() });
        _db.SaveChanges(); return id;
    }

    private static HttpContext As(Guid user) => new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, user.ToString())], "test")) };

    private static (int Status, JsonElement Body) Read(IResult result)
    {
        var status = result is Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult ? 403 : (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
        var value = (result as IValueHttpResult)?.Value;
        return (status, value is null ? default : JsonSerializer.SerializeToElement(value));
    }

    private async Task<(int Status, JsonElement Body)> Get(Guid user) => Read(await IslandManagement.GetAsync(_island, As(user), _db, _clock, default));

    private async Task<(int Status, JsonElement Body)> Save(Guid user, Func<IslandGeneralRequest, IslandGeneralRequest>? change = null)
    {
        var island = await _db.Islands.AsNoTracking().SingleAsync(i => i.Id == _island);
        var request = new IslandGeneralRequest(island.Name, island.Description, island.CoverImageUrl, "social", "livre", "pt-BR", island.MaxPlayers, true, true, 0, 0, island.UpdatedAt);
        return Read(await IslandManagement.SaveGeneralAsync(_island, change?.Invoke(request) ?? request, As(user), _db, _clock, default));
    }

    [Fact]
    public async Task TheOwnerSeesTheRealNumbersOfTheIsland()
    {
        var (status, body) = await Get(_owner);
        Assert.Equal(200, status);
        Assert.Equal("Ilha Tropical", body.GetProperty("name").GetString()); Assert.Equal("alice", body.GetProperty("ownerName").GetString());
        Assert.Equal(2, body.GetProperty("adminCount").GetInt32()); // the owner and one administrator
        Assert.True(body.GetProperty("canEdit").GetBoolean()); Assert.Equal("offline", body.GetProperty("state").GetString()); Assert.Equal(0, body.GetProperty("visitors").GetInt32());
        Assert.Equal(0, body.GetProperty("usage").GetProperty("cost").GetInt32());
    }

    [Fact]
    public async Task APrivateIslandIsClosedToStrangersAndAPublicOneIsReadOnlyForThem()
    {
        Assert.False((await Get(_stranger)).Body.GetProperty("canEdit").GetBoolean());
        (await _db.Islands.SingleAsync()).IsPublic = false; await _db.SaveChangesAsync();
        Assert.Equal(403, (await Get(_stranger)).Status);
        Assert.Equal(200, (await Get(_admin)).Status);
    }

    [Fact]
    public async Task OnlyTheOwnerAndAdministratorsSave_AndTheChangesAreKept()
    {
        Assert.Equal(403, (await Save(_stranger)).Status);
        var (status, body) = await Save(_admin, r => r with { Name = "  Ilha Nova  ", Description = "Nova.", ContentRating = "moderado", Language = "en-US", MaxPlayers = 40, ShowInSearch = false, ShowOnMap = false, SpawnX = 10, SpawnZ = -5 });
        Assert.Equal(200, status);
        var island = await _db.Islands.AsNoTracking().SingleAsync();
        Assert.Equal("Ilha Nova", island.Name); Assert.Equal("moderado", island.ContentRating); Assert.Equal("en-US", island.Language); Assert.Equal(40, island.MaxPlayers);
        Assert.False(island.ShowInSearch); Assert.False(island.ShowOnMap); Assert.Equal((10f, -5f), (island.SpawnX, island.SpawnZ));
        Assert.Equal("Ilha Nova", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task InvalidValuesAreRefusedAndNothingChanges()
    {
        var long48 = new string('a', 49);
        foreach (var change in new Func<IslandGeneralRequest, IslandGeneralRequest>[]
        {
            r => r with { Name = "   " }, r => r with { Name = long48 }, r => r with { Description = new string('d', 501) }, r => r with { Category = "xyz" }, r => r with { ContentRating = "x" },
            r => r with { Language = "klingon" }, r => r with { MaxPlayers = 0 }, r => r with { MaxPlayers = 257 }, r => r with { SpawnX = float.NaN }, r => r with { SpawnZ = 99999 },
        })
        {
            var (status, _) = await Save(_owner, change);
            Assert.True(status is 400 or 422, $"status {status}");
        }
        Assert.Equal("Ilha Tropical", (await _db.Islands.AsNoTracking().SingleAsync()).Name);
    }

    [Fact]
    public async Task ASaveBasedOnAnOlderVersionIsRefusedInsteadOfOverwritingTheNewerOne()
    {
        var stale = (await _db.Islands.AsNoTracking().SingleAsync()).UpdatedAt;
        Assert.Equal(200, (await Save(_admin, r => r with { Name = "Do Bruno" })).Status);
        var (status, body) = await Save(_owner, r => r with { Name = "Da Alice", ExpectedUpdatedAt = stale });
        Assert.Equal(409, status); Assert.Equal("stale", body.GetProperty("error").GetString());
        Assert.Equal("Do Bruno", (await _db.Islands.AsNoTracking().SingleAsync()).Name);
    }

    [Fact]
    public async Task TheSwitchesDecideWhetherTheIslandShowsInSearchAndOnTheMap()
    {
        Assert.True((await _db.Islands.AsNoTracking().SingleAsync()).ShowInSearch);
        Assert.Equal(200, (await Save(_owner, r => r with { ShowInSearch = false })).Status);
        var island = await _db.Islands.AsNoTracking().SingleAsync();
        Assert.False(island.ShowInSearch); Assert.True(island.IsPublic); // public to those who know it, but not listed
    }
}
