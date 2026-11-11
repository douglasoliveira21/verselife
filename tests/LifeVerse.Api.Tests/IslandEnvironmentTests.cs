using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The Ambiente tab: what is accepted as a look, who may change it, the conflict between two administrators, the saved looks, and who the game may hand the look to.</summary>
public sealed class IslandEnvironmentTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly TimeProvider _clock;
    private readonly Guid _owner, _admin, _visitor, _banned, _island;

    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    public IslandEnvironmentTests()
    {
        _clock = new Clock(_now);
        (_owner, _admin, _visitor, _banned) = (User("alice"), User("bruno"), User("carla"), User("duda"));
        _island = Guid.NewGuid();
        _db.Islands.Add(new Island { Id = _island, OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, MaxPlayers = 10, CreatedAt = _now, UpdatedAt = _now.AddDays(-1) });
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island, UserId = _admin, Permission = "admin", PrincipalType = "user", GrantedByUserId = _owner, CreatedAt = _now });
        _db.IslandBans.Add(new IslandBan { Id = Guid.NewGuid(), IslandId = _island, UserId = _banned, BannedByUserId = _owner, Reason = "x", CreatedAt = _now });
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

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private async Task<(int Status, JsonElement Body)> Get(Guid who) => Read(await IslandEnvironmentManagement.GetAsync(_island, As(who), _db, _clock, default));
    private async Task<(int Status, JsonElement Body)> Save(Guid who, string look, DateTimeOffset? expected = null) =>
        Read(await IslandEnvironmentManagement.SaveAsync(_island, new LookRequest(J(look), expected ?? (await _db.Islands.AsNoTracking().SingleAsync()).UpdatedAt), As(who), _db, _clock, default));
    private async Task<(int Status, JsonElement Body)> Preset(Guid who, string name, string look) => Read(await IslandEnvironmentManagement.SavePresetAsync(_island, new PresetRequest(name, J(look)), As(who), _db, _clock, default));

    [Fact]
    public async Task OnlyStaffReadAndChangeTheSettings()
    {
        Assert.Equal(403, (await Get(_visitor)).Status); Assert.Equal(403, (await Save(_visitor, "{\"hour\":3}")).Status); Assert.Equal(403, (await Preset(_visitor, "x", "{}")).Status);
        Assert.Equal(200, (await Get(_owner)).Status); Assert.Equal(200, (await Get(_admin)).Status);
    }

    [Fact]
    public async Task ALookIsSavedCleanAndTheOldOneIsKeptInTheAuditTrail()
    {
        var (status, body) = await Save(_admin, "{\"hour\":18.456,\"weather\":\"rain\",\"fog\":0.4,\"light\":1.2,\"sky\":\"#ff9a5a\"}");
        Assert.Equal(200, status);
        var look = body.GetProperty("look"); Assert.Equal(18.46, look.GetProperty("hour").GetDouble(), 2); Assert.Equal("rain", look.GetProperty("weather").GetString()); Assert.Equal("#FF9A5A", look.GetProperty("sky").GetString());
        Assert.Equal("{\"hour\":18.46,\"weather\":\"rain\",\"fog\":0.4,\"light\":1.2,\"sky\":\"#FF9A5A\"}", (await _db.Islands.AsNoTracking().SingleAsync()).LookJson);
        var entry = await _db.IslandAudit.SingleAsync(); Assert.Equal("ambiente", entry.Category); Assert.Equal("{}", entry.BeforeJson); Assert.Contains("rain", entry.AfterJson);
        Assert.Equal(200, (await Save(_owner, "{}")).Status); // back to the game's own sky
        Assert.Equal("{}", (await _db.Islands.AsNoTracking().SingleAsync()).LookJson); Assert.Contains("padrão", (await _db.IslandAudit.OrderBy(e => e.CreatedAt).LastAsync()).Summary);
    }

    [Fact]
    public async Task WrongValuesAreRefusedWithAReasonAndNothingChanges()
    {
        foreach (var bad in new[] { "{\"hour\":25}", "{\"hour\":-1}", "{\"hour\":\"noon\"}", "{\"fog\":1.5}", "{\"light\":0.1}", "{\"light\":2}", "{\"weather\":\"snow\"}", "{\"weather\":5}", "{\"sky\":\"red\"}", "{\"sky\":\"#12345\"}", "{\"sky\":\"#GGGGGG\"}", "{\"wind\":3}", "[1]", "\"x\"", "5" })
            Assert.True((await Save(_owner, bad)).Status is 400 or 422, bad);
        Assert.Equal("{}", (await _db.Islands.AsNoTracking().SingleAsync()).LookJson); Assert.Equal(0, await _db.IslandAudit.CountAsync());
    }

    [Fact]
    public async Task ASaveOverANewerVersionIsRefused()
    {
        var old = (await _db.Islands.AsNoTracking().SingleAsync()).UpdatedAt;
        Assert.Equal(200, (await Save(_admin, "{\"hour\":3}")).Status);
        var (status, body) = await Save(_owner, "{\"hour\":20}", old);
        Assert.Equal(409, status); Assert.Equal("stale", body.GetProperty("error").GetString()); Assert.Contains("3", (await _db.Islands.AsNoTracking().SingleAsync()).LookJson);
    }

    [Fact]
    public async Task TheGameReadsTheLookOnlyIfThePersonMayEnter()
    {
        await Save(_owner, "{\"hour\":3}");
        var look = Read(await IslandEnvironmentManagement.ReadAsync(_island, As(_visitor), _db, _clock, default)); Assert.Equal(200, look.Status); Assert.Equal(3, look.Body.GetProperty("look").GetProperty("hour").GetInt32());
        Assert.Equal(403, Read(await IslandEnvironmentManagement.ReadAsync(_island, As(_banned), _db, _clock, default)).Status);
        (await _db.Islands.SingleAsync()).AccessClosed = true; await _db.SaveChangesAsync();
        Assert.Equal(403, Read(await IslandEnvironmentManagement.ReadAsync(_island, As(_visitor), _db, _clock, default)).Status); Assert.Equal(200, Read(await IslandEnvironmentManagement.ReadAsync(_island, As(_admin), _db, _clock, default)).Status);
    }

    [Fact]
    public async Task SavedLooksCanBeAddedReplacedAndDeleted_WithinLimits()
    {
        Assert.Equal(6, (await Get(_owner)).Body.GetProperty("builtIn").GetArrayLength());
        Assert.Equal(200, (await Preset(_admin, "Meu pôr do sol", "{\"hour\":19,\"sky\":\"#FF8800\"}")).Status);
        var again = (await Preset(_owner, "meu pôr do sol", "{\"hour\":20}")).Body; Assert.Equal(1, again.GetProperty("custom").GetArrayLength()); Assert.Equal(20, again.GetProperty("custom")[0].GetProperty("look").GetProperty("hour").GetInt32());
        Assert.Equal(409, (await Preset(_owner, "Noite", "{}")).Status); // the name of a look of the game
        Assert.True((await Preset(_owner, "  ", "{}")).Status is 400 or 422); Assert.True((await Preset(_owner, new string('n', 41), "{}")).Status is 400 or 422); Assert.True((await Preset(_owner, "Ruim", "{\"hour\":99}")).Status is 400 or 422);
        for (var i = 1; i < IslandEnvironmentManagement.MaxPresets; i++) Assert.Equal(200, (await Preset(_owner, "P" + i, "{}")).Status);
        Assert.Equal(409, (await Preset(_owner, "Demais", "{}")).Status);
        Assert.Equal(204, Read(await IslandEnvironmentManagement.DeletePresetAsync(_island, "P1", As(_admin), _db, _clock, default)).Status);
        Assert.Equal(404, Read(await IslandEnvironmentManagement.DeletePresetAsync(_island, "P1", As(_admin), _db, _clock, default)).Status);
        Assert.Equal(403, Read(await IslandEnvironmentManagement.DeletePresetAsync(_island, "P2", As(_visitor), _db, _clock, default)).Status);
        Assert.Equal(200, (await Preset(_owner, "Demais", "{}")).Status);
        Assert.Contains(await _db.IslandAudit.Select(e => e.Action).ToListAsync(), a => a == "preset_deleted");
    }
}
