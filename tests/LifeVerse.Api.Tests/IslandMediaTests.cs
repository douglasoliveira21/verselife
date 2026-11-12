using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The Mídia tab: the music of the island, the rules of the address (the player's computer downloads it), who may change it, and that the game receives it.</summary>
public sealed class IslandMediaTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly TimeProvider _clock;
    private readonly Guid _owner, _admin, _visitor, _island;

    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    public IslandMediaTests()
    {
        _clock = new Clock(_now);
        (_owner, _admin, _visitor) = (User("alice"), User("bruno"), User("carla"));
        _island = Guid.NewGuid();
        _db.Islands.Add(new Island { Id = _island, OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, MaxPlayers = 10, CreatedAt = _now, UpdatedAt = _now.AddDays(-1) });
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

    private async Task<(int Status, JsonElement Body)> Get(Guid who) => Read(await IslandMediaManagement.GetAsync(_island, As(who), _db, _clock, default));
    private async Task<(int Status, JsonElement Body)> Save(Guid who, string? url, float volume = .5f, bool autoplay = true, string? name = "Rádio", DateTimeOffset? expected = null) =>
        Read(await IslandMediaManagement.SaveAsync(_island, new MediaRequest(url, volume, autoplay, name, expected ?? (await _db.Islands.AsNoTracking().SingleAsync()).UpdatedAt), As(who), _db, _clock, default));

    [Fact]
    public async Task OnlyStaffReadAndChange()
    {
        Assert.Equal(403, (await Get(_visitor)).Status); Assert.Equal(403, (await Save(_visitor, "https://cdn.example.com/a.mp3")).Status);
        Assert.Equal(200, (await Get(_owner)).Status); Assert.Equal(200, (await Get(_admin)).Status);
    }

    [Fact]
    public async Task AMusicIsSavedCleanReadBackAndTurnedOffByAnEmptyAddress()
    {
        var first = (await Get(_owner)).Body; Assert.Equal(JsonValueKind.Null, first.GetProperty("music").ValueKind); Assert.False(first.GetProperty("video").GetProperty("supported").GetBoolean());
        var (status, body) = await Save(_owner, "  https://cdn.example.com/radio/a.ogg  ", .456f, false, "  Rádio da Praia  ");
        Assert.Equal(200, status); var music = body.GetProperty("music");
        Assert.Equal("https://cdn.example.com/radio/a.ogg", music.GetProperty("url").GetString()); Assert.Equal(.46, music.GetProperty("volume").GetDouble(), 2); Assert.False(music.GetProperty("autoplay").GetBoolean()); Assert.Equal("Rádio da Praia", music.GetProperty("name").GetString());
        var entry = await _db.IslandAudit.SingleAsync(); Assert.Equal("midia", entry.Category); Assert.Equal("{}", entry.BeforeJson); Assert.Contains("a.ogg", entry.AfterJson);
        Assert.Equal(200, (await Save(_owner, "")).Status); Assert.Equal("{}", (await _db.Islands.AsNoTracking().SingleAsync()).MediaJson);
        Assert.Contains("Tirou", (await _db.IslandAudit.OrderBy(e => e.CreatedAt).LastAsync()).Summary);
    }

    [Fact]
    public async Task AddressesThatWouldPointIntoANetworkOrAtTheWrongKindOfFileAreRefused()
    {
        foreach (var bad in new[] { "http://cdn.example.com/a.mp3", "ftp://cdn.example.com/a.mp3", "https://user:pass@cdn.example.com/a.mp3", "https://localhost/a.mp3", "https://127.0.0.1/a.mp3", "https://192.168.0.5/a.mp3", "https://10.0.0.1/a.mp3", "https://[::1]/a.ogg", "https://postgres/a.mp3", "https://server.internal/a.mp3", "https://cdn.example.com/a.wav", "https://cdn.example.com/a", "https://cdn.example.com/" + new string('x', 520) + ".mp3", "not an address" })
        {
            var (status, body) = await Save(_owner, bad);
            Assert.True(status is 400 or 422, bad); Assert.True(body.GetProperty("errors").TryGetProperty("musicUrl", out _), bad);
        }
        Assert.Equal("{}", (await _db.Islands.AsNoTracking().SingleAsync()).MediaJson); Assert.Equal(0, await _db.IslandAudit.CountAsync());
    }

    [Fact]
    public async Task VolumeAndNameHaveLimits_AndAnOlderVersionCannotOverwriteANewerOne()
    {
        Assert.True((await Save(_owner, "https://cdn.example.com/a.mp3", 1.5f)).Status is 400 or 422); Assert.True((await Save(_owner, "https://cdn.example.com/a.mp3", -.1f)).Status is 400 or 422);
        Assert.True((await Save(_owner, "https://cdn.example.com/a.mp3", float.NaN)).Status is 400 or 422); Assert.True((await Save(_owner, "https://cdn.example.com/a.mp3", .5f, true, new string('n', 81))).Status is 400 or 422);
        var old = (await _db.Islands.AsNoTracking().SingleAsync()).UpdatedAt;
        Assert.Equal(200, (await Save(_owner, "https://cdn.example.com/b.mp3")).Status);
        var (status, body) = await Save(_owner, "https://cdn.example.com/c.mp3", expected: old); Assert.Equal(409, status); Assert.Equal("stale", body.GetProperty("error").GetString());
        Assert.Contains("b.mp3", (await _db.Islands.AsNoTracking().SingleAsync()).MediaJson);
    }

    [Fact]
    public async Task TheGameReceivesTheMusicWithTheLook()
    {
        await Save(_owner, "https://cdn.example.com/a.mp3", .7f, true, "Rádio");
        var read = Read(await IslandEnvironmentManagement.ReadAsync(_island, As(_visitor), _db, _clock, default));
        Assert.Equal(200, read.Status); Assert.Equal("https://cdn.example.com/a.mp3", read.Body.GetProperty("media").GetProperty("musicUrl").GetString()); Assert.Equal(.7, read.Body.GetProperty("media").GetProperty("volume").GetDouble(), 2);
    }
}
