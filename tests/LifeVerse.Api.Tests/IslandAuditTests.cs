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

/// <summary>The Histórico e Auditoria tab: every management action leaves an entry with who, what and the values before and after; only staff read it; filters work; build history is included.</summary>
public sealed class IslandAuditTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly TimeProvider _clock;
    private readonly AssetStorageService _storage = new(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
    private readonly Guid _owner, _admin, _carla, _stranger, _island;

    private sealed class Clock(Func<DateTimeOffset> now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now(); }

    public IslandAuditTests()
    {
        _clock = new Clock(() => _now);
        (_owner, _admin, _carla, _stranger) = (User("alice"), User("bruno"), User("carla"), User("edu"));
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

    private async Task<(int Status, JsonElement Body)> Audit(Guid who, int? hours = null, string? user = null, string? category = null, string? q = null, int page = 1) =>
        Read(await IslandAuditManagement.ListAsync(_island, hours, user, category, q, page, As(who), _db, _clock, default));

    private async Task Save(Func<IslandGeneralRequest, IslandGeneralRequest> change)
    {
        var island = await _db.Islands.AsNoTracking().SingleAsync();
        var request = new IslandGeneralRequest(island.Name, island.Description, island.CoverImageUrl, "social", "livre", "pt-BR", island.MaxPlayers, true, true, island.SpawnX, island.SpawnZ, island.UpdatedAt);
        Assert.Equal(200, Read(await IslandManagement.SaveGeneralAsync(_island, change(request), As(_owner), _db, _clock, default)).Status);
    }

    [Fact]
    public async Task OnlyStaffReadTheHistory()
    {
        Assert.Equal(403, (await Audit(_stranger)).Status); Assert.Equal(403, (await Audit(_carla)).Status);
        Assert.Equal(200, (await Audit(_owner)).Status); Assert.Equal(200, (await Audit(_admin)).Status);
    }

    [Fact]
    public async Task ChangingTheSettingsLeavesAnEntryWithTheValuesBeforeAndAfter_AndSavingNothingLeavesNone()
    {
        await Save(r => r);
        Assert.Equal(0, (await Audit(_owner)).Body.GetProperty("total").GetInt32());
        _now = _now.AddMinutes(1); await Save(r => r with { Name = "Ilha Nova", MaxPlayers = 20 });
        var list = (await Audit(_owner)).Body; Assert.Equal(1, list.GetProperty("total").GetInt32());
        var item = list.GetProperty("items")[0]; Assert.Equal("alice", item.GetProperty("actorName").GetString()); Assert.Equal("geral", item.GetProperty("category").GetString()); Assert.Contains("nome", item.GetProperty("summary").GetString());
        var detail = Read(await IslandAuditManagement.DetailAsync(_island, item.GetProperty("id").GetGuid(), As(_owner), _db, _clock, default)).Body;
        Assert.Equal("Ilha", detail.GetProperty("before").GetProperty("name").GetString()); Assert.Equal("Ilha Nova", detail.GetProperty("after").GetProperty("name").GetString());
        Assert.Equal(10, detail.GetProperty("before").GetProperty("maxPlayers").GetInt32()); Assert.Equal(20, detail.GetProperty("after").GetProperty("maxPlayers").GetInt32());
        Assert.Equal("Ilha Nova", detail.GetProperty("island").GetString()); Assert.NotEqual(Guid.Empty, detail.GetProperty("correlationId").GetGuid());
    }

    [Fact]
    public async Task AccessBansAndPermissionsAreAllRecorded()
    {
        var owner = As(_owner);
        Read(await IslandAccessManagement.AllowAsync(_island, new AllowRequest("carla", null), owner, _db, _clock, default));
        var entry = await _db.IslandPermissions.SingleAsync(p => p.UserId == _carla && p.Permission == "enter");
        Read(await IslandAccessManagement.ChangeAsync(_island, entry.Id, new AllowChangeRequest(_now.AddDays(1)), owner, _db, _clock, default));
        Read(await IslandAccessManagement.RemoveAsync(_island, entry.Id, owner, _db, _clock, default));
        Read(await IslandAccessManagement.SetModeAsync(_island, new AccessModeRequest("public", (await _db.Islands.AsNoTracking().SingleAsync()).UpdatedAt), owner, _db, _clock, default));
        var ban = Read(await IslandBanManagement.BanAsync(_island, new BanRequest("carla", "Spam", null), As(_admin), _db, _clock, default)).Body.GetProperty("banId").GetGuid();
        Read(await IslandBanManagement.ChangeAsync(_island, ban, new BanChangeRequest(_now.AddDays(2)), As(_admin), _db, _clock, default));
        Read(await IslandBanManagement.LiftAsync(_island, ban, As(_admin), _db, _clock, default));
        Read(await IslandPermissionManagement.AddAsync(_island, new PermissionSetRequest("carla", ["build"]), As(_admin), _db, _clock, default));
        Read(await IslandPermissionManagement.ChangeAsync(_island, _carla, new PermissionChangeRequest(["build", "ban"]), As(_admin), _db, _clock, default));
        Read(await IslandPermissionManagement.ClearAsync(_island, _carla, As(_admin), _db, _clock, default));
        var actions = await _db.IslandAudit.OrderBy(e => e.CreatedAt).Select(e => e.Action).ToListAsync();
        Assert.Equal(new[] { "allow_added", "allow_changed", "allow_removed", "mode_changed", "ban_added", "ban_changed", "ban_lifted", "permissions_changed", "permissions_changed", "permissions_changed" }.OrderBy(x => x), actions.OrderBy(x => x));
        var mode = await _db.IslandAudit.SingleAsync(e => e.Action == "mode_changed"); Assert.Contains("invite", mode.BeforeJson); Assert.Contains("public", mode.AfterJson);
        var perms = await _db.IslandAudit.Where(e => e.Action == "permissions_changed").Select(e => e.Summary).ToListAsync(); Assert.Contains(perms, s => s.StartsWith("Tirou todas"));
        var ban2 = await _db.IslandAudit.SingleAsync(e => e.Action == "ban_added"); Assert.Equal("bruno", await IslandAudit.NameAsync(_db, ban2.ActorUserId)); Assert.Contains("Spam", ban2.AfterJson);
    }

    [Fact]
    public async Task RefusedActionsLeaveNoEntryAndReturningObjectsIsRecorded()
    {
        Read(await IslandBanManagement.BanAsync(_island, new BanRequest("alice", "x", null), As(_admin), _db, _clock, default));
        Read(await IslandPermissionManagement.AddAsync(_island, new PermissionSetRequest("carla", ["admin"]), As(_admin), _db, _clock, default));
        Assert.Equal(0, await _db.IslandAudit.CountAsync());
        var id = Guid.NewGuid(); _db.BuildObjects.Add(new BuildObject { Id = id, IslandId = _island, CreatorUserId = _carla, OwnerUserId = _carla, Name = "Banco", PrimitiveType = "cube", Cost = 5, CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync();
        Assert.Equal(200, Read(await IslandObjectManagement.ReturnAsync(_island, new ReturnObjectsRequest([id]), As(_owner), _db, _storage, _clock, default)).Status);
        var entry = await _db.IslandAudit.SingleAsync(); Assert.Equal("objeto", entry.Category); Assert.Contains("Banco", entry.Summary);
    }

    [Fact]
    public async Task FiltersByPeriodPersonCategoryAndText_AndPaging()
    {
        _now = _now.AddDays(-10); await Save(r => r with { Name = "Antiga" });
        _now = _now.AddDays(10);
        Read(await IslandBanManagement.BanAsync(_island, new BanRequest("carla", "Fraude", null), As(_admin), _db, _clock, default));
        await Save(r => r with { Description = "Nova" });
        Assert.Equal(3, (await Audit(_owner)).Body.GetProperty("total").GetInt32());
        Assert.Equal(2, (await Audit(_owner, hours: 24)).Body.GetProperty("total").GetInt32());
        Assert.Equal(1, (await Audit(_owner, user: "bruno")).Body.GetProperty("total").GetInt32());
        Assert.Equal(1, (await Audit(_owner, category: "banimento")).Body.GetProperty("total").GetInt32());
        Assert.Equal(1, (await Audit(_owner, q: "carla")).Body.GetProperty("total").GetInt32());
        Assert.Contains("Baniu", (await Audit(_owner, category: "banimento")).Body.GetProperty("items")[0].GetProperty("summary").GetString());
        for (var i = 0; i < 12; i++) { _now = _now.AddMinutes(1); await Save(r => r with { Description = "d" + i }); }
        var first = (await Audit(_owner)).Body; Assert.Equal(15, first.GetProperty("total").GetInt32()); Assert.Equal(10, first.GetProperty("items").GetArrayLength());
        Assert.Equal(5, (await Audit(_owner, page: 2)).Body.GetProperty("items").GetArrayLength());
        var times = first.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("at").GetDateTimeOffset()).ToList(); Assert.Equal(times.OrderByDescending(t => t), times);
    }

    [Fact]
    public async Task TheBuildHistoryIsPartOfIt()
    {
        var id = Guid.NewGuid(); _db.BuildObjects.Add(new BuildObject { Id = id, IslandId = _island, CreatorUserId = _carla, OwnerUserId = _carla, Name = "Mesa", PrimitiveType = "cube", Cost = 5, CreatedAt = _now, UpdatedAt = _now });
        var op = Guid.NewGuid(); _db.BuildOperations.Add(new BuildOperation { Id = op, IslandId = _island, ObjectId = id, ActorUserId = _carla, Operation = "create", PayloadJson = "{\"name\":\"Mesa\"}", Revision = 1, CreatedAt = _now.AddMinutes(1) });
        await _db.SaveChangesAsync(); await Save(r => r with { Name = "Outra" });
        var all = (await Audit(_owner)).Body; Assert.Equal(2, all.GetProperty("total").GetInt32()); Assert.Equal("construcao", all.GetProperty("items")[0].GetProperty("category").GetString()); Assert.Equal("Criou o objeto Mesa", all.GetProperty("items")[0].GetProperty("summary").GetString());
        Assert.Equal(1, (await Audit(_owner, category: "construcao")).Body.GetProperty("total").GetInt32()); Assert.Equal(1, (await Audit(_owner, category: "geral")).Body.GetProperty("total").GetInt32());
        Assert.Equal(1, (await Audit(_owner, q: "mesa")).Body.GetProperty("total").GetInt32());
        var detail = Read(await IslandAuditManagement.DetailAsync(_island, op, As(_admin), _db, _clock, default)).Body; Assert.Equal("carla", detail.GetProperty("actorName").GetString()); Assert.Equal("Mesa", detail.GetProperty("after").GetProperty("name").GetString());
        Assert.Equal(404, Read(await IslandAuditManagement.DetailAsync(_island, Guid.NewGuid(), As(_admin), _db, _clock, default)).Status);
        Assert.Equal(403, Read(await IslandAuditManagement.DetailAsync(_island, op, As(_stranger), _db, _clock, default)).Status);
    }
}
