using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The Objetos e Prims tab: the numbers, the list with filters, the consumption by owner, the details and returning objects without losing them.</summary>
public sealed class IslandObjectsTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly TimeProvider _clock;
    private readonly AssetStorageService _storage = new(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
    private readonly Guid _owner, _admin, _carla, _duda, _stranger, _island;

    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    public IslandObjectsTests()
    {
        _clock = new Clock(_now);
        (_owner, _admin, _carla, _duda, _stranger) = (User("alice"), User("bruno"), User("carla"), User("duda"), User("edu"));
        _island = Guid.NewGuid();
        _db.Islands.Add(new Island { Id = _island, OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, MaxPlayers = 10, CreatedAt = _now, UpdatedAt = _now, ResourceLimitsJson = "{\"cost\":1000,\"objects\":50}" });
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

    private Guid Make(Guid owner, string name, int cost, string type = "cube", int scripts = 0, string physics = "{}", int minutes = 0)
    {
        var id = Guid.NewGuid();
        _db.BuildObjects.Add(new BuildObject { Id = id, IslandId = _island, CreatorUserId = owner, OwnerUserId = owner, Name = name, PrimitiveType = type, Cost = cost, PhysicsJson = physics, PositionX = 1, PositionY = 2, PositionZ = 3, CreatedAt = _now.AddMinutes(minutes), UpdatedAt = _now });
        for (var i = 0; i < scripts; i++) _db.ObjectScripts.Add(new ObjectScript { Id = Guid.NewGuid(), ObjectId = id, OwnerUserId = owner, Name = "s" + i, Source = "", CreatedAt = _now, UpdatedAt = _now });
        _db.SaveChanges(); return id;
    }

    private static HttpContext As(Guid user) => new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, user.ToString())], "test")) };

    private static (int Status, JsonElement Body) Read(IResult result)
    {
        var status = result is Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult ? 403 : (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
        var value = (result as IValueHttpResult)?.Value;
        return (status, value is null ? default : JsonSerializer.SerializeToElement(value));
    }

    private async Task<(int Status, JsonElement Body)> List(Guid who, string? q = null, Guid? owner = null, string? type = null, string? scripts = null, string? sort = null, int page = 1) =>
        Read(await IslandObjectManagement.ListAsync(_island, q, owner, type, scripts, sort, page, As(who), _db, _clock, default));

    private void Fill()
    {
        Make(_carla, "Árvore", 40, "mesh", scripts: 2, minutes: 1); Make(_carla, "Banco", 10, minutes: 2); Make(_duda, "Bola", 25, "sphere", physics: "{\"mode\":\"physical\"}", minutes: 3); Make(_owner, "Casa", 300, "mesh", minutes: 4);
    }

    [Fact]
    public async Task OnlyTheOwnerAndAdministratorsSee()
    {
        Fill();
        Assert.Equal(403, (await List(_stranger)).Status); Assert.Equal(403, (await List(_carla)).Status);
        Assert.Equal(200, (await List(_owner)).Status); Assert.Equal(200, (await List(_admin)).Status);
    }

    [Fact]
    public async Task TheNumbersAreTheRealBudgetOfTheIsland()
    {
        Fill();
        var usage = (await List(_owner)).Body.GetProperty("usage");
        Assert.Equal(4, usage.GetProperty("objects").GetInt32()); Assert.Equal(375, usage.GetProperty("cost").GetInt64());
        Assert.Equal(1000, usage.GetProperty("maxCost").GetInt64()); Assert.Equal(50, usage.GetProperty("maxObjects").GetInt32());
    }

    [Fact]
    public async Task TheListHasOwnersScriptsPhysicsFiltersSearchSortAndPages()
    {
        Fill();
        var body = (await List(_owner)).Body; var items = body.GetProperty("items");
        Assert.Equal("Casa", items[0].GetProperty("name").GetString()); // biggest cost first
        Assert.Equal("alice", items[0].GetProperty("ownerName").GetString());
        var tree = (await List(_owner, "árvore")).Body.GetProperty("items")[0]; Assert.Equal(2, tree.GetProperty("scripts").GetInt32());
        Assert.Equal("physical", (await List(_owner, "bola")).Body.GetProperty("items")[0].GetProperty("physics").GetString());
        Assert.Equal(2, (await List(_owner, owner: _carla)).Body.GetProperty("total").GetInt32());
        Assert.Equal(2, (await List(_owner, type: "mesh")).Body.GetProperty("total").GetInt32());
        Assert.Equal(1, (await List(_owner, scripts: "with")).Body.GetProperty("total").GetInt32()); Assert.Equal(3, (await List(_owner, scripts: "without")).Body.GetProperty("total").GetInt32());
        Assert.Equal(2, (await List(_owner, "carla")).Body.GetProperty("total").GetInt32()); // the owner name is searched too
        Assert.Equal("Casa", (await List(_owner, sort: "recent")).Body.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal("Árvore", (await List(_owner, sort: "name")).Body.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Contains("sphere", (await List(_owner)).Body.GetProperty("types").EnumerateArray().Select(t => t.GetString()));
        for (var i = 0; i < 9; i++) Make(_duda, $"Extra{i}", 1);
        Assert.Equal(10, (await List(_owner)).Body.GetProperty("items").GetArrayLength()); Assert.Equal(3, (await List(_owner, page: 2)).Body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task TheConsumptionByOwnerAddsUpObjectsPrimsAndScripts()
    {
        Fill();
        var rows = Read(await IslandObjectManagement.OwnersAsync(_island, As(_owner), _db, _clock, default)).Body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal("alice", rows[0].GetProperty("name").GetString()); Assert.Equal(300, rows[0].GetProperty("cost").GetInt64());
        var carla = rows.Single(r => r.GetProperty("name").GetString() == "carla"); Assert.Equal((2, 50L, 2), (carla.GetProperty("objects").GetInt32(), carla.GetProperty("cost").GetInt64(), carla.GetProperty("scripts").GetInt32()));
    }

    [Fact]
    public async Task TheDetailsShowWhoMadeAndWhoOwnsIt()
    {
        var id = Make(_carla, "Banco", 10); (await _db.BuildObjects.SingleAsync(o => o.Id == id)).LastOwnerUserId = _duda; await _db.SaveChangesAsync();
        var d = Read(await IslandObjectManagement.DetailAsync(_island, id, As(_admin), _db, _clock, default)).Body;
        Assert.Equal("carla", d.GetProperty("ownerName").GetString()); Assert.Equal("carla", d.GetProperty("creatorName").GetString()); Assert.Equal("duda", d.GetProperty("lastOwnerName").GetString());
        Assert.Equal(3, d.GetProperty("position").GetProperty("z").GetDouble());
        Assert.Equal(404, Read(await IslandObjectManagement.DetailAsync(_island, Guid.NewGuid(), As(_admin), _db, _clock, default)).Status);
        Assert.Equal(403, Read(await IslandObjectManagement.DetailAsync(_island, id, As(_stranger), _db, _clock, default)).Status);
    }

    [Fact]
    public async Task ReturningTakesTheObjectOffTheIslandAndKeepsItInTheInventoryOfItsOwner()
    {
        var banco = Make(_carla, "Banco", 10); var mesa = Make(_carla, "Mesa", 20); var bola = Make(_duda, "Bola", 5);
        var result = Read(await IslandObjectManagement.ReturnAsync(_island, new ReturnObjectsRequest([banco, mesa]), As(_admin), _db, _storage, _clock, default));
        Assert.Equal(200, result.Status); Assert.Equal(2, result.Body.GetProperty("returned").GetInt32());
        Assert.Equal(1, await _db.BuildObjects.CountAsync(o => o.IslandId == _island && o.DeletedAt == null)); Assert.True((await _db.BuildObjects.SingleAsync(o => o.Id == bola)).DeletedAt is null);
        Assert.True(await _db.InventoryItems.AnyAsync(i => i.OwnerUserId == _carla), "the owner got the objects back in the inventory");
        Assert.Equal(403, Read(await IslandObjectManagement.ReturnAsync(_island, new ReturnObjectsRequest([bola]), As(_stranger), _db, _storage, _clock, default)).Status);
        Assert.True((Read(await IslandObjectManagement.ReturnAsync(_island, new ReturnObjectsRequest([]), As(_admin), _db, _storage, _clock, default)).Status) is 400 or 422);
    }

    [Fact]
    public async Task ReturningEverythingOfOnePersonLeavesTheOthers()
    {
        Fill(); for (var i = 0; i < 5; i++) Make(_carla, $"Peca{i}", 1);
        var result = Read(await IslandObjectManagement.ReturnOwnerAsync(_island, new ReturnOwnerRequest(_carla), As(_admin), _db, _storage, _clock, default));
        Assert.Equal(200, result.Status); Assert.Equal(7, result.Body.GetProperty("returned").GetInt32()); Assert.Equal(0, result.Body.GetProperty("remaining").GetInt32());
        Assert.Equal(2, await _db.BuildObjects.CountAsync(o => o.IslandId == _island && o.DeletedAt == null));
        Assert.Equal(409, Read(await IslandObjectManagement.ReturnOwnerAsync(_island, new ReturnOwnerRequest(_owner), As(_admin), _db, _storage, _clock, default)).Status); // an administrator does not clear the owner
        Assert.Equal(403, Read(await IslandObjectManagement.ReturnOwnerAsync(_island, new ReturnOwnerRequest(_duda), As(_carla), _db, _storage, _clock, default)).Status);
    }
}
