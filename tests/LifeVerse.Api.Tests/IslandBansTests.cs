using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The Banimentos tab: who may ban, who cannot be banned, how a ban ends, the history and the counters, and that a ban really keeps a person out.</summary>
public sealed class IslandBansTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly TimeProvider _clock;
    private readonly Guid _owner, _admin, _moderator, _visitor, _other, _island;

    private sealed class Clock(Func<DateTimeOffset> now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now(); }

    public IslandBansTests()
    {
        _clock = new Clock(() => _now);
        (_owner, _admin, _moderator, _visitor, _other) = (User("alice"), User("bruno"), User("moda"), User("carla"), User("duda"));
        _island = Guid.NewGuid();
        _db.Islands.Add(new Island { Id = _island, OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, MaxPlayers = 10, CreatedAt = _now, UpdatedAt = _now });
        foreach (var (user, permission) in new[] { (_admin, "admin"), (_moderator, "ban") }) _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island, UserId = user, Permission = permission, PrincipalType = "user", GrantedByUserId = _owner, CreatedAt = _now });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Guid User(string name)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now });
        _db.SaveChanges(); return id;
    }

    private static HttpContext As(Guid user) => new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, user.ToString())], "test")) };

    private static (int Status, JsonElement Body) Read(IResult result)
    {
        var status = result is Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult ? 403 : (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
        var value = (result as IValueHttpResult)?.Value;
        return (status, value is null ? default : JsonSerializer.SerializeToElement(value));
    }

    private async Task<(int Status, JsonElement Body)> List(Guid who, string? q = null, string? status = null, int page = 1) => Read(await IslandBanManagement.ListAsync(_island, q, status, page, As(who), _db, _clock, default));
    private async Task<(int Status, JsonElement Body)> Ban(Guid who, string name, string reason = "Spam", DateTimeOffset? until = null) => Read(await IslandBanManagement.BanAsync(_island, new BanRequest(name, reason, until), As(who), _db, _clock, default));
    private async Task<bool> CanEnter(Guid who) => await IslandAccess.AllowsAsync(_db, await _db.Islands.AsNoTracking().SingleAsync(), who, _now, default);

    [Fact]
    public async Task OnlyTheOwnerAdministratorsAndModeratorsSee_AndStrangersDoNot()
    {
        Assert.Equal(403, (await List(_visitor)).Status); Assert.Equal(403, (await Ban(_visitor, "duda")).Status);
        foreach (var who in new[] { _owner, _admin, _moderator }) Assert.Equal(200, (await List(who)).Status);
    }

    [Fact]
    public async Task ABanKeepsThePersonOutUntilItIsLifted()
    {
        Assert.True(await CanEnter(_visitor));
        var (status, body) = await Ban(_moderator, "CARLA", "Xingamentos");
        Assert.Equal(200, status); Assert.Equal("active", body.GetProperty("status").GetString()); Assert.Equal("moda", body.GetProperty("bannedBy").GetString());
        Assert.False(await CanEnter(_visitor)); Assert.True(await CanEnter(_other));
        var id = body.GetProperty("banId").GetGuid();
        Assert.Equal(204, Read(await IslandBanManagement.LiftAsync(_island, id, As(_admin), _db, _clock, default)).Status);
        Assert.True(await CanEnter(_visitor));
        Assert.Equal(404, Read(await IslandBanManagement.LiftAsync(_island, id, As(_admin), _db, _clock, default)).Status);
    }

    [Fact]
    public async Task ATemporaryBanEndsByItselfAndStaysInTheHistory()
    {
        Assert.Equal(200, (await Ban(_owner, "carla", "Pausa", _now.AddHours(2))).Status);
        Assert.False(await CanEnter(_visitor));
        _now = _now.AddHours(3);
        Assert.True(await CanEnter(_visitor));
        var list = (await List(_owner)).Body; Assert.Equal("expired", list.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal(1, list.GetProperty("counts").GetProperty("expired").GetInt32()); Assert.Equal(0, list.GetProperty("counts").GetProperty("active").GetInt32());
        Assert.Equal(200, (await Ban(_owner, "carla", "De novo")).Status); // after it ended the person can be banned again
    }

    [Fact]
    public async Task OwnerAdministratorsAndOneselfCannotBeBanned_AndNeitherCanAPersonTwice()
    {
        Assert.Equal(409, (await Ban(_admin, "alice")).Status); Assert.Equal(409, (await Ban(_moderator, "bruno")).Status); Assert.Equal(409, (await Ban(_moderator, "moda")).Status);
        Assert.Equal(200, (await Ban(_owner, "carla")).Status); Assert.Equal(409, (await Ban(_owner, "carla")).Status);
        Assert.True(await CanEnter(_owner) && await CanEnter(_admin));
    }

    [Fact]
    public async Task BadRequestsAreRefusedWithAReason()
    {
        Assert.Equal(404, (await Ban(_owner, "ninguem")).Status);
        foreach (var bad in new[] { await Ban(_owner, "  "), await Ban(_owner, "carla", "   "), await Ban(_owner, "carla", new string('x', 201)), await Ban(_owner, "carla", "x", _now.AddDays(-1)) }) Assert.True(bad.Status is 400 or 422);
        Assert.Equal(0, await _db.IslandBans.CountAsync());
    }

    [Fact]
    public async Task TheDurationCanBeChangedAndTheListHasCountersFiltersSearchAndPages()
    {
        var first = (await Ban(_owner, "carla", "Spam", _now.AddDays(1))).Body.GetProperty("banId").GetGuid();
        Assert.Equal(200, Read(await IslandBanManagement.ChangeAsync(_island, first, new BanChangeRequest(null), As(_owner), _db, _clock, default)).Status);
        var counts = (await List(_owner)).Body.GetProperty("counts"); Assert.Equal(1, counts.GetProperty("permanent").GetInt32()); Assert.Equal(0, counts.GetProperty("temporary").GetInt32());
        for (var i = 0; i < 12; i++) { var name = $"pessoa{i:00}"; User(name); Assert.Equal(200, (await Ban(_owner, name, i % 2 == 0 ? "Spam" : "Fraude", _now.AddDays(1))).Status); }
        var (_, all) = await List(_owner); Assert.Equal(13, all.GetProperty("total").GetInt32()); Assert.Equal(10, all.GetProperty("items").GetArrayLength());
        Assert.Equal(3, (await List(_owner, page: 2)).Body.GetProperty("items").GetArrayLength());
        Assert.Equal(1, (await List(_owner, "pessoa07")).Body.GetProperty("total").GetInt32());
        Assert.Equal(6, (await List(_owner, "fraude")).Body.GetProperty("total").GetInt32()); // the reason is searched too
        var lifted = (await List(_owner, "pessoa03")).Body.GetProperty("items")[0].GetProperty("banId").GetGuid();
        await IslandBanManagement.LiftAsync(_island, lifted, As(_owner), _db, _clock, default);
        Assert.Equal(1, (await List(_owner, status: "lifted")).Body.GetProperty("total").GetInt32());
        Assert.Equal(12, (await List(_owner, status: "active")).Body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task APrivateIslandAlsoKeepsOutTheBannedEvenIfTheyAreOnTheList()
    {
        (await _db.Islands.SingleAsync()).IsPublic = false;
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island, UserId = _visitor, Permission = "enter", PrincipalType = "user", GrantedByUserId = _owner, CreatedAt = _now });
        await _db.SaveChangesAsync(); Assert.True(await CanEnter(_visitor));
        await Ban(_owner, "carla"); Assert.False(await CanEnter(_visitor));
    }
}
