using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The Acesso tab: the mode of the island, the list of people allowed in with an expiry, and the rule that decides who may come in.</summary>
public sealed class IslandAccessTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly TimeProvider _clock;
    private readonly Guid _owner, _admin, _guest, _stranger, _island;

    private sealed class Clock(Func<DateTimeOffset> now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now(); }

    public IslandAccessTests()
    {
        _clock = new Clock(() => _now);
        (_owner, _admin, _guest, _stranger) = (User("alice"), User("bruno"), User("carla"), User("duda"));
        _island = Guid.NewGuid();
        _db.Islands.Add(new Island { Id = _island, OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = false, MaxPlayers = 10, CreatedAt = _now, UpdatedAt = _now });
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island, UserId = _admin, Permission = "admin", PrincipalType = "user", GrantedByUserId = _owner, CreatedAt = _now });
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

    private async Task<(int, JsonElement)> List(Guid who, string? q = null, int page = 1) => Read(await IslandAccessManagement.GetAsync(_island, q, page, As(who), _db, _clock, default));
    private async Task<(int, JsonElement)> Allow(Guid who, string name, DateTimeOffset? until = null) => Read(await IslandAccessManagement.AllowAsync(_island, new AllowRequest(name, until), As(who), _db, _clock, default));
    private async Task<bool> CanEnter(Guid who) => await IslandAccess.AllowsAsync(_db, await _db.Islands.AsNoTracking().SingleAsync(), who, _now, default);
    private async Task<(int, JsonElement)> Mode(Guid who, string mode) => Read(await IslandAccessManagement.SetModeAsync(_island, new AccessModeRequest(mode, (await _db.Islands.AsNoTracking().SingleAsync()).UpdatedAt), As(who), _db, _clock, default));

    [Fact]
    public async Task OnlyTheOwnerAndAdministratorsSeeAndChangeTheAccess()
    {
        Assert.Equal(403, (await List(_stranger)).Item1); Assert.Equal(403, (await Allow(_stranger, "carla")).Item1); Assert.Equal(403, (await Mode(_guest, "public")).Item1);
        Assert.Equal(200, (await List(_admin)).Item1); Assert.Equal(200, (await List(_owner)).Item1);
    }

    [Fact]
    public async Task APrivateIslandLetsInOnlyThePeopleOnTheList()
    {
        Assert.Equal("invite", (await List(_owner)).Item2.GetProperty("mode").GetString());
        Assert.False(await CanEnter(_guest)); Assert.True(await CanEnter(_owner)); Assert.True(await CanEnter(_admin));
        Assert.Equal(200, (await Allow(_owner, "CARLA")).Item1);
        Assert.True(await CanEnter(_guest)); Assert.False(await CanEnter(_stranger));
    }

    [Fact]
    public async Task AnEntryThatExpiredNoLongerLetsAnyoneIn()
    {
        Assert.Equal(200, (await Allow(_owner, "carla", _now.AddHours(2))).Item1);
        Assert.True(await CanEnter(_guest));
        _now = _now.AddHours(3);
        Assert.False(await CanEnter(_guest));
        Assert.Equal(0, (await List(_owner)).Item2.GetProperty("total").GetInt32()); // and it leaves the list
    }

    [Fact]
    public async Task ClosingBlocksEveryoneButStaffAndOpeningAgainBringsBackTheOldMode()
    {
        Assert.Equal(200, (await Mode(_owner, "public")).Item1); Assert.True(await CanEnter(_stranger));
        Assert.Equal(200, (await Mode(_admin, "closed")).Item1);
        Assert.False(await CanEnter(_stranger)); Assert.True(await CanEnter(_owner)); Assert.True(await CanEnter(_admin));
        Assert.Equal("closed", (await List(_owner)).Item2.GetProperty("mode").GetString());
        await Allow(_owner, "carla"); Assert.False(await CanEnter(_guest), "a person on the list cannot come in while it is closed");
        Assert.Equal(200, (await Mode(_owner, "public")).Item1); Assert.True(await CanEnter(_stranger));
    }

    [Fact]
    public async Task TheListHasSearchPagesAndRemoval()
    {
        for (var i = 0; i < 12; i++) { User($"pessoa{i:00}"); Assert.Equal(200, (await Allow(_owner, $"pessoa{i:00}")).Item1); }
        var (_, first) = await List(_owner); Assert.Equal(12, first.GetProperty("total").GetInt32()); Assert.Equal(10, first.GetProperty("items").GetArrayLength());
        Assert.Equal(2, (await List(_owner, page: 2)).Item2.GetProperty("items").GetArrayLength());
        var found = (await List(_owner, "pessoa07")).Item2; Assert.Equal(1, found.GetProperty("total").GetInt32());
        var id = found.GetProperty("items")[0].GetProperty("permissionId").GetGuid();
        Assert.Equal(204, Read(await IslandAccessManagement.RemoveAsync(_island, id, As(_owner), _db, _clock, default)).Status);
        Assert.Equal(11, (await List(_owner)).Item2.GetProperty("total").GetInt32());
        Assert.Equal(404, Read(await IslandAccessManagement.RemoveAsync(_island, id, As(_owner), _db, _clock, default)).Status);
    }

    [Fact]
    public async Task AddingAgainSomeoneWhoseEntryExpiredRenewsItInsteadOfMakingASecondOne()
    {
        Assert.Equal(200, (await Allow(_owner, "carla", _now.AddHours(1))).Item1);
        _now = _now.AddHours(2); Assert.False(await CanEnter(_guest));
        Assert.Equal(200, (await Allow(_owner, "carla")).Item1); Assert.True(await CanEnter(_guest));
        Assert.Equal(1, await _db.IslandPermissions.CountAsync(p => p.UserId == _guest && p.Permission == "enter" && p.RevokedAt == null));
    }

    [Fact]
    public async Task BadRequestsAreRefusedWithAReason()
    {
        Assert.Equal(404, (await Allow(_owner, "ninguem")).Item1);
        Assert.Equal(409, (await Allow(_owner, "alice")).Item1);
        Assert.Equal(400, (await Allow(_owner, "  ")).Item1 is 400 or 422 ? 400 : 0);
        Assert.True((await Allow(_owner, "carla", _now.AddDays(-1))).Item1 is 400 or 422);
        Assert.Equal(200, (await Allow(_owner, "carla")).Item1); Assert.Equal(409, (await Allow(_owner, "carla")).Item1);
        Assert.True((await Mode(_owner, "banana")).Item1 is 400 or 422);
    }

    [Fact]
    public async Task ChangingTheExpiryWorksAndASaveOverANewerVersionIsRefused()
    {
        var added = (await Allow(_owner, "carla", _now.AddDays(1))).Item2; var id = added.GetProperty("permissionId").GetGuid();
        var changed = Read(await IslandAccessManagement.ChangeAsync(_island, id, new AllowChangeRequest(null), As(_owner), _db, _clock, default));
        Assert.Equal(200, changed.Status); Assert.Equal(JsonValueKind.Null, changed.Body.GetProperty("expiresAt").ValueKind);
        var stale = Read(await IslandAccessManagement.SetModeAsync(_island, new AccessModeRequest("public", _now.AddDays(-3)), As(_owner), _db, _clock, default));
        Assert.Equal(409, stale.Status); Assert.False((await _db.Islands.AsNoTracking().SingleAsync()).IsPublic);
    }
}
