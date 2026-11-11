using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The Física tab: which objects are simulated, freezing and releasing them without losing their settings, what the island server reports, and that the island server hears about it.</summary>
public sealed class IslandPhysicsTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly TimeProvider _clock;
    private readonly Guid _owner, _carla, _stranger, _island, _instance = Guid.NewGuid();

    private sealed class Clock(Func<DateTimeOffset> now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now(); }
    private const string Physical = "{\"mode\":\"physical\",\"mass\":5,\"shape\":\"box\"}";

    public IslandPhysicsTests()
    {
        _clock = new Clock(() => _now);
        (_owner, _carla, _stranger) = (User("alice"), User("carla"), User("edu"));
        _island = Guid.NewGuid();
        _db.Islands.Add(new Island { Id = _island, OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, MaxPlayers = 10, CreatedAt = _now, UpdatedAt = _now });
        _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = _island, Hostname = "h", Port = 1, Status = "healthy", StartedAt = _now.AddHours(-1), LastHeartbeatAt = _now });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Guid User(string name)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now });
        _db.SaveChanges(); return id;
    }

    private Guid Make(Guid owner, string name, string physics, int cost = 10)
    {
        var id = Guid.NewGuid();
        _db.BuildObjects.Add(new BuildObject { Id = id, IslandId = _island, CreatorUserId = owner, OwnerUserId = owner, Name = name, PrimitiveType = "cube", Cost = cost, PhysicsJson = physics, CreatedAt = _now, UpdatedAt = _now });
        _db.SaveChanges(); return id;
    }

    private static HttpContext As(Guid user) => new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, user.ToString())], "test")) };

    private static (int Status, JsonElement Body) Read(IResult result)
    {
        var status = result is Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult ? 403 : (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
        var value = (result as IValueHttpResult)?.Value;
        return (status, value is null ? default : JsonSerializer.SerializeToElement(value));
    }

    private async Task<(int Status, JsonElement Body)> Get(Guid who, string? q = null, string? state = null, int page = 1) => Read(await IslandPhysicsManagement.GetAsync(_island, q, state, page, As(who), _db, _clock, default));
    private async Task<(int Status, JsonElement Body)> Freeze(Guid who, params Guid[] ids) => Read(await IslandPhysicsManagement.FreezeAsync(_island, new FreezeRequest(ids, false), As(who), _db, _clock, default));
    private async Task<(int Status, JsonElement Body)> Release(Guid who, params Guid[] ids) => Read(await IslandPhysicsManagement.ReleaseAsync(_island, new FreezeRequest(ids, false), As(who), _db, _clock, default));
    private async Task<BuildObject> Object(Guid id) => await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id);

    [Fact]
    public async Task OnlyStaffLookAndAct()
    {
        var ball = Make(_carla, "Bola", Physical);
        Assert.Equal(403, (await Get(_carla)).Status); Assert.Equal(403, (await Freeze(_stranger, ball)).Status); Assert.Equal(403, (await Release(_carla, ball)).Status);
        Assert.Equal(200, (await Get(_owner)).Status); Assert.Equal("physical", (await Object(ball)).PhysicsJson.Contains("physical") ? "physical" : "frozen");
    }

    [Fact]
    public async Task TheListShowsOnlyWhatIsSimulatedWithItsStateAndTheSummary()
    {
        Make(_carla, "Bola", Physical, 30); Make(_carla, "Parede", "{\"mode\":\"static\"}", 50); Make(_owner, "Caixa", Physical, 20); Make(_owner, "Nada", "{}", 5);
        var body = (await Get(_owner)).Body; var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { "Bola", "Caixa" }, items.Select(i => i.GetProperty("name").GetString()!)); // heaviest first; static objects are not simulated
        Assert.Equal((2, 2, 0), (body.GetProperty("summary").GetProperty("simulated").GetInt32(), body.GetProperty("summary").GetProperty("active").GetInt32(), body.GetProperty("summary").GetProperty("frozen").GetInt32()));
        Assert.Equal("carla", items[0].GetProperty("ownerName").GetString()); Assert.Equal("active", items[0].GetProperty("state").GetString());
        Assert.Equal(1, (await Get(_owner, "carla")).Body.GetProperty("total").GetInt32()); Assert.Equal(1, (await Get(_owner, "caixa")).Body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task FreezingMakesTheObjectStaticAndReleasingPutsItBackExactly()
    {
        var ball = Make(_carla, "Bola", Physical); var rev = (await Object(ball)).Revision;
        var (status, body) = await Freeze(_owner, ball);
        Assert.Equal(200, status); Assert.Equal(1, body.GetProperty("changed").GetInt32());
        var frozen = await Object(ball); Assert.Contains("static", frozen.PhysicsJson); Assert.Contains("\"mass\":5", frozen.PhysicsJson); Assert.Equal(Physical, frozen.FrozenPhysicsJson); Assert.Equal(rev + 1, frozen.Revision);
        Assert.Equal("frozen", (await Get(_owner)).Body.GetProperty("items")[0].GetProperty("state").GetString());
        Assert.Equal(0, (await Freeze(_owner, ball)).Body.GetProperty("changed").GetInt32()); // already frozen: nothing to do
        var (releaseStatus, released) = await Release(_owner, ball);
        Assert.Equal(200, releaseStatus); Assert.Equal((1, 1, 0), (released.GetProperty("changed").GetInt32(), released.GetProperty("restored").GetInt32(), released.GetProperty("kept").GetInt32()));
        var back = await Object(ball); Assert.Equal(Physical, back.PhysicsJson); Assert.Null(back.FrozenPhysicsJson); Assert.Equal(rev + 2, back.Revision);
    }

    [Fact]
    public async Task IfTheOwnerChangedThePhysicsWhileFrozenReleasingKeepsTheirChange()
    {
        var ball = Make(_carla, "Bola", Physical); await Freeze(_owner, ball);
        (await _db.BuildObjects.SingleAsync(o => o.Id == ball)).PhysicsJson = "{\"mode\":\"static\",\"mass\":99,\"shape\":\"sphere\"}"; await _db.SaveChangesAsync();
        var released = (await Release(_owner, ball)).Body; Assert.Equal((1, 0, 1), (released.GetProperty("changed").GetInt32(), released.GetProperty("restored").GetInt32(), released.GetProperty("kept").GetInt32()));
        var after = await Object(ball); Assert.Contains("99", after.PhysicsJson); Assert.Null(after.FrozenPhysicsJson);
    }

    [Fact]
    public async Task FreezingEverythingAtOnceOnlyTouchesTheObjectsThatMove_AndAnythingElseIsLeftAlone()
    {
        var a = Make(_carla, "A", Physical); var b = Make(_owner, "B", Physical); var wall = Make(_owner, "Parede", "{\"mode\":\"static\"}"); var other = Guid.NewGuid();
        var elsewhere = Make(_owner, "Outra", Physical); (await _db.BuildObjects.SingleAsync(o => o.Id == elsewhere)).IslandId = Guid.NewGuid(); await _db.SaveChangesAsync();
        Assert.Equal(2, Read(await IslandPhysicsManagement.FreezeAsync(_island, new FreezeRequest(null, true), As(_owner), _db, _clock, default)).Body.GetProperty("changed").GetInt32());
        Assert.NotNull((await Object(a)).FrozenPhysicsJson); Assert.NotNull((await Object(b)).FrozenPhysicsJson); Assert.Null((await Object(wall)).FrozenPhysicsJson); Assert.Null((await Object(elsewhere)).FrozenPhysicsJson);
        Assert.Equal(0, (await Freeze(_owner, wall, other)).Body.GetProperty("changed").GetInt32()); // static or unknown: nothing
        Assert.True((Read(await IslandPhysicsManagement.FreezeAsync(_island, new FreezeRequest([], false), As(_owner), _db, _clock, default))).Status is 400 or 422);
        Assert.Equal(2, Read(await IslandPhysicsManagement.ReleaseAsync(_island, new FreezeRequest(null, true), As(_owner), _db, _clock, default)).Body.GetProperty("changed").GetInt32());
    }

    [Fact]
    public async Task EveryChangeLeavesASyncOperationForTheIslandServerAndAnAuditEntry()
    {
        var ball = Make(_carla, "Bola", Physical); await Freeze(_owner, ball); await Release(_owner, ball);
        var operations = await _db.BuildOperations.Where(o => o.ObjectId == ball).OrderBy(o => o.Revision).ToListAsync();
        Assert.All(operations, o => Assert.Equal("physics_admin", o.Operation)); Assert.Equal(2, operations.Count); Assert.Contains("freeze", operations[0].PayloadJson); Assert.Contains("release", operations[1].PayloadJson);
        Assert.Equal(["physics_frozen", "physics_released"], (await _db.IslandAudit.OrderBy(e => e.CreatedAt).ToListAsync()).Select(e => e.Action).OrderByDescending(x => x).Reverse().ToList().OrderBy(x => x == "physics_released").ToList());
        // and the sync the island server asks for every few seconds carries them as an update of the object (that is how it learns the ball no longer moves)
        await Freeze(_owner, ball);
        var answer = await LifeVerse.Api.Modules.Land.LandSync.BuildAsync(_db, _instance, _now.AddMinutes(-1).UtcTicks, _clock, default);
        var revision = (await Object(ball)).Revision;
        var update = answer!.Events.First(e => e.GetProperty("operation").GetString() == "update" && e.GetProperty("objectId").GetGuid() == ball && e.GetProperty("revision").GetInt32() == revision); // one per operation, each carrying the current state
        Assert.Contains("static", update.GetProperty("objectState").GetProperty("physicsJson").GetString());
    }

    [Fact]
    public async Task TheIslandServerFiguresAreShownOnlyWhileFresh()
    {
        Assert.Equal(JsonValueKind.Null, (await Get(_owner)).Body.GetProperty("server").ValueKind);
        await ServerChannel.StoreReportAsync(_db, _instance, new ServerReport([], 60, 3, 100, 10, 60, 12, 40, 200, 1.7), _now, default);
        var server = (await Get(_owner)).Body.GetProperty("server");
        Assert.Equal((12, 40, 200, 1.7), (server.GetProperty("bodies").GetInt32(), server.GetProperty("fixedBodies").GetInt32(), server.GetProperty("maxBodies").GetInt32(), server.GetProperty("physicsMs").GetDouble()));
        _now = _now.AddSeconds(90); (await _db.ServerInstances.SingleAsync()).LastHeartbeatAt = _now; await _db.SaveChangesAsync();
        Assert.Equal(JsonValueKind.Null, (await Get(_owner)).Body.GetProperty("server").ValueKind);
    }
}
