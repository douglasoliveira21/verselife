using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Loading the objects of an island is part of entering it: whoever may enter gets the list (a visitor of a public island too), whoever may not is turned away.</summary>
public sealed class BuildListAccessTests : IDisposable
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 17, 20, 0, 0, TimeSpan.Zero);
    private readonly Guid _owner = Guid.NewGuid(), _visitor = Guid.NewGuid();
    public void Dispose() => _db.Dispose();

    private Island NewIsland(bool isPublic = true, bool closed = false)
    {
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Praça", Slug = "p" + Guid.NewGuid().ToString("N")[..6], IsPublic = isPublic, AccessClosed = closed, Size = 256 };
        _db.Islands.Add(island); _db.BuildObjects.Add(new BuildObject { Id = Guid.NewGuid(), IslandId = island.Id, CreatorUserId = _owner, OwnerUserId = _owner, Name = "Banco", PrimitiveType = "cube" }); _db.SaveChanges(); return island;
    }

    private async Task<int> List(Island island, Guid user)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, user.ToString())], "test")) };
        var result = await BuildEndpoints.ListAsync(island.Id, context, _db, new FixedClock(_now), default);
        return result is ForbidHttpResult ? 403 : result is IStatusCodeHttpResult { StatusCode: { } code } ? code : result is IValueHttpResult ? 200 : 0;
    }

    [Fact] public async Task AVisitorOfAPublicIslandGetsTheObjects() => Assert.Equal(200, await List(NewIsland(), _visitor));
    [Fact] public async Task TheOwnerAlwaysGetsThem() => Assert.Equal(200, await List(NewIsland(isPublic: false, closed: true), _owner));
    [Fact] public async Task AnIslandByInvitationTurnsAwayWhoWasNotInvited() => Assert.Equal(403, await List(NewIsland(isPublic: false), _visitor));

    [Fact]
    public async Task AnInvitedPersonGetsThemAndALapsedInvitationDoesNot()
    {
        var island = NewIsland(isPublic: false);
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = island.Id, UserId = _visitor, Permission = "enter", PrincipalType = "user", GrantedByUserId = _owner, CreatedAt = _now.AddDays(-2), ExpiresAt = _now.AddDays(1) }); _db.SaveChanges();
        Assert.Equal(200, await List(island, _visitor));
        _db.IslandPermissions.Single().ExpiresAt = _now.AddMinutes(-1); _db.SaveChanges();
        Assert.Equal(403, await List(island, _visitor));
    }

    [Fact] public async Task AClosedIslandTurnsEveryoneAwayButTheOwner() => Assert.Equal(403, await List(NewIsland(closed: true), _visitor));

    [Fact]
    public async Task ABannedPersonIsTurnedAwayEvenFromAPublicIsland()
    {
        var island = NewIsland(); _db.IslandBans.Add(new IslandBan { Id = Guid.NewGuid(), IslandId = island.Id, UserId = _visitor, BannedByUserId = _owner, Reason = "teste", CreatedAt = _now.AddDays(-1) }); _db.SaveChanges();
        Assert.Equal(403, await List(island, _visitor));
    }

    [Fact] public async Task AnIslandThatDoesNotExistIsNotFound() => Assert.Equal(404, await List(new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "x", Slug = "x", Size = 1 }, _visitor));
}
