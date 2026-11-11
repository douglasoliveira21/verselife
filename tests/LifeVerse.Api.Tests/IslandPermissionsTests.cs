using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The Permissões tab: who may change permissions, the limits (nobody edits themselves or the owner, only the owner touches "admin"), roles as sets, and that the changes really take effect.</summary>
public sealed class IslandPermissionsTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly TimeProvider _clock;
    private readonly Guid _owner, _admin, _builder, _visitor, _other, _island;

    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    public IslandPermissionsTests()
    {
        _clock = new Clock(_now);
        (_owner, _admin, _builder, _visitor, _other) = (User("alice"), User("bruno"), User("beto"), User("carla"), User("duda"));
        _island = Guid.NewGuid();
        _db.Islands.Add(new Island { Id = _island, OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, MaxPlayers = 10, CreatedAt = _now, UpdatedAt = _now });
        Grant(_admin, "admin"); Grant(_builder, "build"); Grant(_builder, "run_scripts"); Grant(_builder, "enter");
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private void Grant(Guid user, string permission) => _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island, UserId = user, Permission = permission, PrincipalType = "user", GrantedByUserId = _owner, CreatedAt = _now });

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

    private async Task<(int Status, JsonElement Body)> List(Guid who, string? q = null, int page = 1) => Read(await IslandPermissionManagement.ListAsync(_island, q, page, As(who), _db, _clock, default));
    private async Task<(int Status, JsonElement Body)> Add(Guid who, string name, params string[] permissions) => Read(await IslandPermissionManagement.AddAsync(_island, new PermissionSetRequest(name, permissions), As(who), _db, _clock, default));
    private async Task<(int Status, JsonElement Body)> Change(Guid who, Guid target, params string[] permissions) => Read(await IslandPermissionManagement.ChangeAsync(_island, target, new PermissionChangeRequest(permissions), As(who), _db, _clock, default));
    private async Task<List<string>> Held(Guid user) => await _db.IslandPermissions.AsNoTracking().Where(p => p.UserId == user && p.RevokedAt == null).Select(p => p.Permission).OrderBy(p => p).ToListAsync();

    [Fact]
    public async Task OnlyTheOwnerAndAdministratorsSeeAndChange()
    {
        Assert.Equal(403, (await List(_visitor)).Status); Assert.Equal(403, (await List(_builder)).Status); Assert.Equal(403, (await Add(_builder, "carla", "build")).Status);
        Assert.Equal(200, (await List(_owner)).Status); Assert.Equal(200, (await List(_admin)).Status);
    }

    [Fact]
    public async Task TheListShowsPeopleWithTheirRolesAndTheRolesCountThem()
    {
        var body = (await List(_owner)).Body;
        Assert.Equal(5, body.GetProperty("definitions").GetArrayLength());
        Assert.Equal(2, body.GetProperty("total").GetInt32()); // the administrator and the builder; the owner is a role, not a row
        var items = body.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("username").GetString()!, i => i);
        Assert.Equal("admin", items["bruno"].GetProperty("role").GetString()); Assert.Equal("builder", items["beto"].GetProperty("role").GetString());
        Assert.Equal(new[] { "build", "run_scripts" }, items["beto"].GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToArray());
        var roles = body.GetProperty("roles").EnumerateArray().ToDictionary(r => r.GetProperty("id").GetString()!, r => r.GetProperty("people").GetInt32());
        Assert.Equal((1, 1, 0, 1, 0), (roles["owner"], roles["admin"], roles["moderator"], roles["builder"], roles["visitor"]));
        Assert.DoesNotContain("enter", items["beto"].GetProperty("permissions").EnumerateArray().Select(p => p.GetString())); // coming in is the Acesso tab
    }

    [Fact]
    public async Task APersonIsAddedWithASetOfPermissionsAndTheChangeTakesEffectOnTheRealCheck()
    {
        var (status, body) = await Add(_owner, "CARLA", "ban"); Assert.Equal(200, status); Assert.Equal("moderator", body.GetProperty("role").GetString());
        Assert.Contains("ban", await Held(_visitor));
        Assert.Equal(200, (await Change(_admin, _visitor, "build", "edit_terrain")).Status);
        Assert.Equal(new[] { "build", "edit_terrain" }, await Held(_visitor));
        Assert.Equal("custom", (await Change(_admin, _visitor, "build", "edit_terrain")).Body.GetProperty("role").GetString());
        Assert.Equal(409, (await Add(_owner, "carla", "build")).Status); // already there: adjust in the list
    }

    [Fact]
    public async Task PermissionsCanBeTakenAwayOneByOneOrAllAtOnce()
    {
        Assert.Equal(200, (await Change(_owner, _builder, "build")).Status); Assert.DoesNotContain("run_scripts", await Held(_builder));
        Assert.Equal(204, Read(await IslandPermissionManagement.ClearAsync(_island, _builder, As(_admin), _db, _clock, default)).Status);
        Assert.Equal(new[] { "enter" }, await Held(_builder)); // the right to come in is not touched here
        Assert.Equal(404, Read(await IslandPermissionManagement.ClearAsync(_island, _builder, As(_admin), _db, _clock, default)).Status);
    }

    [Fact]
    public async Task NobodyEditsThemselvesOrTheOwner_AndOnlyTheOwnerTouchesAdmin()
    {
        Assert.Equal(409, (await Change(_admin, _admin, "build")).Status);
        Assert.Equal(409, (await Add(_admin, "alice", "build")).Status);
        Assert.Equal(409, (await Add(_admin, "carla", "admin")).Status); Assert.Empty(await Held(_visitor));
        Assert.Equal(409, (await Change(_admin, _builder, "admin")).Status); Assert.DoesNotContain("admin", await Held(_builder));
        Assert.Equal(200, (await Add(_owner, "carla", "admin")).Status); Assert.Equal(new[] { "admin" }, await Held(_visitor));
        Assert.Equal(409, (await Change(_admin, _visitor, "build")).Status); // an administrator cannot change another administrator
        Assert.Equal(200, (await Change(_owner, _visitor, "ban")).Status);
    }

    [Fact]
    public async Task BadRequestsAreRefusedWithAReason()
    {
        Assert.Equal(404, (await Add(_owner, "ninguem", "build")).Status);
        Assert.True((await Add(_owner, "  ", "build")).Status is 400 or 422); Assert.True((await Add(_owner, "carla")).Status is 400 or 422); Assert.True((await Add(_owner, "carla", "dar_tudo")).Status is 400 or 422);
        Assert.Equal(404, (await Change(_owner, _visitor, "build")).Status); // not on the list yet
    }

    [Fact]
    public async Task AnOldEditPermissionCountsAsBuildAndAnExpiredOneDoesNotCount()
    {
        Grant(_other, "edit"); await _db.SaveChangesAsync();
        var row = (await List(_owner)).Body.GetProperty("items").EnumerateArray().First(i => i.GetProperty("username").GetString() == "duda");
        Assert.Equal(new[] { "build" }, row.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToArray());
        Assert.Equal(200, (await Change(_owner, _other, "ban")).Status); Assert.Equal(new[] { "ban" }, await Held(_other)); // the old name is dropped when build is taken away
        (await _db.IslandPermissions.SingleAsync(p => p.UserId == _other && p.RevokedAt == null)).ExpiresAt = _now.AddDays(-1); await _db.SaveChangesAsync();
        Assert.Equal(404, (await Change(_owner, _other, "build")).Status);
    }

    [Fact]
    public async Task SearchAndPaging()
    {
        for (var i = 0; i < 12; i++) { User($"pessoa{i:00}"); Assert.Equal(200, (await Add(_owner, $"pessoa{i:00}", "build")).Status); }
        var first = (await List(_owner)).Body; Assert.Equal(14, first.GetProperty("total").GetInt32()); Assert.Equal(10, first.GetProperty("items").GetArrayLength());
        Assert.Equal(4, (await List(_owner, page: 2)).Body.GetProperty("items").GetArrayLength());
        Assert.Equal(1, (await List(_owner, "pessoa05")).Body.GetProperty("total").GetInt32());
    }
}
